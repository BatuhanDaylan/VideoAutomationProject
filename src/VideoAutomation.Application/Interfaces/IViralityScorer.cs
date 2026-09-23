using VideoAutomation.Domain.Entities;
using VideoAutomation.Domain.ValueObjects;

namespace VideoAutomation.Application.Interfaces;

public interface IViralityScorer
{
    Task<ViralityScore> CalculateScoreAsync(Trend trend, CancellationToken ct = default);
    Task<List<Trend>> RankTrendsAsync(List<Trend> trends, CancellationToken ct = default);
}
