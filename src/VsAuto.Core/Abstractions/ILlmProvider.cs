namespace VsAuto.Core.Abstractions;

/// <summary>
/// Pluggable LLM backend (Claude / Azure OpenAI / null). Used for advisory visual
/// validation, failure root-cause analysis, and bug-summary drafting — never for driving
/// clicks in the deterministic path.
/// </summary>
public interface ILlmProvider
{
    string Name { get; }
    Task<LlmVerdict> AnalyzeAsync(LlmRequest request, CancellationToken ct);
}

public sealed record LlmRequest(
    string Prompt,
    IReadOnlyList<string> ImagePaths,
    string? Context = null);

/// <summary>
/// Passed == null means inconclusive (the deterministic checks remain authoritative).
/// </summary>
public sealed record LlmVerdict(bool? Passed, string Summary, string? Detail = null);
