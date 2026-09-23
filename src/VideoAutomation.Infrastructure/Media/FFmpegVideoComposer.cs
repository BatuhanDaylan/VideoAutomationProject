using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoAutomation.Domain.Entities;
using VideoAutomation.Domain.Enums;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Shared.Exceptions;

namespace VideoAutomation.Infrastructure.Media;

/// <summary>
/// IVideoComposer implementation: per-scene A/B split composition, xfade/acrossfade scene transitions,
/// ASS subtitle burn-in, optional avatar picture-in-picture and BGM mix — all hand-built FFmpeg
/// filter_complex strings run through FFmpegRunner (Xabe's fluent Conversion API can't express graphs
/// this specific, so we bypass it and pass raw arguments).
/// </summary>
public class FFmpegVideoComposer : IVideoComposer
{
    private const int Width = 1080;
    private const int Height = 1920;
    private const int HalfHeight = Height / 2;
    private const int Fps = 30;

    // Per-scene composition is an intermediate that gets re-encoded again during the final xfade/subtitle
    // pass — keep it near-lossless (low CRF) so quality loss only really happens once, at delivery time.
    private const string IntermediateEncode = "-preset medium -crf 16 -c:v libx264 -c:a aac -b:a 192k -pix_fmt yuv420p";
    private const string FinalEncode = "-preset slow -crf 18 -c:v libx264 -c:a aac -b:a 192k -pix_fmt yuv420p";

    /// <summary>
    /// Stand-in audio input for a generated clip that unexpectedly came back silent. Declared as a real
    /// input (not an filtergraph source) so the "-map N:a" indices stay predictable; "-t" on the output
    /// bounds this otherwise-infinite stream.
    /// </summary>
    private const string SilentAudioInput = "-f lavfi -i anullsrc=channel_layout=stereo:sample_rate=44100 ";

    private readonly FFmpegRunner _ffmpegRunner;
    private readonly EdgeTtsService _edgeTts;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FFmpegVideoComposer> _logger;

    public FFmpegVideoComposer(FFmpegRunner ffmpegRunner, EdgeTtsService edgeTts, IConfiguration configuration, ILogger<FFmpegVideoComposer> logger)
    {
        _ffmpegRunner = ffmpegRunner;
        _edgeTts = edgeTts;
        _configuration = configuration;
        _logger = logger;
    }

    public Task<string> GenerateTTSAsync(string text, string voice, string outputPath, CancellationToken ct = default)
        => _edgeTts.GenerateAsync(text, voice, outputPath, ct);

    public async Task RemoveSilenceAsync(string audioPath, string outputPath, CancellationToken ct = default)
    {
        var thresholdDb = _configuration.GetValue("Media:SilenceThresholdDb", -40);
        var filter =
            $"silenceremove=start_periods=1:start_duration=0:start_threshold={thresholdDb}dB:detection=peak," +
            $"silenceremove=stop_periods=1:stop_duration=0.3:stop_threshold={thresholdDb}dB:detection=peak";

        var args = $"-i \"{audioPath}\" -af \"{filter}\" \"{outputPath}\"";
        await _ffmpegRunner.RunAsync(args, ct);
    }

