using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using VsAuto.Core.Model;

namespace VsAuto.Core.Yaml;

/// <summary>
/// Resolves ${...} tokens in test-case strings against the case's data/targets and an
/// external environment dictionary. Supports dotted paths and [index], e.g.
/// ${data.workDir}, ${targets.vs[0]}, ${WorkDir_Root}. Resolution is recursive so a data
/// value may itself reference another variable.
/// </summary>
public sealed partial class VariableResolver
{
    private const int MaxDepth = 12;

    private readonly IReadOnlyDictionary<string, object?> _data;
    private readonly Targets? _targets;
    private readonly IReadOnlyDictionary<string, string> _env;

    public VariableResolver(
        IReadOnlyDictionary<string, object?> data,
        Targets? targets,
        IReadOnlyDictionary<string, string> env)
    {
        _data = data;
        _targets = targets;
        _env = env;
    }

    [GeneratedRegex(@"\$\{([^}]+)\}")]
    private static partial Regex TokenRegex();

    /// <summary>Resolve a single string, expanding nested variables.</summary>
    public string ResolveString(string input)
    {
        var current = input;
        for (var depth = 0; depth < MaxDepth; depth++)
        {
            if (!current.Contains("${", StringComparison.Ordinal))
                return current;

            var replaced = TokenRegex().Replace(current, m =>
            {
                var path = m.Groups[1].Value.Trim();
                var value = Lookup(path);
                return value is null
                    ? m.Value // leave unresolved tokens intact rather than blanking them
                    : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            });

            if (replaced == current)
                return replaced;
            current = replaced;
        }

        return current;
    }

    /// <summary>Recursively resolve every string inside a value graph (dicts/lists/scalars).</summary>
    public object? ResolveValue(object? value) => value switch
    {
        string s => ResolveString(s),
        IReadOnlyDictionary<string, object?> dict =>
            dict.ToDictionary(kv => kv.Key, kv => ResolveValue(kv.Value)),
        IDictionary map => ResolveDictionary(map),
        IEnumerable list and not string => ResolveList(list),
        _ => value
    };

    public IReadOnlyDictionary<string, object?> ResolveParams(
        IReadOnlyDictionary<string, object?> source)
        => source.ToDictionary(kv => kv.Key, kv => ResolveValue(kv.Value));

    private Dictionary<string, object?> ResolveDictionary(IDictionary map)
    {
        var result = new Dictionary<string, object?>();
        foreach (DictionaryEntry entry in map)
            result[Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? ""] =
                ResolveValue(entry.Value);
        return result;
    }

    private List<object?> ResolveList(IEnumerable list)
    {
        var result = new List<object?>();
        foreach (var item in list)
            result.Add(ResolveValue(item));
        return result;
    }

    private object? Lookup(string path)
    {
        var segments = SplitPath(path);
        if (segments.Count == 0)
            return null;

        var (rootName, rootIndex) = segments[0];

        object? cursor = rootName switch
        {
            "data" => AsObject(_data),
            "targets" => TargetsAsMap(),
            "env" => AsObject(_env),
            _ => LookupBare(rootName)
        };

        if (rootName is "data" or "targets" or "env")
        {
            // The first segment selected a root namespace; nothing indexed yet.
            if (rootIndex is not null)
                cursor = Index(cursor, rootIndex.Value);
        }
        else if (rootIndex is not null)
        {
            cursor = Index(cursor, rootIndex.Value);
        }

        for (var i = 1; i < segments.Count; i++)
        {
            var (name, index) = segments[i];
            cursor = Navigate(cursor, name);
            if (index is not null)
                cursor = Index(cursor, index.Value);
        }

        return cursor;
    }

    private object? LookupBare(string name)
    {
        // Bare names resolve from env first, then fall back to data.
        if (_env.TryGetValue(name, out var envValue))
            return envValue;
        if (_data.TryGetValue(name, out var dataValue))
            return dataValue;
        return null;
    }

    private object? TargetsAsMap()
    {
        if (_targets is null)
            return null;
        return new Dictionary<string, object?>
        {
            ["os"] = _targets.Os,
            ["arch"] = _targets.Arch,
            ["vs"] = _targets.Vs
        };
    }

    private static object? AsObject<TValue>(IReadOnlyDictionary<string, TValue> dict)
        => dict.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);

    private static object? Navigate(object? cursor, string name)
    {
        return cursor switch
        {
            IReadOnlyDictionary<string, object?> d when d.TryGetValue(name, out var v) => v,
            IDictionary map when map.Contains(name) => map[name],
            _ => null
        };
    }

    private static object? Index(object? cursor, int index)
    {
        if (cursor is string)
            return cursor; // don't index into strings
        if (cursor is IEnumerable enumerable)
        {
            var i = 0;
            foreach (var item in enumerable)
            {
                if (i++ == index)
                    return item;
            }
        }
        return null;
    }

    private static List<(string Name, int? Index)> SplitPath(string path)
    {
        var result = new List<(string, int?)>();
        foreach (var raw in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = raw.Trim();
            int? index = null;
            var bracket = segment.IndexOf('[');
            if (bracket >= 0 && segment.EndsWith(']'))
            {
                var inner = segment[(bracket + 1)..^1];
                if (int.TryParse(inner, out var parsed))
                    index = parsed;
                segment = segment[..bracket];
            }
            result.Add((segment, index));
        }
        return result;
    }
}
