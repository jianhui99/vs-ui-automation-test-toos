using System.Diagnostics;
using System.Text;
using VsAuto.Core.Abstractions;

namespace VsAuto.Ai;

/// <summary>
/// LLM backend that shells out to the GitHub Copilot CLI (`copilot`). This is the right fit
/// for teams that already use Copilot and have NO standalone provider API keys: Copilot
/// handles auth and routes to whichever model is selected (claude-opus-4.8, gpt-5.5, ...).
///
/// Invocation: the prompt is piped over stdin (avoids ARG_MAX/quoting limits) and the CLI
/// runs non-interactively with `-s` (clean text output) and `--no-ask-user`. No tools are
/// granted, so the agent only reasons over the text we provide.
///
/// Note: the Copilot CLI does not document image input, so screenshots are referenced by
/// path in the prompt rather than attached — analysis is text-first (error, classification,
/// build output, UIA context), which matches the platform's ground-truth-first philosophy.
/// </summary>
public sealed class CopilotCliProvider : ILlmProvider
{
    private readonly string _executable;
    private readonly string? _model;
    private readonly TimeSpan _timeout;

    public CopilotCliProvider(string executable = "copilot", string? model = null, TimeSpan? timeout = null)
    {
        _executable = executable;
        _model = model;
        _timeout = timeout ?? TimeSpan.FromSeconds(120);
    }

    public string Name => _model is null ? "copilot-cli" : $"copilot-cli:{_model}";

    /// <summary>
    /// Returns a provider only if the Copilot CLI is on PATH. Model can be pinned via the
    /// VSAUTO_COPILOT_MODEL environment variable (recommended for consistent results).
    /// </summary>
    public static CopilotCliProvider? FromEnvironment(string? modelOverride = null)
    {
        var exe = LocateExecutable();
        if (exe is null)
            return null;
        var model = modelOverride
            ?? Environment.GetEnvironmentVariable("VSAUTO_COPILOT_MODEL");
        return new CopilotCliProvider(exe, string.IsNullOrWhiteSpace(model) ? null : model);
    }

    public async Task<LlmVerdict> AnalyzeAsync(LlmRequest request, CancellationToken ct)
    {
        var prompt = BuildPrompt(request);

        var psi = new ProcessStartInfo
        {
            FileName = _executable,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-s");            // silent: clean text, no session metadata
        psi.ArgumentList.Add("--no-ask-user"); // never block on clarifying questions
        if (_model is not null)
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(_model);
        }

        try
        {
            using var process = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.StandardInput.WriteAsync(prompt);
            process.StandardInput.Close();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_timeout);
            await process.WaitForExitAsync(cts.Token);

            var text = stdout.ToString().Trim();
            if (process.ExitCode != 0 || text.Length == 0)
            {
                return new LlmVerdict(
                    Passed: null,
                    Summary: $"Copilot CLI analysis unavailable (exit {process.ExitCode}).",
                    Detail: stderr.ToString().Trim() is { Length: > 0 } err ? err : null);
            }

            // Advisory by contract: verdict is inconclusive (null) so deterministic checks rule.
            return new LlmVerdict(Passed: null, Summary: Truncate(text, 400), Detail: text);
        }
        catch (Exception ex)
        {
            return new LlmVerdict(null, $"Copilot CLI invocation failed: {ex.Message}", null);
        }
    }

    private static string BuildPrompt(LlmRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine(request.Prompt);
        if (!string.IsNullOrWhiteSpace(request.Context))
        {
            sb.AppendLine();
            sb.AppendLine("Context:");
            sb.AppendLine(request.Context);
        }
        if (request.ImagePaths.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Screenshots captured (paths for the developer; not attached):");
            foreach (var p in request.ImagePaths)
                sb.AppendLine($"  - {p}");
        }
        sb.AppendLine();
        sb.AppendLine("Respond in plain text only. Do not modify files or run commands.");
        return sb.ToString();
    }

    private static string? LocateExecutable()
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "copilot.exe", "copilot.cmd", "copilot" }
            : ["copilot"];

        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in pathDirs)
        {
            foreach (var name in candidates)
            {
                var full = Path.Combine(dir, name);
                if (File.Exists(full))
                    return full;
            }
        }
        return null;
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
