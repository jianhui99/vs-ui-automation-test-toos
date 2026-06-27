using VsAuto.Core.Abstractions;

namespace VsAuto.Tests;

internal sealed class NullLog : IExecutionLog
{
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message) { }
}

/// <summary>In-memory driver: writes a real csproj so ground-truth validators have something to read.</summary>
internal sealed class FakeDriver : IVsDriver
{
    public string Name => "fake";
    public bool BuildSucceeds { get; init; } = true;
    public int RunExitCode { get; init; } = 0;
    public string TargetFramework { get; init; } = "net10.0";

    public Task LaunchAsync(LaunchOptions options, CancellationToken ct) => Task.CompletedTask;

    public async Task<NewProjectResult> NewProjectAsync(NewProjectOptions options, CancellationToken ct)
    {
        var dir = Path.Combine(options.Location, options.Name);
        Directory.CreateDirectory(dir);
        var csproj = Path.Combine(dir, $"{options.Name}.csproj");
        await File.WriteAllTextAsync(csproj,
            $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>" +
            $"<TargetFramework>{TargetFramework}</TargetFramework></PropertyGroup></Project>", ct);
        return new NewProjectResult(dir, csproj);
    }

    public Task<BuildOutcome> BuildAsync(BuildOptions options, CancellationToken ct)
        => Task.FromResult(new BuildOutcome(BuildSucceeds, BuildSucceeds ? 0 : 1,
            BuildSucceeds ? "Build succeeded" : "Build FAILED", null));

    public Task<RunOutcome> RunAsync(RunOptions options, CancellationToken ct)
        => Task.FromResult(new RunOutcome(RunExitCode, "Hello, World!", ""));

    public Task<string> CaptureScreenshotAsync(string label, string evidenceDir, CancellationToken ct)
    {
        Directory.CreateDirectory(evidenceDir);
        var path = Path.Combine(evidenceDir, $"{label}.png");
        File.WriteAllBytes(path, [0]);
        return Task.FromResult(path);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
