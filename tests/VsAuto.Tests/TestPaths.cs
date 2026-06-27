namespace VsAuto.Tests;

internal static class TestPaths
{
    /// <summary>Resolve a path relative to the repository root by walking up from the test bin dir.</summary>
    public static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return candidate;
            if (File.Exists(Path.Combine(dir.FullName, "VsAuto.sln")))
                return Path.Combine(dir.FullName, relative);
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not locate {relative} from {AppContext.BaseDirectory}");
    }
}
