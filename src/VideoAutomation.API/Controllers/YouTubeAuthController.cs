using Microsoft.AspNetCore.Mvc;
using VideoAutomation.Infrastructure.Publishing;

namespace VideoAutomation.API.Controllers;

/// <summary>
/// One-time YouTube OAuth consent. Uploading needs the channel owner's authorization, which is an
/// interactive browser flow — it CANNOT run inside the background publish job (it would block the worker
/// forever). So the user hits this endpoint once: it opens the local browser, caches the refresh token,
/// and every publish afterwards is silent. Works because the API runs on the user's own machine.
/// </summary>
[ApiController]
[Route("api/youtube")]
public class YouTubeAuthController : ControllerBase
{
    private readonly YouTubePublisher _youtube;

    public YouTubeAuthController(YouTubePublisher youtube) => _youtube = youtube;

    [HttpGet("auth-status")]
    public async Task<IActionResult> AuthStatus(CancellationToken ct)
        => Ok(new { authorized = await _youtube.IsAuthorizedAsync(ct) });

    [HttpPost("authorize")]
    public async Task<IActionResult> Authorize(CancellationToken ct)
    {
        // Bound the wait: if the user doesn't finish the browser consent within 3 minutes, return instead
        // of leaving the request (and the OAuth local listener) hanging.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            var channel = await _youtube.AuthorizeInteractivelyAsync(cts.Token);
            return Ok(new { authorized = true, channel, message = $"✅ YouTube yetkilendirildi: {channel}" });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(408, new { authorized = false, message = "Yetkilendirme zaman aşımına uğradı — tarayıcıdaki onayı 3 dakika içinde tamamlamadınız." });
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { authorized = false, message = ex.Message });
        }
    }
}