    public async Task<string> ComposeVideoAsync(Video video, Template template, CancellationToken ct = default)
    {
        var scenes = video.Scenes.OrderBy(s => s.OrderIndex).ToList();
        if (scenes.Count == 0)
            throw new VideoGenerationException($"Video {video.Id} için sahne bulunamadı.");

        var assetsDir = _configuration.GetValue("Pipeline:AssetsDirectory", "assets")!;
        var outputDir = _configuration.GetValue("Pipeline:OutputDirectory", "output")!;
        var workDir = Path.Combine(assetsDir, "work", video.Id.ToString());
        Directory.CreateDirectory(workDir);
        Directory.CreateDirectory(outputDir);

        var nativeAudio = video.VisualMode.UsesNativeAudio();

        // What drives the real scene length differs by mode — Gemini's estimate (Faz 3) is only ever a seed.
        foreach (var scene in scenes)
        {
            // Narrated: the TTS clip's length. Native: the generated clip's own length, since its audio is
            // baked into the video and can't be stretched to fit some other target.
            var source = nativeAudio ? scene.VideoFilePathA : scene.AudioFilePath;
            if (string.IsNullOrEmpty(source))
                throw new VideoGenerationException(
                    $"Sahne {scene.OrderIndex} için {(nativeAudio ? "görsel" : "ses")} dosyası yok.");
            var duration = await _ffmpegRunner.GetDurationAsync(source, ct);
            scene.DurationSeconds = Math.Max(duration.TotalSeconds, 1.0);
        }

        var transitionDuration = Math.Min(
            _configuration.GetValue("Media:TransitionDurationSeconds", 0.5),
            scenes.Min(s => s.DurationSeconds) * 0.3);

        if (nativeAudio)
        {
            // The narrated path gets its xfade surplus for free by stream_loop'ing a silent stock clip past
            // its real length. A generated clip can't be extended that way — looping it would repeat its
            // audio — so the surplus is instead *reserved out of* each non-last clip's own duration, which
            // keeps BuildComposition's invariant (final length == sum of DurationSeconds) intact either way.
            for (var i = 0; i < scenes.Count - 1; i++)
                scenes[i].DurationSeconds = Math.Max(scenes[i].DurationSeconds - transitionDuration, 1.0);
        }

        for (var i = 0; i < scenes.Count; i++)
            await ComposeSceneAsync(scenes[i], template, isLast: i == scenes.Count - 1, transitionDuration, nativeAudio, workDir, ct);

        // Subtitles follow the real (untrimmed, gapless) audio timeline, not the xfade-shortened video
        // timeline — audio is no longer time-compressed to match xfade (see BuildComposition), so this is
        // just a plain cumulative sum of scene durations.
        // Subtitles ONLY on the narrated (Pexels/StockVideo) path. AI-generated (native-audio) videos never
        // get subtitles by design — there's no narration to caption and burned text would fight the model's
        // own on-screen/spoken dialogue. (User rule: altyazı sadece Pexels'te, AI üretiminde asla.)
        var burnSubtitles = !nativeAudio;
        var timeline = ComputeSceneTimeline(scenes);
        var assPath = burnSubtitles ? BuildAssSubtitleFile(scenes, timeline, template, workDir) : null;

        var (chainArgs, totalDuration) = BuildComposition(scenes, transitionDuration, assPath, template, nativeAudio);

        var finalPath = Path.Combine(outputDir, $"{video.Id}.mp4");
        var thumbPath = Path.Combine(outputDir, $"{video.Id}_thumb.jpg");

        await _ffmpegRunner.RunAsync($"{chainArgs} -t {totalDuration.ToString(CultureInfo.InvariantCulture)} \"{finalPath}\"", ct);
        // Thumbnail: let ffmpeg's `thumbnail` filter pick the most representative frame from a batch instead of
        // grabbing a fixed 1s frame (which often landed on a blur/transition). n=300 ≈ first ~10s at 30fps.
        await _ffmpegRunner.RunAsync($"-i \"{finalPath}\" -vf \"thumbnail=n=300\" -frames:v 1 \"{thumbPath}\"", ct);

        var actualDuration = await _ffmpegRunner.GetDurationAsync(finalPath, ct);

        video.OutputFilePath = finalPath;
        video.ThumbnailPath = thumbPath;
        video.DurationSeconds = (int)Math.Round(actualDuration.TotalSeconds);

        return finalPath;
    }

    // ── Per-scene composition (visual(s) + audio → one clip per scene) ─────────

