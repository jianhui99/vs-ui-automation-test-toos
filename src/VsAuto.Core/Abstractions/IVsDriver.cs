namespace VsAuto.Core.Abstractions;

/// <summary>
/// Performs the actual Visual Studio operations. The Windows implementation drives the
/// IDE via FlaUI/UIA3; the simulation implementation shells out to the dotnet CLI so the
/// pipeline is exercisable on any platform. Actions in YAML map onto these operations.
/// </summary>
public interface IVsDriver : IAsyncDisposable
{
    string Name { get; }

    Task LaunchAsync(LaunchOptions options, CancellationToken ct);

    Task<NewProjectResult> NewProjectAsync(NewProjectOptions options, CancellationToken ct);

    Task<BuildOutcome> BuildAsync(BuildOptions options, CancellationToken ct);

    Task<RunOutcome> RunAsync(RunOptions options, CancellationToken ct);

    /// <summary>Capture a screenshot (or a placeholder under simulation); returns the file path.</summary>
    Task<string> CaptureScreenshotAsync(string label, string evidenceDir, CancellationToken ct);
}

public sealed record LaunchOptions(string? Version, bool CleanInstance, string WorkDir);

public sealed record NewProjectOptions(
    string Template,
    string Name,
    string Location,
    string? Framework);

public sealed record NewProjectResult(string ProjectDir, string CsprojPath);

public sealed record BuildOptions(string ProjectPath, string Configuration);

public sealed record BuildOutcome(bool Succeeded, int ExitCode, string Output, string? LogPath);

public sealed record RunOptions(string ProjectPath, string Configuration);

public sealed record RunOutcome(int ExitCode, string StdOut, string StdErr);
