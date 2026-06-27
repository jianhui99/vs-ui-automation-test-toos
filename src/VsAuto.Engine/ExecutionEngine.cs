using System.Diagnostics;
using System.Runtime.InteropServices;
using VsAuto.Core.Abstractions;
using VsAuto.Core.Model;
using VsAuto.Core.Yaml;

namespace VsAuto.Engine;

public sealed class EngineOptions
{
    public required IVsDriver Driver { get; init; }
    public required RunPaths Paths { get; init; }
    public required IReadOnlyDictionary<string, string> Environment { get; init; }
    public IReadOnlyList<IStepHandler> Handlers { get; init; } = [];
    public IReadOnlyList<IAssertionValidator> Validators { get; init; } = [];
    public IEvidenceCollector? Evidence { get; init; }
    public ILlmProvider? Llm { get; init; }
    public IExecutionLog? Log { get; init; }
    public string? VsVersion { get; init; }
}

/// <summary>
/// Drives a single test case through the step lifecycle: variable resolution → action →
/// assertions → retry/backoff → evidence + classification → continue/stop.
/// </summary>
public sealed class ExecutionEngine
{
    private readonly EngineOptions _opts;
    private readonly Dictionary<string, IStepHandler> _handlers;
    private readonly Dictionary<string, IAssertionValidator> _validators;
    private readonly IExecutionLog _log;

    public ExecutionEngine(EngineOptions options)
    {
        _opts = options;
        _handlers = options.Handlers.ToDictionary(h => h.Action, StringComparer.OrdinalIgnoreCase);
        _validators = options.Validators.ToDictionary(v => v.Type, StringComparer.OrdinalIgnoreCase);
        _log = options.Log ?? ConsoleLog.Instance;
    }

    public async Task<CaseResult> RunAsync(TestCase testCase, CancellationToken ct = default)
    {
        var result = new CaseResult
        {
            CaseId = testCase.Id,
            Title = testCase.Title,
            Priority = testCase.Priority,
            Environment = DescribeEnvironment(),
        };

        var resolver = new VariableResolver(testCase.Data, testCase.Targets, _opts.Environment);
        var state = new ExecutionState();
        var caseTimer = Stopwatch.StartNew();

        _log.Info($"▶ {testCase.Id}: {testCase.Title} [{testCase.Priority}]");
        Directory.CreateDirectory(_opts.Paths.WorkDir);
        Directory.CreateDirectory(_opts.Paths.EvidenceDir);

        var stop = false;
        foreach (var step in testCase.Steps)
        {
            if (stop)
            {
                result.Steps.Add(new StepResult
                {
                    Action = step.Action,
                    Name = step.Display,
                    Status = RunStatus.Skipped
                });
                continue;
            }

            var stepResult = await RunStepAsync(testCase, step, resolver, state, result, ct);
            result.Steps.Add(stepResult);

            if (stepResult.Status == RunStatus.Failed)
            {
                result.Status = RunStatus.Failed;
                if (testCase.Config.OnFailure == OnFailure.Stop)
                    stop = true;
            }
        }

        caseTimer.Stop();
        result.Duration = caseTimer.Elapsed;
        _log.Info($"■ {testCase.Id}: {result.Status} in {result.Duration.TotalSeconds:F1}s");
        return result;
    }

    private async Task<StepResult> RunStepAsync(
        TestCase testCase,
        Step step,
        VariableResolver resolver,
        ExecutionState state,
        CaseResult caseResult,
        CancellationToken ct)
    {
        var stepResult = new StepResult { Action = step.Action, Name = step.Display };
        var timer = Stopwatch.StartNew();
        var maxAttempts = 1 + (step.Retries ?? testCase.Config.Retries);

        var resolvedWith = resolver.ResolveParams(step.With);
        var ctx = new StepContext
        {
            Case = testCase,
            Step = step,
            With = resolvedWith,
            Driver = _opts.Driver,
            State = state,
            Paths = _opts.Paths,
            Log = _log
        };

        if (!_handlers.TryGetValue(step.Action, out var handler))
        {
            stepResult.Status = RunStatus.Failed;
            stepResult.Classification = FailureClass.AutomationError;
            stepResult.Error = $"No handler registered for action '{step.Action}'.";
            timer.Stop();
            stepResult.Duration = timer.Elapsed;
            return stepResult;
        }

        Exception? lastError = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            stepResult.Attempts = attempt;
            try
            {
                _log.Info($"  → {step.Display} (attempt {attempt}/{maxAttempts})");
                await handler.ExecuteAsync(ctx, ct);
                lastError = null;
                break;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                _log.Warn($"    action error: {ex.Message}");
                if (attempt < maxAttempts)
                    await Task.Delay(Backoff(attempt), ct);
            }
        }

