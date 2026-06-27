namespace VsAuto.Core.Model;

/// <summary>
/// A declarative test case authored by QA in YAML. Mirrors
/// tests/cases/_schema/test-case.schema.json.
/// </summary>
public sealed class TestCase
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Priority { get; init; } = "P2";
    public string Feature { get; init; } = "";
    public string? Classification { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public Targets? Targets { get; init; }

    /// <summary>Free-form variables referenced by steps via ${data.x}.</summary>
    public IReadOnlyDictionary<string, object?> Data { get; init; } =
        new Dictionary<string, object?>();

    public CaseConfig Config { get; init; } = new();
    public IReadOnlyList<Step> Steps { get; init; } = [];
}

public sealed class Targets
{
    public IReadOnlyList<string> Os { get; init; } = [];
    public IReadOnlyList<string> Arch { get; init; } = [];
    public IReadOnlyList<string> Vs { get; init; } = [];
}

public enum OnFailure
{
    Stop,
    Continue
}

public sealed class CaseConfig
{
    public OnFailure OnFailure { get; init; } = OnFailure.Stop;
    public int TimeoutSeconds { get; init; } = 600;
    public int Retries { get; init; } = 0;
}

public sealed class Step
{
    public required string Action { get; init; }
    public string? Name { get; init; }

    /// <summary>Action-specific parameters (already variable-resolved at run time).</summary>
    public IReadOnlyDictionary<string, object?> With { get; init; } =
        new Dictionary<string, object?>();

    public int? Retries { get; init; }
    public int? TimeoutSeconds { get; init; }
    public IReadOnlyList<Assertion> Assert { get; init; } = [];

    public string Display => string.IsNullOrWhiteSpace(Name) ? Action : Name!;
}

public sealed class Assertion
{
    public required string Type { get; init; }
    public bool Advisory { get; init; }

    /// <summary>All sibling keys of the assertion (e.g. project/property/equals).</summary>
    public IReadOnlyDictionary<string, object?> Params { get; init; } =
        new Dictionary<string, object?>();
}
