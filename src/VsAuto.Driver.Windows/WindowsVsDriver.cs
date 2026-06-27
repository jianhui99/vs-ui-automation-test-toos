using VsAuto.Core.Abstractions;

namespace VsAuto.Driver.Windows;

/// <summary>
/// Drives a real Visual Studio instance on Windows via FlaUI/UIA3. Project build/run use
/// MSBuild ground truth (binlog) rather than scraping the Output window, per the design.
///
/// NOTE: UIA automation ids for the New Project flow differ across VS versions; the marked
/// spots are the ones to tune on a target image. On non-Windows this type compiles to a
/// stub that throws PlatformNotSupportedException so the solution stays buildable everywhere.
/// </summary>
public sealed class WindowsVsDriver : IVsDriver
{
    public string Name => "windows-flaui";

#if WINDOWS
    private FlaUI.UIA3.UIA3Automation? _automation;
    private FlaUI.Core.Application? _app;

    public async Task LaunchAsync(LaunchOptions options, CancellationToken ct)
    {
        Directory.CreateDirectory(options.WorkDir);

        var devenv = LocateDevenv(options.Version)
            ?? throw new InvalidOperationException("Could not locate devenv.exe (use vswhere).");

        // A clean instance uses an isolated root suffix so first-run defaults (e.g. TC10) apply.
        var args = "/log";
        if (options.CleanInstance)
            args += " /RootSuffix VsAutoClean";

        _app = FlaUI.Core.Application.Launch(new System.Diagnostics.ProcessStartInfo(devenv, args));
        _automation = new FlaUI.UIA3.UIA3Automation();

        // Wait for the main IDE window to be ready.
        await WaitForMainWindowAsync(TimeSpan.FromMinutes(3), ct);
    }

    public async Task<NewProjectResult> NewProjectAsync(NewProjectOptions options, CancellationToken ct)
    {
        var window = GetMainWindow();

        // TODO(tune per VS version): drive File > New > Project, search the template,
        // set name/location/framework, and click Create. Element automation ids vary by
        // VS version, so they belong in a per-version locator map rather than inline here.
        // The deterministic assertion still reads the generated .csproj as ground truth.
        DriveNewProjectDialog(window, options);

        var projectDir = Path.Combine(options.Location, options.Name);
        var csproj = Path.Combine(projectDir, $"{options.Name}.csproj");
        await WaitForFileAsync(csproj, TimeSpan.FromMinutes(2), ct);
        return new NewProjectResult(projectDir, csproj);
    }

    public async Task<BuildOutcome> BuildAsync(BuildOptions options, CancellationToken ct)
    {
        // Ground truth: build via MSBuild and capture a binlog rather than reading the IDE.
        var (exit, output, binlog) = await MsBuild.BuildAsync(options.ProjectPath, options.Configuration, ct);
        return new BuildOutcome(exit == 0, exit, output, binlog);
    }

    public async Task<RunOutcome> RunAsync(RunOptions options, CancellationToken ct)
    {
        var (exit, stdout, stderr) = await MsBuild.RunAsync(options.ProjectPath, options.Configuration, ct);
        return new RunOutcome(exit, stdout, stderr);
    }

    public async Task<string> CaptureScreenshotAsync(string label, string evidenceDir, CancellationToken ct)
    {
        Directory.CreateDirectory(evidenceDir);
        var safe = string.Concat(label.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        var path = Path.Combine(evidenceDir, $"{safe}.png");
        var image = FlaUI.Core.Capturing.Capture.Screen();
        image.ToFile(path);
        await Task.CompletedTask;
        return path;
    }

    public ValueTask DisposeAsync()
    {
        _automation?.Dispose();
        try { _app?.Close(); } catch { /* best effort */ }
        return ValueTask.CompletedTask;
    }

    // --- helpers (kept small; locator detail lives in a per-version map in a full build) ---

    private static string? LocateDevenv(string? version) => VsWhere.FindDevenv(version);

    private FlaUI.Core.AutomationElements.Window GetMainWindow()
        => _app!.GetMainWindow(_automation!) ?? throw new InvalidOperationException("VS main window not found.");

    private async Task WaitForMainWindowAsync(TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var window = _app!.GetMainWindow(_automation!);
            if (window is not null && !string.IsNullOrEmpty(window.Title))
                return;
            await Task.Delay(1000, ct);
        }
        throw new TimeoutException("Visual Studio did not become ready in time.");
    }

    private static void DriveNewProjectDialog(
        FlaUI.Core.AutomationElements.Window window, NewProjectOptions options)
    {
        // Placeholder for the UIA interactions; see TODO above.
        _ = window;
        _ = options;
    }

    private static async Task WaitForFileAsync(string path, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
                return;
            await Task.Delay(500, ct);
        }
        throw new TimeoutException($"Expected file was not created: {path}");
    }
#else
    private const string Unsupported =
        "WindowsVsDriver requires Windows + FlaUI. Use the simulation driver on this platform.";

    public Task LaunchAsync(LaunchOptions options, CancellationToken ct)
        => throw new PlatformNotSupportedException(Unsupported);

    public Task<NewProjectResult> NewProjectAsync(NewProjectOptions options, CancellationToken ct)
        => throw new PlatformNotSupportedException(Unsupported);

    public Task<BuildOutcome> BuildAsync(BuildOptions options, CancellationToken ct)
        => throw new PlatformNotSupportedException(Unsupported);

    public Task<RunOutcome> RunAsync(RunOptions options, CancellationToken ct)
        => throw new PlatformNotSupportedException(Unsupported);

    public Task<string> CaptureScreenshotAsync(string label, string evidenceDir, CancellationToken ct)
        => throw new PlatformNotSupportedException(Unsupported);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
#endif
}
