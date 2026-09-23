namespace VideoAutomation.Domain.Enums;

public enum VideoStatus
{
    Pending = 0,
    ScriptGenerated = 1,
    AssetsDownloading = 2,
    AssetsReady = 3,
    Composing = 4,
    Composed = 5,
    Publishing = 6,
    Published = 7,
    Failed = 8,
    PendingApproval = 9, // sent to Telegram, waiting on the user's approve/reject decision
    Rejected = 10        // user rejected via Telegram — never published
}
