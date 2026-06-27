using VsAuto.Core.Model;
using VsAuto.Core.Yaml;
using Xunit;

namespace VsAuto.Tests;

public class TestCaseLoaderTests
{
    private const string Yaml = """
        id: TC11
        title: Single Target Framework Console Application
        priority: P0
        feature: Project Creation
        data:
          template: "Console App"
        config:
          onFailure: stop
          retries: 1
        steps:
          - action: new_project
            name: Create a .NET Console Application
            with:
              template: "${data.template}"
            assert:
              - type: csproj_property
                property: TargetFramework
                equals: "net10.0"
          - action: build
            assert:
              - type: build_succeeded
              - type: ai_visual
                advisory: true
                prompt: "looks ok?"
        """;

    [Fact]
    public void Parses_metadata_steps_and_assertions()
    {
        var tc = TestCaseLoader.LoadString(Yaml);

        Assert.Equal("TC11", tc.Id);
        Assert.Equal("P0", tc.Priority);
        Assert.Equal(OnFailure.Stop, tc.Config.OnFailure);
        Assert.Equal(1, tc.Config.Retries);
        Assert.Equal(2, tc.Steps.Count);

        var newProject = tc.Steps[0];
        Assert.Equal("new_project", newProject.Action);
        Assert.Equal("${data.template}", newProject.With["template"]);
        Assert.Single(newProject.Assert);
        Assert.Equal("csproj_property", newProject.Assert[0].Type);

        var build = tc.Steps[1];
        Assert.Equal(2, build.Assert.Count);
        Assert.False(build.Assert[0].Advisory);
        Assert.True(build.Assert[1].Advisory); // ai_visual marked advisory
    }

    [Fact]
    public void Loads_repository_TC11_file()
    {
        var path = TestPaths.RepoFile("tests/cases/TC11_SingleTfm.yaml");
        var tc = TestCaseLoader.LoadFile(path);
        Assert.Equal("TC11", tc.Id);
        Assert.Contains(tc.Steps, s => s.Action == "new_project");
        Assert.Contains(tc.Steps, s => s.Action == "build");
    }
}
