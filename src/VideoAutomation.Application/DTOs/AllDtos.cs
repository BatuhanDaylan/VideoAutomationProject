using VideoAutomation.Domain.Enums;

namespace VideoAutomation.Application.DTOs;

public class TrendDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string SourcePlatform { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public long ViewCount { get; set; }
    public long LikeCount { get; set; }
    public long CommentCount { get; set; }
    public double FinalViralityScore { get; set; }
    public string ViralityLabel { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public bool IsSelected { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>When the source video was published (used by the "son N saat" trend filter).</summary>
    public DateTime? PublishedAt { get; set; }
    public string? LanguageCode { get; set; }
    /// <summary>Null until this trend has actually been through an AI-feasibility evaluation.</summary>
    public int? AiFeasibilityScore { get; set; }
    public string? AiFeasibilityReason { get; set; }
    /// <summary>What a multimodal model saw when it watched the source video; null until analyzed.</summary>
    public string? ContentSummary { get; set; }
}

public class ScanResultDto
{
    public int TotalTrends { get; set; }
    public Dictionary<string, int> ByPlatform { get; set; } = new();
    public List<TrendDto> TopTrends { get; set; } = new();
    public DateTime ScannedAt { get; set; }
}

public class ViralityScoreDto
{
    public double TrendMomentum { get; set; }
    public double ViewVelocity { get; set; }
    public double OutlierFactor { get; set; }
    public double EngagementRate { get; set; }
    public int CrossPlatformCount { get; set; }
    public double FinalScore { get; set; }
    public string Label { get; set; } = string.Empty;
}

public class VideoDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string VisualMode { get; set; } = string.Empty;
    public string TemplateType { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
    public string? OutputFilePath { get; set; }
    public string? ThumbnailPath { get; set; }
    public int? TrendId { get; set; }
    public ScriptDto? Script { get; set; }
    public List<SceneDto> Scenes { get; set; } = new();
    public List<PublicationDto> Publications { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class SceneDto
{
    public int Id { get; set; }
    public int OrderIndex { get; set; }
    public string NarrationText { get; set; } = string.Empty;
    public string SceneType { get; set; } = string.Empty;
    public string VisualKeywordA { get; set; } = string.Empty;
    public string VisualKeywordB { get; set; } = string.Empty;
    /// <summary>Detailed English prompt sent to the AI video model when generating this scene.</summary>
    public string VideoPrompt { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public bool IsAvatarScene { get; set; }
    public string TransitionEffect { get; set; } = string.Empty;
}

public class ScriptDto
{
    public int Id { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string FullText { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public List<SceneDto> Scenes { get; set; } = new();
}

public class PublicationDto
{
    public int Id { get; set; }
    public string Platform { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? PlatformUrl { get; set; }
    public string? ErrorMessage { get; set; }
}

public class TemplateDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string TTSVoice { get; set; } = string.Empty;
    public double TTSRate { get; set; }
    public string TransitionStyle { get; set; } = string.Empty;
    public bool UseABSplit { get; set; }
    public bool UseAvatarInjection { get; set; }
    public bool IsDefault { get; set; }
}

public class PublishResultDto
{
    public bool Success { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string? PlatformUrl { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PipelineStatusDto
{
    public int Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string CurrentStep { get; set; } = string.Empty;
    public int TotalSteps { get; set; }
    public int CompletedSteps { get; set; }
    public double ProgressPercent => TotalSteps > 0 ? Math.Round((double)CompletedSteps / TotalSteps * 100, 1) : 0;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string LogOutput { get; set; } = string.Empty;
    public int? VideoId { get; set; }
}
