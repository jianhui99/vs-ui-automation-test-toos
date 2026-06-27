namespace VsAuto.Core.Model;

public enum RunStatus
{
    Passed,
    Failed,
    Skipped
}

/// <summary>How a failure was classified — drives reporting and bug drafting.</summary>
public enum FailureClass
{
    None,
    Infrastructure,
    ProductDefect,
    Flake,
    AutomationError
}

public sealed class AssertionResult
{
    public required string Type { get; init; }
    public required bool Passed { get; init; }
    public bool Advisory { get; init; }
    public string? Detail { get; init; }
}

public sealed class StepResult
{
    public required string Action { get; init; }
    public required string Name { get; init; }
    public RunStatus Status { get; set; } = RunStatus.Passed;
    public TimeSpan Duration { get; set; }
    public int Attempts { get; set; } = 1;
    public FailureClass Classification { get; set; } = FailureClass.None;
    public string? Error { get; set; }
    public List<AssertionResult> Assertions { get; } = [];
    public List<string> Evidence { get; } = [];

    /// <summary>AI-produced root-cause / verdict text, if any.</summary>
    public string? AiAnalysis { get; set; }
}

public sealed class CaseResult
{
    public required string CaseId { get; init; }
    public required string Title { get; init; }
    public required string Priority { get; init; }
    public RunStatus Status { get; set; } = RunStatus.Passed;
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;
    public TimeSpan Duration { get; set; }
    public required EnvironmentInfo Environment { get; init; }
    public List<StepResult> Steps { get; } = [];

    /// <summary>AI-drafted bug summary when the case fails on a product defect.</summary>
    public string? SuggestedBug { get; set; }

    public string RunId { get; init; } = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
}

public sealed class EnvironmentInfo
{
    public required string Os { get; init; }
    public required string Arch { get; init; }
    public string? VsVersion { get; init; }
    public required string Driver { get; init; }
    public required string Machine { get; init; }
}
