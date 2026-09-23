using VideoAutomation.Domain.Enums;

namespace VideoAutomation.Domain.Entities;

/// <summary>
/// Represents a video template with predefined style configurations.
/// </summary>
public class Template : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TemplateType Type { get; set; }

    // Visual style
    public string FontFamily { get; set; } = "Arial";
    public string FontColor { get; set; } = "#FFFFFF";
    public int FontSize { get; set; } = 48;
    public string SubtitlePosition { get; set; } = "bottom"; // top, center, bottom

    // Audio style
    public string TTSVoice { get; set; } = "tr-TR-AhmetNeural";
    public double TTSRate { get; set; } = 1.0;
    public string? BGMFilePath { get; set; }
    public double BGMVolume { get; set; } = 0.15;

    // Video style
    public string TransitionStyle { get; set; } = "fade"; // default transition
    public bool UseABSplit { get; set; } = true;
    public bool UseAvatarInjection { get; set; } = false;

    public bool IsDefault { get; set; }
}