        if (lastError is not null)
        {
            stepResult.Status = RunStatus.Failed;
            stepResult.Classification = FailureClass.Infrastructure;
            stepResult.Error = lastError.Message;
        }
        else
        {
            await EvaluateAssertionsAsync(step, ctx, stepResult, resolver, ct);
            if (stepResult.Attempts > 1 && stepResult.Status == RunStatus.Passed)
                stepResult.Classification = FailureClass.Flake; // passed only after retry
        }

        if (stepResult.Status == RunStatus.Failed)
        {
            if (_opts.Evidence is { } evidence)
                await evidence.CollectAsync(ctx, stepResult, ct);
            await ApplyAiAnalysisAsync(ctx, stepResult, caseResult, ct);
        }

        timer.Stop();
        stepResult.Duration = timer.Elapsed;
        return stepResult;
    }

    private async Task EvaluateAssertionsAsync(
        Step step, StepContext ctx, StepResult stepResult, VariableResolver resolver, CancellationToken ct)
    {
        foreach (var assertion in step.Assert)
        {
            if (!_validators.TryGetValue(assertion.Type, out var validator))
            {
                stepResult.Assertions.Add(new AssertionResult
                {
                    Type = assertion.Type,
                    Passed = false,
                    Advisory = assertion.Advisory,
                    Detail = $"No validator registered for assertion '{assertion.Type}'."
                });
                if (!assertion.Advisory)
                {
                    stepResult.Status = RunStatus.Failed;
                    stepResult.Classification = FailureClass.AutomationError;
                }
                continue;
            }

            var resolved = new Assertion
            {
                Type = assertion.Type,
                Advisory = assertion.Advisory,
                Params = resolver.ResolveParams(assertion.Params)
            };
            var ar = await validator.ValidateAsync(resolved, ctx, ct);
            stepResult.Assertions.Add(ar);

            if (!ar.Passed && !ar.Advisory)
            {
                stepResult.Status = RunStatus.Failed;
                // Failures against ground truth are real product-defect candidates.
                stepResult.Classification = FailureClass.ProductDefect;
                stepResult.Error ??= ar.Detail;
            }
        }
    }

    private async Task ApplyAiAnalysisAsync(
        StepContext ctx, StepResult stepResult, CaseResult caseResult, CancellationToken ct)
    {
        if (_opts.Llm is not { } llm)
            return;

        try
        {
            var prompt =
                $"Test '{ctx.Case.Id}: {ctx.Case.Title}' failed at step '{stepResult.Name}'. " +
                $"Classification: {stepResult.Classification}. Error: {stepResult.Error}. " +
                "Give a one-paragraph root-cause hypothesis and a concise bug summary " +
                "(title + expected vs actual).";
            var verdict = await llm.AnalyzeAsync(
                new LlmRequest(prompt, stepResult.Evidence.Where(IsImage).ToList()), ct);
            stepResult.AiAnalysis = verdict.Summary;
            caseResult.SuggestedBug ??= verdict.Detail ?? verdict.Summary;
        }
        catch (Exception ex)
        {
            _log.Warn($"AI analysis skipped: {ex.Message}");
        }
    }

    private static bool IsImage(string path)
        => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase);

    private static TimeSpan Backoff(int attempt)
        => TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));

    private EnvironmentInfo DescribeEnvironment() => new()
    {
        Os = RuntimeInformation.OSDescription,
        Arch = RuntimeInformation.OSArchitecture.ToString(),
        VsVersion = _opts.VsVersion,
        Driver = _opts.Driver.Name,
        Machine = System.Environment.MachineName
    };
}

/// <summary>Default console execution log.</summary>
public sealed class ConsoleLog : IExecutionLog
{
    public static readonly ConsoleLog Instance = new();
    public void Info(string message) => Console.WriteLine(message);
    public void Warn(string message) => Console.WriteLine($"WARN  {message}");
    public void Error(string message) => Console.Error.WriteLine($"ERROR {message}");
}
