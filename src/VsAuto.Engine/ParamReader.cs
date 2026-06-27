using System.Globalization;

namespace VsAuto.Engine;

/// <summary>Small helpers for reading typed values out of resolved parameter maps.</summary>
internal static class ParamReader
{
    public static string GetString(this IReadOnlyDictionary<string, object?> map, string key, string fallback = "")
        => map.TryGetValue(key, out var v) && v is not null
            ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? fallback
            : fallback;

    public static string? GetStringOrNull(this IReadOnlyDictionary<string, object?> map, string key)
        => map.TryGetValue(key, out var v) && v is not null
            ? Convert.ToString(v, CultureInfo.InvariantCulture)
            : null;

    public static bool GetBool(this IReadOnlyDictionary<string, object?> map, string key, bool fallback = false)
    {
        if (!map.TryGetValue(key, out var v) || v is null)
            return fallback;
        if (v is bool b)
            return b;
        return bool.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out var parsed) ? parsed : fallback;
    }

    public static int GetInt(this IReadOnlyDictionary<string, object?> map, string key, int fallback)
    {
        if (!map.TryGetValue(key, out var v) || v is null)
            return fallback;
        return int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out var parsed) ? parsed : fallback;
    }
}