    private async Task ComposeSceneAsync(Scene scene, Template template, bool isLast, double transitionDuration, bool nativeAudio, string workDir, CancellationToken ct)
    {
        // Non-last scenes render `transitionDuration` MORE video than their real (audio) length, and the
        // next xfade "eats" exactly this surplus during its blend (see BuildComposition), so the final
        // assembled video ends up exactly as long as the untrimmed audio with no post-hoc padding.
        // Narrated mode gets that surplus free by stream_loop'ing the silent stock clip; native mode had it
        // subtracted out of the clip's own length up front (see ComposeVideoAsync), so both land here with
        // an outputDuration the source can actually satisfy.
        var outputDuration = (isLast ? scene.DurationSeconds : scene.DurationSeconds + transitionDuration)
            .ToString(CultureInfo.InvariantCulture);
        var composedPath = Path.Combine(workDir, $"scene-{scene.OrderIndex}-composed.mp4");

        // Looping a generated clip would repeat its baked-in audio mid-scene; stock clips are silent, so
        // looping them to cover a longer narration is exactly what we want.
        var loop = nativeAudio ? "" : "-stream_loop -1 ";

        // A generated clip is *supposed* to have native audio, but a silent one must not break the concat
        // downstream — anullsrc stands in so every composed scene has exactly one audio stream either way.
        var hasOwnAudio = nativeAudio
            && !string.IsNullOrEmpty(scene.VideoFilePathA)
            && await _ffmpegRunner.HasAudioStreamAsync(scene.VideoFilePathA, ct);

        if (nativeAudio && !hasOwnAudio)
            _logger.LogWarning("Sahne {Index} için üretilen klipte ses akışı yok, sessiz iz ekleniyor", scene.OrderIndex);

        string args;
        if (template.UseABSplit && !string.IsNullOrEmpty(scene.VideoFilePathB) && scene.VideoFilePathB != scene.VideoFilePathA)
        {
            // A/B split has two visuals but only ever one audio track — on the native path that's clip A's
            // (mixing two generated ambiences would just muddy it), on the narrated path it's the TTS input.
            var (audioInput, audioMap) = nativeAudio
                ? (hasOwnAudio ? "" : SilentAudioInput, hasOwnAudio ? "0:a" : "2:a")
                : ($"-i \"{scene.AudioFilePath}\" ", "2:a");

            args =
                $"{loop}-i \"{scene.VideoFilePathA}\" {loop}-i \"{scene.VideoFilePathB}\" {audioInput}" +
                $"-filter_complex \"" +
                $"[0:v]scale={Width}:{HalfHeight}:force_original_aspect_ratio=increase,crop={Width}:{HalfHeight}[top];" +
                $"[1:v]scale={Width}:{HalfHeight}:force_original_aspect_ratio=increase,crop={Width}:{HalfHeight}[bot];" +
                $"[top][bot]vstack=inputs=2[v]\" " +
                $"-map \"[v]\" -map {audioMap} -t {outputDuration} -r {Fps} {IntermediateEncode} \"{composedPath}\"";
        }
        else
        {
            var (audioInput, audioMap) = nativeAudio
                ? (hasOwnAudio ? "" : SilentAudioInput, hasOwnAudio ? "0:a" : "1:a")
                : ($"-i \"{scene.AudioFilePath}\" ", "1:a");

            args =
                $"{loop}-i \"{scene.VideoFilePathA}\" {audioInput}" +
                $"-filter_complex \"[0:v]scale={Width}:{Height}:force_original_aspect_ratio=increase,crop={Width}:{Height}[v]\" " +
                $"-map \"[v]\" -map {audioMap} -t {outputDuration} -r {Fps} {IntermediateEncode} \"{composedPath}\"";
        }

        await _ffmpegRunner.RunAsync(args, ct);
        scene.ComposedFilePath = composedPath;
    }

    // ── Scene timeline for subtitles: plain cumulative sum, matches the real (untrimmed) audio track ────

    private static List<(double Start, double End)> ComputeSceneTimeline(List<Scene> scenes)
    {
        var timeline = new List<(double Start, double End)>();
        var cursor = 0.0;
        foreach (var scene in scenes)
        {
            var start = cursor;
            var end = start + scene.DurationSeconds;
            timeline.Add((start, end));
            cursor = end;
        }
        return timeline;
    }

    // ── xfade video chain + gapless audio concat + subtitle burn + optional avatar/BGM ─────────

