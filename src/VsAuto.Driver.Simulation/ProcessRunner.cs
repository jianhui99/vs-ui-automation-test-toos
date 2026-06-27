using System.Diagnostics;
using System.Text;

namespace VsAuto.Driver.Simulation;

internal sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public string Combined => string.IsNullOrEmpty(StdErr) ? StdOut : $"{StdOut}\n{StdErr}";
}

internal static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName, string arguments, string? workingDir, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
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

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
