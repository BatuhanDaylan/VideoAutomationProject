using VideoAutomation.Domain.Entities;

namespace VideoAutomation.Application.Interfaces.Agents;

/// <summary>
/// Runs the full Tarama → Seçim → Analiz → Üretim chain as one tracked unit, for the cron-scheduled daily
/// run and the manual "run now" trigger alike — both call this, neither re-implements the chain.
/// </summary>
public interface IPipelineOrchestrator
{
    Task<PipelineRun> RunAsync(string region = "TR", CancellationToken ct = default);
}
