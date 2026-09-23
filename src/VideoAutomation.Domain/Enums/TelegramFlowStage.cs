namespace VideoAutomation.Domain.Enums;

/// <summary>
/// Stage of the interactive Telegram video-creation conversation. The bot proactively sends viral
/// candidates at peak hours, then walks the user through: pick → review content → choose tool → generate
/// → review video → publish, with "change/regenerate" loops at the review stages.
/// </summary>
public enum TelegramFlowStage
{
    AwaitingSelection = 0,   // candidates sent, waiting for the user to pick one
    Analyzing = 1,           // selected trend is being analyzed (multimodal → konu + prompt)
    ReviewingContent = 2,    // konu + prompt shown, waiting for "üret" / "başka video"
    AwaitingToolChoice = 3,  // waiting for Pexels / AI
    Generating = 4,          // video is being produced
    ReviewingVideo = 5,      // generated video sent, waiting for "yükle" / "yeniden oluştur"
    Publishing = 6,          // uploading to YouTube
    Completed = 7,
    Cancelled = 8,
}
