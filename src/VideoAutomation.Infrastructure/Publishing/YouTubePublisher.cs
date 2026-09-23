using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Domain.Entities;
using VideoAutomation.Domain.Enums;
using VideoAutomation.Shared.Exceptions;
using YouTubeVideo = Google.Apis.YouTube.v3.Data.Video;

namespace VideoAutomation.Infrastructure.Publishing;

/// <summary>
/// IPublisher backed by the real YouTube Data API v3 resumable upload (Videos.insert), not the read-only
/// ApiKeys:YouTubeApiKey used elsewhere for search/trend-scraping — uploading requires OAuth from the
/// channel owner, hence the separate YouTube:ClientId/ClientSecret (already scaffolded in Faz 1).
/// </summary>
public class YouTubePublisher : IPublisher
{
    public Platform Platform => Platform.YouTube;

    private readonly IConfiguration _configuration;
    private readonly IUserSecrets _secrets;
    private readonly ILogger<YouTubePublisher> _logger;

    public YouTubePublisher(IConfiguration configuration, IUserSecrets secrets, ILogger<YouTubePublisher> logger)
    {
        _configuration = configuration;
        _secrets = secrets;
        _logger = logger;
    }

    public async Task<Publication> PublishAsync(Video video, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(video.OutputFilePath) || !File.Exists(video.OutputFilePath))
            throw new PublishingException("YouTube", $"Video {video.Id} için çıktı dosyası yok, YouTube'a yüklenemedi.");

        var service = await GetAuthorizedServiceAsync(ct);

        var tags = ParseTags(video.Tags);
        var ytVideo = new YouTubeVideo
        {
            Snippet = new Google.Apis.YouTube.v3.Data.VideoSnippet
            {
                Title = Truncate(video.Title, 100),
                Description = Truncate(video.Description, 5000),
                Tags = tags.Count > 0 ? tags : null,
                CategoryId = _configuration.GetValue("Media:YouTube:CategoryId", "24")!, // 24 = Entertainment
            },
            Status = new Google.Apis.YouTube.v3.Data.VideoStatus
            {
                // Defaults to private on purpose: this is a new integration and the Telegram approval step
                // only confirms the CONTENT is good, not that the channel is ready for it to go public
                // unattended. Flip Media:YouTube:PrivacyStatus once confident in the pipeline end to end.
                PrivacyStatus = _configuration.GetValue("Media:YouTube:PrivacyStatus", "private"),
                SelfDeclaredMadeForKids = false,
            },
        };

        await using var fileStream = File.OpenRead(video.OutputFilePath);
        var request = service.Videos.Insert(ytVideo, "snippet,status", fileStream, "video/mp4");
        request.ProgressChanged += progress =>
            _logger.LogDebug("YouTube upload video={VideoId} status={Status} bytes={Bytes}", video.Id, progress.Status, progress.BytesSent);

        var uploadProgress = await request.UploadAsync(ct);
        if (uploadProgress.Status != UploadStatus.Completed)
            throw new PublishingException("YouTube", $"YouTube yüklemesi başarısız: {uploadProgress.Exception?.Message ?? uploadProgress.Status.ToString()}", uploadProgress.Exception);

        var uploaded = request.ResponseBody;
        _logger.LogInformation("Video {VideoId} YouTube'a yüklendi: {YouTubeId}", video.Id, uploaded.Id);

