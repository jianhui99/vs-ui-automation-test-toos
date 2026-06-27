using VsAuto.Core.Model;
using VsAuto.Core.Yaml;
using Xunit;

namespace VsAuto.Tests;

public class VariableResolverTests
{
    [Fact]
    public void Resolves_nested_data_and_env()
    {
        var data = new Dictionary<string, object?>
        {
            ["workDir"] = "${WorkDir_Root}/TC11",
            ["projectName"] = "Tc11ConsoleApp"
        };
        var env = new Dictionary<string, string> { ["WorkDir_Root"] = "/runs/abc" };
        var r = new VariableResolver(data, targets: null, env);

        Assert.Equal("/runs/abc/TC11", r.ResolveString("${data.workDir}"));
        Assert.Equal("/runs/abc/TC11/Tc11ConsoleApp/Tc11ConsoleApp.csproj",
            r.ResolveString("${data.workDir}/${data.projectName}/${data.projectName}.csproj"));
    }

    [Fact]
    public void Resolves_targets_index()
    {
        var targets = new Targets { Vs = ["18.0", "17.0"] };
        var r = new VariableResolver(new Dictionary<string, object?>(), targets,
            new Dictionary<string, string>());

        Assert.Equal("18.0", r.ResolveString("${targets.vs[0]}"));
        Assert.Equal("17.0", r.ResolveString("${targets.vs[1]}"));
    }

    [Fact]
    public void Leaves_unknown_tokens_intact()
    {
        var r = new VariableResolver(new Dictionary<string, object?>(), null,
            new Dictionary<string, string>());
        Assert.Equal("${nope}", r.ResolveString("${nope}"));
    }
}
