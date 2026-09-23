using VideoAutomation.Domain.Entities;
using VideoAutomation.Domain.Enums;

namespace VideoAutomation.Application.Interfaces;

public interface IScraper
{
    Platform Platform { get; }
    Task<List<Trend>> GetTrendsAsync(string region = "TR", int maxResults = 20, CancellationToken ct = default);
}
