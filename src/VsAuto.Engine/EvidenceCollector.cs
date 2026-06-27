using System.Text;
using VsAuto.Core.Abstractions;
using VsAuto.Core.Model;

namespace VsAuto.Engine;

/// <summary>
/// On step failure, assembles a reproduction bundle: a screenshot, the build log, the
/// generated project files list, and a failure descriptor — everything a developer needs
/// to reproduce the issue.
/// </summary>
public sealed class EvidenceCollector : IEvidenceCollector
{
    public async Task CollectAsync(StepContext ctx, StepResult stepResult, CancellationToken ct)
    {
        Directory.CreateDirectory(ctx.Paths.EvidenceDir);

        // Always grab a failure screenshot.
        try
        {
            var shot = await ctx.Driver.CaptureScreenshotAsync(
                $"FAIL-{Sanitize(stepResult.Name)}", ctx.Paths.EvidenceDir, ct);
            stepResult.Evidence.Add(shot);
        }
        catch (Exception ex)
        {
            ctx.Log.Warn($"Screenshot capture failed: {ex.Message}");
        }

        // Persist the build log if we have one.
        if (ctx.State.LastBuild?.LogPath is { } logPath && File.Exists(logPath))
            stepResult.Evidence.Add(logPath);

        // Capture build output text inline for the bundle.
        if (ctx.State.LastBuild is { } build)
        {
            var buildTxt = Path.Combine(ctx.Paths.EvidenceDir, $"build-{Sanitize(stepResult.Name)}.log");
            await File.WriteAllTextAsync(buildTxt, build.Output, ct);
            stepResult.Evidence.Add(buildTxt);
        }

        // Write a compact failure descriptor.
        var descriptor = new StringBuilder()
            .AppendLine($"Case:     {ctx.Case.Id} — {ctx.Case.Title}")
            .AppendLine($"Step:     {stepResult.Name} ({stepResult.Action})")
            .AppendLine($"Class:    {stepResult.Classification}")
            .AppendLine($"Attempts: {stepResult.Attempts}")
            .AppendLine($"Error:    {stepResult.Error}")
            .AppendLine($"Project:  {ctx.State.LastProject?.ProjectDir}")
            .ToString();
        var descPath = Path.Combine(ctx.Paths.EvidenceDir, $"failure-{Sanitize(stepResult.Name)}.txt");
        await File.WriteAllTextAsync(descPath, descriptor, ct);
        stepResult.Evidence.Add(descPath);
    }

    private static string Sanitize(string name)
        => string.Concat(name.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
}
