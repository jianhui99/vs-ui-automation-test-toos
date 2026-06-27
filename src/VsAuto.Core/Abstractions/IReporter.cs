using VsAuto.Core.Model;

namespace VsAuto.Core.Abstractions;

public interface IReporter
{
    /// <summary>Write a report for the run; returns the path of the produced artifact.</summary>
    Task<string> WriteAsync(CaseResult result, string outDir, CancellationToken ct);
}

/// <summary>Collects evidence (screenshots, logs, generated files) when a step fails.</summary>
public interface IEvidenceCollector
{
    Task CollectAsync(StepContext ctx, StepResult stepResult, CancellationToken ct);
}