    private (string Args, double TotalDuration) BuildComposition(
        List<Scene> scenes, double transitionDuration, string? assPath, Template template, bool nativeAudio)
    {
        var inputs = new StringBuilder();
        foreach (var scene in scenes)
            inputs.Append($"-i \"{scene.ComposedFilePath}\" ");

        var filter = new StringBuilder();
        string vLabel, aLabel;
        double totalDuration;

        if (scenes.Count == 1)
        {
            // Route the lone input through identity filters so vLabel/aLabel are always filtergraph pad
            // names (not raw "0:v" stream specifiers) — keeps every downstream [label] reference and
            // -map "[label]" uniform regardless of how many scenes there are.
            filter.Append("[0:v]null[v0];[0:a]anull[a0];");
            vLabel = "v0";
            aLabel = "a0";
            totalDuration = scenes[0].DurationSeconds;
        }
        else
        {
            // Video: xfade chain. Each composed-scene input's actual video content is `transitionDuration`
            // longer than its real (audio) duration for every non-last scene (see ComposeSceneAsync) — the
            // xfade blend below consumes exactly that surplus, so the chain's final running duration
            // telescopes down to precisely sum(scene audio durations), matching the audio track with no
            // padding/trimming needed anywhere.
            double EffectiveDuration(int i) => i == scenes.Count - 1 ? scenes[i].DurationSeconds : scenes[i].DurationSeconds + transitionDuration;

            var runningDuration = EffectiveDuration(0);
            var prevV = "[0:v]";

            for (var i = 1; i < scenes.Count; i++)
            {
                var offset = Math.Max(runningDuration - transitionDuration, 0);
                var transitionName = MapTransitionName(scenes[i - 1].TransitionEffect);
                var vOut = $"[v{i}]";

                filter.Append(
                    $"{prevV}[{i}:v]xfade=transition={transitionName}:duration={transitionDuration.ToString(CultureInfo.InvariantCulture)}:offset={offset.ToString(CultureInfo.InvariantCulture)}{vOut};");

                runningDuration = runningDuration + EffectiveDuration(i) - transitionDuration;
                prevV = vOut;
            }

            vLabel = prevV.Trim('[', ']');
            totalDuration = runningDuration; // == sum(scenes[i].DurationSeconds), i.e. the real audio length

            if (nativeAudio)
            {
                // Native audio must be CROSSFADED, not concatenated. Each composed scene carries its clip's
                // full length of audio (EffectiveDuration), while the video chain telescopes by
                // transitionDuration at every xfade — concatenating therefore runs the audio progressively
                // late, and the drift accumulates per transition. Measured on real media: by the 2nd
                // transition the audio was a full ~1s behind (video already on clip 3 while clip 2's tone
                // still played); acrossfade tracked the video exactly at the same timestamps.
                // Crossfading is also simply correct for ambience/SFX — the objection that killed it on the
                // narrated path (below) was specific to speech.
                var prevA = "[0:a]";
                for (var i = 1; i < scenes.Count; i++)
                {
                    var aOut = $"[a{i}]";
                    filter.Append($"{prevA}[{i}:a]acrossfade=d={transitionDuration.ToString(CultureInfo.InvariantCulture)}{aOut};");
                    prevA = aOut;
                }
                aLabel = prevA.Trim('[', ']');
            }
            else
            {
                // Narrated audio is played back-to-back at FULL length, never trimmed or crossfaded — and
                // here concat is exactly right, because each scene's audio is already DurationSeconds long,
                // so the totals match without any blending. RemoveSilenceAsync already stripped leading/
                // trailing silence from each TTS clip, so the tail is real spoken words — trimming any of it
                // (an earlier version did, to force-match a shorter video timeline) cut sentences off
                // mid-word. acrossfade was tried before that and rejected too — it blends two different
                // sentences' waveforms into overlapping, garbled speech.
                var audioLabels = new StringBuilder();
                for (var i = 0; i < scenes.Count; i++)
                    audioLabels.Append($"[{i}:a]");
                filter.Append($"{audioLabels}concat=n={scenes.Count}:v=0:a=1[aconcat];");
                aLabel = "aconcat";
            }
        }

        // Burn-in is skipped entirely when there's no narration to caption (native-audio mode) — vLabel then
        // just carries straight through to the avatar/output stage below.
        if (assPath != null)
        {
            filter.Append($"[{vLabel}]subtitles='{EscapeFfmpegPath(assPath)}'[vsub];");
            vLabel = "vsub";
        }

        var extraInputIndex = scenes.Count;
        string finalVLabel = vLabel;

        var avatarPath = _configuration.GetValue("Media:Avatar:DefaultAvatarPath", "assets/avatars/default.mp4")!;
        if (template.UseAvatarInjection && File.Exists(avatarPath))
        {
            inputs.Append($"-stream_loop -1 -i \"{avatarPath}\" ");
            filter.Append(
                $"[{extraInputIndex}:v]scale=300:-1[avatar];[{vLabel}][avatar]overlay=W-w-24:H-h-24[vfinal];");
            finalVLabel = "vfinal";
            extraInputIndex++;
        }
        else if (template.UseAvatarInjection)
        {
            _logger.LogWarning("Template avatar overlay istiyor ama {Path} bulunamadı, atlanıyor", avatarPath);
        }

        string finalALabel = aLabel;
        if (!string.IsNullOrEmpty(template.BGMFilePath) && File.Exists(template.BGMFilePath))
        {
            inputs.Append($"-stream_loop -1 -i \"{template.BGMFilePath}\" ");
            var bgmVolume = template.BGMVolume.ToString(CultureInfo.InvariantCulture);
            filter.Append(
                $"[{extraInputIndex}:a]volume={bgmVolume}[bgm];[{aLabel}][bgm]amix=inputs=2:duration=first:dropout_transition=0[afinal];");
            finalALabel = "afinal";
        }
        else if (!string.IsNullOrEmpty(template.BGMFilePath))
        {
            _logger.LogWarning("Template BGM istiyor ama {Path} bulunamadı, atlanıyor", template.BGMFilePath);
        }

        // ffmpeg's filter_complex parser treats a trailing ';' as introducing an (empty, invalid) filter.
        var filterStr = filter.ToString().TrimEnd(';');
        var args =
            $"{inputs}-filter_complex \"{filterStr}\" -map \"[{finalVLabel}]\" -map \"[{finalALabel}]\" " +
            $"-r {Fps} {FinalEncode}";

        return (args, totalDuration);
    }

