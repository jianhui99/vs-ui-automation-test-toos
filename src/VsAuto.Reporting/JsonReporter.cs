using System.Text.Json;
using System.Text.Json.Serialization;
using VsAuto.Core.Abstractions;
using VsAuto.Core.Model;

namespace VsAuto.Reporting;

/// <summary>Machine-readable report for CI gating and trend aggregation.</summary>
public sealed class JsonReporter : IReporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<string> WriteAsync(CaseResult result, string outDir, CancellationToken ct)
    {
        Directory.CreateDirectory(outDir);
        var path = Path.Combine(outDir, $"{result.CaseId}.result.json");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, result, Options, ct);
        return path;
    }
}
