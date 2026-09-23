using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VideoAutomation.Infrastructure.Persistence;
using VideoAutomation.Application.DTOs;

namespace VideoAutomation.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TemplatesController : ControllerBase
{
    private readonly AppDbContext _db;

    public TemplatesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<TemplateDto>>> GetAll()
    {
        var templates = await _db.Templates
            .OrderBy(t => t.Id)
            .Select(t => new TemplateDto
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                Type = t.Type.ToString(),
                TTSVoice = t.TTSVoice,
                TTSRate = t.TTSRate,
                TransitionStyle = t.TransitionStyle,
                UseABSplit = t.UseABSplit,
                UseAvatarInjection = t.UseAvatarInjection,
                IsDefault = t.IsDefault
            })
            .ToListAsync();

        return Ok(templates);
    }
}