        return new Publication
        {
            VideoId = video.Id,
            Platform = Platform.YouTube,
            IsPublished = true,
            PublishedAt = DateTime.UtcNow,
            PlatformVideoId = uploaded.Id,
            PlatformUrl = $"https://www.youtube.com/watch?v={uploaded.Id}",
            Title = ytVideo.Snippet.Title,
            Description = ytVideo.Snippet.Description,
            Tags = video.Tags,
        };
    }

    public async Task<bool> ValidateCredentialsAsync(CancellationToken ct = default)
    {
        try
        {
            var service = await GetAuthorizedServiceAsync(ct);
            var request = service.Channels.List("id");
            request.Mine = true;
            var response = await request.ExecuteAsync(ct);
            return response.Items.Count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "YouTube kimlik doğrulaması geçersiz");
            return false;
        }
    }

    /// <summary>
    /// Builds a YouTubeService from the ALREADY-CACHED OAuth token — never triggers interactive consent.
    /// Publishing runs inside a Hangfire background worker; a browser-consent prompt there would block the
    /// worker forever (no one is watching a browser). So if there's no cached token we throw a clear,
    /// actionable error telling the caller to run the one-time interactive authorize first
    /// (POST /api/youtube/authorize) rather than hanging. Every call after that first consent is silent.
    /// </summary>
    private async Task<YouTubeService> GetAuthorizedServiceAsync(CancellationToken ct)
    {
        var credential = await TryLoadCredentialAsync(ct)
            ?? throw new PublishingException("YouTube",
                "YouTube yetkilendirmesi yapılmamış. Bir kez POST /api/youtube/authorize çalıştırıp tarayıcıda izin verin, sonra tekrar deneyin.");

        return new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = _configuration.GetValue("YouTube:ApplicationName", "VideoAutomation"),
        });
    }

    /// <summary>Loads the cached refresh token (if any) WITHOUT prompting. Returns null when the user has
    /// never authorized, so callers can fail fast instead of blocking on a browser flow.</summary>
    private async Task<UserCredential?> TryLoadCredentialAsync(CancellationToken ct)
    {
        var flow = BuildFlow();
        var token = await flow.LoadTokenAsync("user", ct);
        // A cached token with no refresh token can't be renewed silently — treat as "not authorized".
        if (token == null || string.IsNullOrWhiteSpace(token.RefreshToken)) return null;
        return new UserCredential(flow, "user", token);
    }

    // The current user's OAuth client + a per-user token store folder (assets/youtube-token/{userId}), so
    // each user's YouTube authorization is separate. Throws (no browser) if the user hasn't set client id/secret.
    private (string ClientId, string ClientSecret, string TokenStorePath) OAuthConfig()
    {
        var clientId = _secrets.YouTubeClientId;
        var clientSecret = _secrets.YouTubeClientSecret;
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new PublishingException("YouTube", "YouTube OAuth client bilgileri yapılandırılmamış — panelden (🔑 API Anahtarları) YouTube Client ID/Secret gir.");

        var basePath = _configuration.GetValue("YouTube:TokenStorePath", "assets/youtube-token")!;
        var userPath = Path.Combine(basePath, (_secrets.UserId ?? 0).ToString());
        return (clientId!, clientSecret!, userPath);
    }

    private GoogleAuthorizationCodeFlow BuildFlow()
    {
        var (clientId, clientSecret, tokenStorePath) = OAuthConfig();
        return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
            Scopes = [YouTubeService.Scope.YoutubeUpload, YouTubeService.Scope.YoutubeReadonly],
            DataStore = new FileDataStore(tokenStorePath, true),
        });
    }

    /// <summary>True when a usable cached token exists — no prompt, safe to call from anywhere.</summary>
    public async Task<bool> IsAuthorizedAsync(CancellationToken ct = default) =>
        await TryLoadCredentialAsync(ct) is not null;

    /// <summary>
    /// The ONE-TIME interactive consent — opens the user's default browser and listens on localhost for the
    /// OAuth redirect (desktop-app flow, works because the app runs on the user's own machine). Must be
    /// triggered by an explicit, user-initiated request (POST /api/youtube/authorize), NEVER from a
    /// background job. Caches the refresh token so all later publishes are silent. Returns the channel title
    /// as a sanity check that the credential actually works.
    /// </summary>
    public async Task<string> AuthorizeInteractivelyAsync(CancellationToken ct = default)
    {
        var (clientId, clientSecret, tokenStorePath) = OAuthConfig();

        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
            [YouTubeService.Scope.YoutubeUpload, YouTubeService.Scope.YoutubeReadonly],
            "user",
            ct,
            new FileDataStore(tokenStorePath, true));

        var service = new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = _configuration.GetValue("YouTube:ApplicationName", "VideoAutomation"),
        });
        var request = service.Channels.List("snippet");
        request.Mine = true;
        var response = await request.ExecuteAsync(ct);
        return response.Items is { Count: > 0 } ? response.Items[0].Snippet.Title : "(kanal adı okunamadı)";
    }

    private static List<string> ParseTags(string tagsJson)
    {
        if (string.IsNullOrWhiteSpace(tagsJson)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(tagsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Truncate(string text, int maxChars) =>
        string.IsNullOrEmpty(text) ? "" : text.Length <= maxChars ? text : text[..maxChars];
}
