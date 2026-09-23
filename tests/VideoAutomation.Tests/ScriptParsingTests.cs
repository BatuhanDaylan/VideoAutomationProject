using System.Text.Json;
using VideoAutomation.Infrastructure.AI;

namespace VideoAutomation.Tests;

/// <summary>
/// The script pipeline depends on two deterministic string/JSON steps: stripping the ```json fences Gemini
/// often wraps its output in, and deserializing the scene schema. Both are internal to Infrastructure and
/// exercised here via InternalsVisibleTo.
/// </summary>
public class ScriptParsingTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void CleanJsonResponse_StripsJsonFence()
    {
        var raw = "```json\n{\"scenes\":[]}\n```";
        Assert.Equal("{\"scenes\":[]}", GeminiClient.CleanJsonResponse(raw));
    }

    [Fact]
    public void CleanJsonResponse_StripsBareFence()
    {
        var raw = "```\n{\"scenes\":[]}\n```";
        Assert.Equal("{\"scenes\":[]}", GeminiClient.CleanJsonResponse(raw));
    }

    [Fact]
    public void CleanJsonResponse_LeavesPlainJsonUntouched()
    {
        Assert.Equal("{\"a\":1}", GeminiClient.CleanJsonResponse("{\"a\":1}"));
    }

    [Fact]
    public void CleanJsonResponse_TrimsSurroundingWhitespace()
    {
        Assert.Equal("{\"a\":1}", GeminiClient.CleanJsonResponse("   {\"a\":1}   "));
    }

    [Fact]
    public void Deserialize_ScriptResponse_MapsFieldsCaseInsensitively()
    {
        var json = """
        {"scenes":[{"sceneType":"giriş","narrationText":"Merhaba dünya","visualKeywordA":"cat","visualKeywordB":"box","videoPrompt":"a cat in a box","durationSeconds":6}]}
        """;

        var parsed = JsonSerializer.Deserialize<GeminiScriptResponse>(json, Options);

        Assert.NotNull(parsed);
        var scene = Assert.Single(parsed!.Scenes);
        Assert.Equal("giriş", scene.SceneType);
        Assert.Equal("Merhaba dünya", scene.NarrationText);
        Assert.Equal("cat", scene.VisualKeywordA);
        Assert.Equal("a cat in a box", scene.VideoPrompt);
        Assert.Equal(6, scene.DurationSeconds);
    }

    [Fact]
    public void Deserialize_PreservesSceneOrderAndCount()
    {
        var json = """
        {"scenes":[
          {"sceneType":"giriş","narrationText":"bir","durationSeconds":4},
          {"sceneType":"gelişme","narrationText":"iki","durationSeconds":5},
          {"sceneType":"sonuç","narrationText":"üç","durationSeconds":6}
        ]}
        """;

        var parsed = JsonSerializer.Deserialize<GeminiScriptResponse>(json, Options);

        Assert.NotNull(parsed);
        Assert.Equal(3, parsed!.Scenes.Count);
        Assert.Equal(new[] { "bir", "iki", "üç" }, parsed.Scenes.Select(s => s.NarrationText));
    }

    [Fact]
    public void CleanThenDeserialize_HandlesFencedGeminiOutput()
    {
        // The realistic path: Gemini returns fenced JSON, CleanJsonResponse strips it, then it parses.
        var raw = "```json\n{\"scenes\":[{\"sceneType\":\"punchline\",\"narrationText\":\"son\",\"durationSeconds\":5}]}\n```";

        var cleaned = GeminiClient.CleanJsonResponse(raw);
        var parsed = JsonSerializer.Deserialize<GeminiScriptResponse>(cleaned, Options);

        Assert.NotNull(parsed);
        Assert.Equal("son", Assert.Single(parsed!.Scenes).NarrationText);
    }
}