    // ── ASS subtitle file ─────────

    private static string BuildAssSubtitleFile(List<Scene> scenes, List<(double Start, double End)> timeline, Template template, string workDir)
    {
        var alignment = template.SubtitlePosition.ToLowerInvariant() switch
        {
            "top" => 8,
            "center" => 5,
            _ => 2, // bottom
        };
        var color = HexToAssColor(template.FontColor);
        var marginV = alignment == 2 ? 80 : 40;

        var sb = new StringBuilder();
        sb.AppendLine("[Script Info]");
        sb.AppendLine("ScriptType: v4.00+");
        sb.AppendLine($"PlayResX: {Width}");
        sb.AppendLine($"PlayResY: {Height}");
        sb.AppendLine();
        sb.AppendLine("[V4+ Styles]");
        sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        sb.AppendLine($"Style: Default,{template.FontFamily},{template.FontSize},{color},&H000000FF,&H00000000,&H64000000,0,0,0,0,100,100,0,0,1,3,0,{alignment},20,20,{marginV},1");
        sb.AppendLine();
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        for (var i = 0; i < scenes.Count; i++)
        {
            var text = scenes[i].NarrationText.Replace("{", "(").Replace("}", ")").Replace("\r\n", "\\N").Replace("\n", "\\N");
            sb.AppendLine($"Dialogue: 0,{FormatAssTime(timeline[i].Start)},{FormatAssTime(timeline[i].End)},Default,,0,0,0,,{text}");
        }

        var path = Path.Combine(workDir, "subtitles.ass");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        return path;
    }

    private static string FormatAssTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(seconds, 0));
        return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}";
    }

    /// <summary>"#RRGGBB" → ASS "&amp;H00BBGGRR" (ASS colors are BGR, alpha-first, opaque = 00).</summary>
    private static string HexToAssColor(string hex)
    {
        var clean = hex.TrimStart('#');
        if (clean.Length != 6) return "&H00FFFFFF"; // fall back to white
        var r = clean[..2];
        var g = clean[2..4];
        var b = clean[4..6];
        return $"&H00{b}{g}{r}";
    }

    /// <summary>ffmpeg filtergraph syntax needs ':' escaped and backslash paths normalized to forward slashes.</summary>
    private static string EscapeFfmpegPath(string path) => path.Replace('\\', '/').Replace(":", "\\:");

    private static string MapTransitionName(string transitionEffect) => transitionEffect.ToLowerInvariant() switch
    {
        "slide" => "slideleft",
        "wipe" => "wipeleft",
        "dissolve" => "dissolve",
        _ => "fade",
    };
}
