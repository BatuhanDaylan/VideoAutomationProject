using Microsoft.AspNetCore.Mvc;
using VideoAutomation.Application.DTOs;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Application.Interfaces.Agents;

namespace VideoAutomation.API.Controllers;

/// <summary>
/// Manual "chat with Gemini to shape a video idea, then produce it" page (Faz 7). Two steps:
/// POST /api/chat continues a stateless conversation (client holds the history), and
/// POST /api/chat/generate-video turns the agreed idea/transcript into a real Video via the same
/// AnalyzerAgent path a trend uses — so it flows into GeneratorAgent/publishing unchanged.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IContentGenerator _contentGenerator;
    private readonly IAnalyzerAgent _analyzerAgent;
    private readonly ILogger<ChatController> _logger;

    public ChatController(IContentGenerator contentGenerator, IAnalyzerAgent analyzerAgent, ILogger<ChatController> logger)
    {
        _contentGenerator = contentGenerator;
        _analyzerAgent = analyzerAgent;
        _logger = logger;
    }

    // Plain classes, not positional records: System.Text.Json binds these [FromBody] payloads far more
    // predictably (positional-record + camelCase constructor-param matching was misbinding the array items).
    public class ChatMessageDto
    {
        public string Role { get; set; } = "user";
        public string Content { get; set; } = string.Empty;
    }
    public class ChatRequest
    {
        public List<ChatMessageDto> Messages { get; set; } = new();
    }
    public record ChatResponse(string Reply);

    [HttpPost]
    public async Task<ActionResult<ChatResponse>> Chat([FromBody] ChatRequest request, CancellationToken ct = default)
    {
        if (request?.Messages == null || request.Messages.Count == 0)
            return BadRequest(new { error = "En az bir mesaj gönderilmeli." });

        var history = request.Messages
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => new ChatTurn(m.Role, m.Content))
            .ToList();

        if (history.Count == 0)
            return BadRequest(new { error = "Mesaj içeriği boş." });

        var reply = await _contentGenerator.GenerateChatReplyAsync(history, ct);
        return Ok(new ChatResponse(reply));
    }

    public class GenerateFromChatRequest
    {
        public string Idea { get; set; } = string.Empty;
        public int? TemplateId { get; set; }
        public string? VisualMode { get; set; }
    }

    [HttpPost("generate-video")]
    public async Task<ActionResult<VideoDto>> GenerateVideo([FromBody] GenerateFromChatRequest request, CancellationToken ct = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Idea))
            return BadRequest(new { error = "Video fikri (idea) boş olamaz." });

        var video = await _analyzerAgent.CreateVideoFromIdeaAsync(request.Idea, request.TemplateId, request.VisualMode, ct);
        _logger.LogInformation("Sohbetten video oluşturuldu: video {VideoId}", video.Id);

        return Ok(VideosController.MapToDto(video));
    }
}
