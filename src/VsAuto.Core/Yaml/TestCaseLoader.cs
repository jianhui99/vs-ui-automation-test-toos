using System.Globalization;
using VsAuto.Core.Model;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace VsAuto.Core.Yaml;

/// <summary>
/// Loads a YAML test case into the typed model. Deserializes to a loose object graph then
/// maps explicitly, which keeps the flexible 'with' and 'assert' blocks (arbitrary keys)
/// robust without fighting attribute-based binding.
/// </summary>
public static class TestCaseLoader
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .Build();

    public static TestCase LoadFile(string path)
    {
        using var reader = new StreamReader(path);
        return Load(reader);
    }

    public static TestCase LoadString(string content)
    {
        using var reader = new StringReader(content);
        return Load(reader);
    }

    private static TestCase Load(TextReader reader)
    {
        var root = Yaml.Deserialize<object?>(reader) as IDictionary<object, object>
            ?? throw new InvalidDataException("Test case YAML must be a mapping at the root.");

        var map = Normalize(root);

        return new TestCase
        {
            Id = GetString(map, "id") ?? throw Missing("id"),
            Title = GetString(map, "title") ?? throw Missing("title"),
            Priority = GetString(map, "priority") ?? "P2",
            Feature = GetString(map, "feature") ?? "",
            Classification = GetString(map, "classification"),
            Tags = GetStringList(map, "tags"),
            Targets = ReadTargets(map),
            Data = ReadDict(map, "data"),
            Config = ReadConfig(map),
            Steps = ReadSteps(map)
        };
    }

    private static Targets? ReadTargets(IReadOnlyDictionary<string, object?> map)
    {
        if (map.GetValueOrDefault("targets") is not IReadOnlyDictionary<string, object?> t)
            return null;
        return new Targets
        {
            Os = ToStringList(t.GetValueOrDefault("os")),
            Arch = ToStringList(t.GetValueOrDefault("arch")),
            Vs = ToStringList(t.GetValueOrDefault("vs"))
        };
    }

    private static CaseConfig ReadConfig(IReadOnlyDictionary<string, object?> map)
    {
        if (map.GetValueOrDefault("config") is not IReadOnlyDictionary<string, object?> c)
            return new CaseConfig();

        var onFailure = GetString(c, "onFailure")?.ToLowerInvariant() == "continue"
            ? OnFailure.Continue
            : OnFailure.Stop;

        return new CaseConfig
        {
            OnFailure = onFailure,
            TimeoutSeconds = GetInt(c, "timeoutSeconds") ?? 600,
            Retries = GetInt(c, "retries") ?? 0
        };
    }

    private static IReadOnlyList<Step> ReadSteps(IReadOnlyDictionary<string, object?> map)
    {
        if (map.GetValueOrDefault("steps") is not IEnumerable<object?> rawSteps)
            return [];

        var steps = new List<Step>();
        foreach (var raw in rawSteps)
        {
            if (raw is not IReadOnlyDictionary<string, object?> s)
                continue;

            steps.Add(new Step
            {
                Action = GetString(s, "action") ?? throw Missing("step.action"),
                Name = GetString(s, "name"),
                With = ReadDict(s, "with"),
                Retries = GetInt(s, "retries"),
                TimeoutSeconds = GetInt(s, "timeoutSeconds"),
                Assert = ReadAssertions(s)
            });
        }
        return steps;
    }

    private static IReadOnlyList<Assertion> ReadAssertions(IReadOnlyDictionary<string, object?> step)
    {
        if (step.GetValueOrDefault("assert") is not IEnumerable<object?> rawAsserts)
            return [];

        var asserts = new List<Assertion>();
        foreach (var raw in rawAsserts)
        {
            if (raw is not IReadOnlyDictionary<string, object?> a)
                continue;

            var type = GetString(a, "type") ?? throw Missing("assert.type");
            var advisory = a.GetValueOrDefault("advisory") is bool b && b
                || string.Equals(GetString(a, "advisory"), "true", StringComparison.OrdinalIgnoreCase);

            var parms = a
                .Where(kv => kv.Key is not "type" and not "advisory")
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            asserts.Add(new Assertion { Type = type, Advisory = advisory, Params = parms });
        }
        return asserts;
    }

    private static IReadOnlyDictionary<string, object?> ReadDict(
        IReadOnlyDictionary<string, object?> map, string key)
        => map.GetValueOrDefault(key) as IReadOnlyDictionary<string, object?>
           ?? new Dictionary<string, object?>();

    // ---- normalization of the YamlDotNet object graph into string-keyed dictionaries ----

    private static IReadOnlyDictionary<string, object?> Normalize(IDictionary<object, object> source)
    {
        var result = new Dictionary<string, object?>();
        foreach (var kv in source)
            result[Convert.ToString(kv.Key, CultureInfo.InvariantCulture) ?? ""] = NormalizeValue(kv.Value);
        return result;
    }

    private static object? NormalizeValue(object? value) => value switch
    {
        IDictionary<object, object> dict => Normalize(dict),
        IEnumerable<object?> list => list.Select(NormalizeValue).ToList(),
        _ => value
    };

    private static IReadOnlyList<string> GetStringList(IReadOnlyDictionary<string, object?> map, string key)
        => ToStringList(map.GetValueOrDefault(key));

    private static IReadOnlyList<string> ToStringList(object? value)
        => value is IEnumerable<object?> list
            ? list.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "").ToList()
            : [];

    private static string? GetString(IReadOnlyDictionary<string, object?> map, string key)
        => map.GetValueOrDefault(key) is { } v ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;

    private static int? GetInt(IReadOnlyDictionary<string, object?> map, string key)
        => map.GetValueOrDefault(key) is { } v
           && int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out var i)
            ? i
            : null;

    private static InvalidDataException Missing(string field)
        => new($"Test case is missing required field: {field}");
}
