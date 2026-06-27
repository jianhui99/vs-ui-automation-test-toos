#if WINDOWS
using System.Diagnostics;
using System.Text;

namespace VsAuto.Driver.Windows;

/// <summary>Locates devenv.exe via the standard vswhere tool.</summary>
internal static class VsWhere
{
    public static string? FindDevenv(string? version)
    {
        var vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere))
            return null;

        var args = "-latest -products * -property productPath";
        if (!string.IsNullOrWhiteSpace(version))
            args = $"-version \"[{version},)\" -products * -property productPath";

        var psi = new ProcessStartInfo(vswhere, args)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        return string.IsNullOrWhiteSpace(output) ? null : output;
    }
}

/// <summary>MSBuild/dotnet ground-truth build and run for the Windows driver.</summary>
internal static class MsBuild
{
    public static async Task<(int Exit, string Output, string? Binlog)> BuildAsync(
        string projectPath, string configuration, CancellationToken ct)
    {
        var projDir = Path.GetDirectoryName(projectPath)!;
        var binlog = Path.Combine(projDir, "build.binlog");
        var r = await RunProcessAsync(
            "dotnet", $"build \"{projectPath}\" -c {configuration} -bl:\"{binlog}\"", projDir, ct);
        return (r.Exit, r.Output, File.Exists(binlog) ? binlog : null);
    }

    public static async Task<(int Exit, string StdOut, string StdErr)> RunAsync(
        string projectPath, string configuration, CancellationToken ct)
    {
        var projDir = Path.GetDirectoryName(projectPath)!;
        var r = await RunProcessAsync(
            "dotnet", $"run --project \"{projectPath}\" -c {configuration} --no-build", projDir, ct);
        return (r.Exit, r.StdOut, r.StdErr);
    }

    private static async Task<(int Exit, string Output, string StdOut, string StdErr)> RunProcessAsync(
        string fileName, string arguments, string workingDir, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct);
        var combined = stderr.Length == 0 ? stdout.ToString() : $"{stdout}\n{stderr}";
        return (process.ExitCode, combined, stdout.ToString(), stderr.ToString());
    }
}
#endif
