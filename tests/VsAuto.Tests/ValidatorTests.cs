using VsAuto.Core.Abstractions;
using VsAuto.Core.Model;
using VsAuto.Validators;
using Xunit;

namespace VsAuto.Tests;

public class ValidatorTests
{
    private static StepContext MakeContext(ExecutionState state)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "vsauto-tests");
        return new StepContext
        {
            Case = new TestCase { Id = "T", Title = "t" },
            Step = new Step { Action = "x" },
            With = new Dictionary<string, object?>(),
            Driver = new FakeDriver(),
            State = state,
            Paths = new RunPaths(tmp, tmp, tmp),
            Log = new NullLog()
        };
    }

    [Fact]
    public async Task Csproj_property_matches_target_framework()
    {
        var dir = Directory.CreateTempSubdirectory("vsauto").FullName;
        var csproj = Path.Combine(dir, "App.csproj");
        await File.WriteAllTextAsync(csproj,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>" +
            "<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var validator = new CsprojPropertyValidator();
        var assertion = new Assertion
        {
            Type = "csproj_property",
            Params = new Dictionary<string, object?>
            {
                ["project"] = csproj,
                ["property"] = "TargetFramework",
                ["equals"] = "net10.0"
            }
        };

        var result = await validator.ValidateAsync(assertion, MakeContext(new ExecutionState()), default);
        Assert.True(result.Passed);

        var mismatch = new Assertion
        {
            Type = "csproj_property",
            Params = new Dictionary<string, object?>
            {
                ["project"] = csproj,
                ["property"] = "TargetFramework",
                ["equals"] = "net99.0"
            }
        };
        var failed = await validator.ValidateAsync(mismatch, MakeContext(new ExecutionState()), default);
        Assert.False(failed.Passed);
    }

    [Fact]
    public async Task Build_succeeded_reads_state()
    {
        var state = new ExecutionState
        {
            LastBuild = new BuildOutcome(true, 0, "Build succeeded", null)
        };
        var result = await new BuildSucceededValidator()
            .ValidateAsync(new Assertion { Type = "build_succeeded" }, MakeContext(state), default);
        Assert.True(result.Passed);
    }

    [Fact]
    public async Task Process_exit_code_compares()
    {
        var state = new ExecutionState { LastRun = new RunOutcome(0, "Hello, World!", "") };
        var assertion = new Assertion
        {
            Type = "process_exit_code",
            Params = new Dictionary<string, object?> { ["equals"] = "0" }
        };
        var result = await new ProcessExitCodeValidator()
            .ValidateAsync(assertion, MakeContext(state), default);
        Assert.True(result.Passed);
    }
}
