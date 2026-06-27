using VsAuto.Core.Abstractions;

namespace VsAuto.Engine.Handlers;

/// <summary>
/// Built-in action handlers. Each adapts a YAML action's 'with' block to a driver call
/// and records the result in the shared ExecutionState. To add a new action, implement
/// IStepHandler and register it — the engine needs no changes.
/// </summary>
public sealed class LaunchVsHandler : IStepHandler
{
    public string Action => "launch_vs";

    public async Task ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        var options = new LaunchOptions(
            Version: ctx.With.GetStringOrNull("version"),
            CleanInstance: ctx.With.GetBool("cleanInstance"),
            WorkDir: ctx.Paths.WorkDir);
        await ctx.Driver.LaunchAsync(options, ct);
    }
}

public sealed class NewProjectHandler : IStepHandler
{
    public string Action => "new_project";

    public async Task ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        var options = new NewProjectOptions(
            Template: ctx.With.GetString("template"),
            Name: ctx.With.GetString("name"),
            Location: ctx.With.GetString("location"),
            Framework: ctx.With.GetStringOrNull("framework"));

        var result = await ctx.Driver.NewProjectAsync(options, ct);
        ctx.State.LastProject = result;
        ctx.State.Bag["csproj"] = result.CsprojPath;
        ctx.Log.Info($"Created project at {result.CsprojPath}");
    }
}

public sealed class BuildHandler : IStepHandler
{
    public string Action => "build";

    public async Task ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        var projectPath = ctx.With.GetStringOrNull("project")
            ?? ctx.State.LastProject?.CsprojPath
            ?? throw new InvalidOperationException("build: no project path and no prior new_project.");

        var outcome = await ctx.Driver.BuildAsync(
            new BuildOptions(projectPath, ctx.With.GetString("configuration", "Debug")), ct);

        ctx.State.LastBuild = outcome;
        ctx.Log.Info($"Build {(outcome.Succeeded ? "succeeded" : "FAILED")} (exit {outcome.ExitCode}).");
    }
}

public sealed class RunHandler : IStepHandler
{
    public string Action => "run";

    public async Task ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        var projectPath = ctx.With.GetStringOrNull("project")
            ?? ctx.State.LastProject?.CsprojPath
            ?? throw new InvalidOperationException("run: no project path and no prior new_project.");

        var outcome = await ctx.Driver.RunAsync(
            new RunOptions(projectPath, ctx.With.GetString("configuration", "Debug")), ct);

        ctx.State.LastRun = outcome;
        ctx.Log.Info($"Run exited with code {outcome.ExitCode}.");
    }
}

public sealed class ScreenshotHandler : IStepHandler
{
    public string Action => "screenshot";

    public async Task ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        var label = ctx.With.GetString("label", "screenshot");
        var path = await ctx.Driver.CaptureScreenshotAsync(label, ctx.Paths.EvidenceDir, ct);
        ctx.State.Screenshots.Add(path);
    }
}

public sealed class WaitForHandler : IStepHandler
{
    public string Action => "wait_for";

    public async Task ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        var seconds = ctx.With.GetInt("seconds", 1);
        await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 120)), ct);
    }
}
