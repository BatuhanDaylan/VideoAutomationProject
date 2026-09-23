namespace VideoAutomation.Domain.Enums;

public static class VisualModeExtensions
{
    /// <summary>
    /// True when the visual clips carry their own audio (ambience, background conversation, SFX generated
    /// by the video model) and the pipeline must NOT lay a TTS narration track over them.
    /// <para>
    /// Only <see cref="VisualMode.AIGenerated"/> qualifies. Stock footage (Pexels) is silent, so it needs
    /// narration to carry the content. <see cref="VisualMode.Mixed"/> deliberately stays on the narrated
    /// path: it alternates silent stock clips with generated ones, and a video whose audio cut between a
    /// narrator and unrelated generated ambience every few seconds would sound broken — narrating
    /// throughout is the coherent choice there.
    /// </para>
    /// </summary>
    public static bool UsesNativeAudio(this VisualMode mode) => mode == VisualMode.AIGenerated;
}
