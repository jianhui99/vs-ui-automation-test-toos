using VsAuto.Core.Abstractions;

namespace VsAuto.Driver.Simulation;

/// <summary>
/// Cross-platform driver that simulates Visual Studio operations using the dotnet CLI.
/// It lets the whole engine — steps, validators, evidence, reporting — be exercised on any
/// OS and in CI without a real VS install. On Windows the FlaUI driver replaces it to drive
/// the actual IDE; the engine code is identical either way.
/// </summary>
public sealed class SimulationDriver : IVsDriver
{
    // 1x1 transparent PNG so screenshot evidence is a real image file.
    private static readonly byte[] PlaceholderPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    public string Name => "simulation";

    public Task LaunchAsync(LaunchOptions options, CancellationToken ct)
    {
        Directory.CreateDirectory(options.WorkDir);
        // No IDE to launch; cleanInstance maps to a clean working directory.
        return Task.CompletedTask;
    }

    public async Task<NewProjectResult> NewProjectAsync(NewProjectOptions options, CancellationToken ct)
    {
        var template = MapTemplate(options.Template);
        var projectDir = Path.Combine(options.Location, options.Name);
        if (Directory.Exists(projectDir))
            Directory.Delete(projectDir, recursive: true); // idempotent for retries
        Directory.CreateDirectory(projectDir);

        var args = $"new {template} -n \"{options.Name}\" -o \"{projectDir}\"";
        if (!string.IsNullOrWhiteSpace(options.Framework))
            args += $" --framework {options.Framework}";

        var r = await ProcessRunner.RunAsync("dotnet", args, options.Location, ct);
        if (r.ExitCode != 0)
            throw new InvalidOperationException($"dotnet new failed: {r.Combined}");

        var csproj = Path.Combine(projectDir, $"{options.Name}.csproj");
        if (!File.Exists(csproj))
            csproj = Directory.EnumerateFiles(projectDir, "*.csproj").FirstOrDefault() ?? csproj;

        return new NewProjectResult(projectDir, csproj);
    }

    public async Task<BuildOutcome> BuildAsync(BuildOptions options, CancellationToken ct)
    {
        var projDir = Path.GetDirectoryName(options.ProjectPath)!;
        var binlog = Path.Combine(projDir, "build.binlog");
        var args = $"build \"{options.ProjectPath}\" -c {options.Configuration} -bl:\"{binlog}\"";
        var r = await ProcessRunner.RunAsync("dotnet", args, projDir, ct);
        return new BuildOutcome(
            Succeeded: r.ExitCode == 0,
            ExitCode: r.ExitCode,
            Output: r.Combined,
            LogPath: File.Exists(binlog) ? binlog : null);
    }

    public async Task<RunOutcome> RunAsync(RunOptions options, CancellationToken ct)
    {
        var projDir = Path.GetDirectoryName(options.ProjectPath)!;
        var args = $"run --project \"{options.ProjectPath}\" -c {options.Configuration} --no-build";
        var r = await ProcessRunner.RunAsync("dotnet", args, projDir, ct);
        return new RunOutcome(r.ExitCode, r.StdOut, r.StdErr);
    }

    public async Task<string> CaptureScreenshotAsync(string label, string evidenceDir, CancellationToken ct)
    {
        Directory.CreateDirectory(evidenceDir);
        var safe = string.Concat(label.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        var path = Path.Combine(evidenceDir, $"{safe}.png");
        await File.WriteAllBytesAsync(path, PlaceholderPng, ct);
        return path;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static string MapTemplate(string template) => template.Trim().ToLowerInvariant() switch
    {
        "console app" or "console" => "console",
        "class library" or "classlib" => "classlib",
        "xunit" or "xunit test project" => "xunit",
        "nunit" or "nunit test project" => "nunit",
        "mstest" or "mstest test project" => "mstest",
        _ => "console"
    };
}
