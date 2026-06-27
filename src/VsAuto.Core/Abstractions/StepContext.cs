using VsAuto.Core.Model;

namespace VsAuto.Core.Abstractions;

/// <summary>Mutable state carried across the steps of a single case execution.</summary>
public sealed class ExecutionState
{
    public NewProjectResult? LastProject { get; set; }
    public BuildOutcome? LastBuild { get; set; }
    public RunOutcome? LastRun { get; set; }
    public List<string> Screenshots { get; } = [];
    public Dictionary<string, object?> Bag { get; } = [];
}

/// <summary>Filesystem locations for a run's working dir and evidence.</summary>
public sealed record RunPaths(string RunDir, string WorkDir, string EvidenceDir);

/// <summary>Everything a step handler or validator needs to do its job.</summary>
public sealed class StepContext
{
    public required TestCase Case { get; init; }
    public required Step Step { get; init; }

    /// <summary>Variable-resolved parameters for the current step.</summary>
    public required IReadOnlyDictionary<string, object?> With { get; init; }

    public required IVsDriver Driver { get; init; }
    public required ExecutionState State { get; init; }
    public required RunPaths Paths { get; init; }
    public required IExecutionLog Log { get; init; }
}

/// <summary>A handler for one action verb (launch_vs, build, ...). Add a handler to add an action.</summary>
public interface IStepHandler
{
    string Action { get; }
    Task ExecuteAsync(StepContext ctx, CancellationToken ct);
}

/// <summary>A validator for one assertion type (csproj_property, build_succeeded, ...).</summary>
public interface IAssertionValidator
{
    string Type { get; }
    Task<AssertionResult> ValidateAsync(Assertion assertion, StepContext ctx, CancellationToken ct);
}

public interface IExecutionLog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}
