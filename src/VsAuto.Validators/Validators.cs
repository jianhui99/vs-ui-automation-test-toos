using System.Globalization;
using System.Xml.Linq;
using VsAuto.Core.Abstractions;
using VsAuto.Core.Model;

namespace VsAuto.Validators;

internal static class P
{
    public static string? Str(IReadOnlyDictionary<string, object?> m, string key)
        => m.TryGetValue(key, out var v) && v is not null
            ? Convert.ToString(v, CultureInfo.InvariantCulture)
            : null;

    public static AssertionResult Pass(Assertion a, string detail)
        => new() { Type = a.Type, Passed = true, Advisory = a.Advisory, Detail = detail };

    public static AssertionResult Fail(Assertion a, string detail)
        => new() { Type = a.Type, Passed = false, Advisory = a.Advisory, Detail = detail };
}

/// <summary>Reads a property (e.g. TargetFramework) from a .csproj — ground truth, not UI.</summary>
public sealed class CsprojPropertyValidator : IAssertionValidator
{
    public string Type => "csproj_property";

    public Task<AssertionResult> ValidateAsync(Assertion a, StepContext ctx, CancellationToken ct)
    {
        var project = P.Str(a.Params, "project") ?? ctx.State.LastProject?.CsprojPath;
        var property = P.Str(a.Params, "property") ?? "TargetFramework";
        var expected = P.Str(a.Params, "equals");

        if (project is null || !File.Exists(project))
            return Task.FromResult(P.Fail(a, $"csproj not found: {project}"));

        var doc = XDocument.Load(project);
        var actual = doc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == property)?.Value?.Trim();

        if (actual is null)
            return Task.FromResult(P.Fail(a, $"{property} not present in {Path.GetFileName(project)}"));

        var ok = expected is null || string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(ok
            ? P.Pass(a, $"{property} = {actual}")
            : P.Fail(a, $"{property} expected '{expected}' but was '{actual}'"));
    }
}

/// <summary>Asserts the last build succeeded (uses the build outcome, not the Output window).</summary>
public sealed class BuildSucceededValidator : IAssertionValidator
{
    public string Type => "build_succeeded";

    public Task<AssertionResult> ValidateAsync(Assertion a, StepContext ctx, CancellationToken ct)
    {
        var build = ctx.State.LastBuild;
        if (build is null)
            return Task.FromResult(P.Fail(a, "no build has run"));
        return Task.FromResult(build.Succeeded
            ? P.Pass(a, "build succeeded")
            : P.Fail(a, $"build failed (exit {build.ExitCode})"));
    }
}

/// <summary>Checks that the last build/run output contains a substring.</summary>
public sealed class StdoutContainsValidator : IAssertionValidator
{
    public string Type => "stdout_contains";

    public Task<AssertionResult> ValidateAsync(Assertion a, StepContext ctx, CancellationToken ct)
    {
        var needle = P.Str(a.Params, "value") ?? "";
        var haystack = ctx.State.LastRun?.StdOut ?? ctx.State.LastBuild?.Output ?? "";
        return Task.FromResult(haystack.Contains(needle, StringComparison.OrdinalIgnoreCase)
            ? P.Pass(a, $"output contains '{needle}'")
            : P.Fail(a, $"output did not contain '{needle}'"));
    }
}

/// <summary>Asserts the last run's process exit code.</summary>
public sealed class ProcessExitCodeValidator : IAssertionValidator
{
    public string Type => "process_exit_code";

    public Task<AssertionResult> ValidateAsync(Assertion a, StepContext ctx, CancellationToken ct)
    {
        var run = ctx.State.LastRun;
        if (run is null)
            return Task.FromResult(P.Fail(a, "no application has run"));
        var expected = int.TryParse(P.Str(a.Params, "equals"), out var e) ? e : 0;
        return Task.FromResult(run.ExitCode == expected
            ? P.Pass(a, $"exit code {run.ExitCode}")
            : P.Fail(a, $"expected exit {expected} but was {run.ExitCode}"));
    }
}

public sealed class FileExistsValidator : IAssertionValidator
{
    public string Type => "file_exists";

    public Task<AssertionResult> ValidateAsync(Assertion a, StepContext ctx, CancellationToken ct)
    {
        var path = P.Str(a.Params, "path") ?? "";
        return Task.FromResult(File.Exists(path)
            ? P.Pass(a, $"exists: {path}")
            : P.Fail(a, $"missing: {path}"));
    }
}

public sealed class FileContainsValidator : IAssertionValidator
{
    public string Type => "file_contains";

    public async Task<AssertionResult> ValidateAsync(Assertion a, StepContext ctx, CancellationToken ct)
    {
        var path = P.Str(a.Params, "path") ?? "";
        var needle = P.Str(a.Params, "value") ?? "";
        if (!File.Exists(path))
            return P.Fail(a, $"missing: {path}");
        var text = await File.ReadAllTextAsync(path, ct);
        return text.Contains(needle, StringComparison.Ordinal)
            ? P.Pass(a, $"'{needle}' found in {Path.GetFileName(path)}")
            : P.Fail(a, $"'{needle}' not in {Path.GetFileName(path)}");
    }
}

/// <summary>
/// AI visual check. Delegates to the configured LLM provider. Advisory by default — an
/// inconclusive (null) verdict never fails the run; deterministic checks stay authoritative.
/// </summary>
public sealed class AiVisualValidator : IAssertionValidator
{
    private readonly ILlmProvider _llm;
    public AiVisualValidator(ILlmProvider llm) => _llm = llm;

    public string Type => "ai_visual";

    public async Task<AssertionResult> ValidateAsync(Assertion a, StepContext ctx, CancellationToken ct)
    {
        var prompt = P.Str(a.Params, "prompt") ?? "Validate the IDE state looks correct.";
        var images = ctx.State.Screenshots.TakeLast(1).ToList();
        var verdict = await _llm.AnalyzeAsync(new LlmRequest(prompt, images), ct);

        // null == inconclusive → treat as pass for an advisory assertion.
        var passed = verdict.Passed ?? true;
        return new AssertionResult
        {
            Type = a.Type,
            Passed = passed,
            Advisory = a.Advisory,
            Detail = $"[{_llm.Name}] {verdict.Summary}"
        };
    }
}
