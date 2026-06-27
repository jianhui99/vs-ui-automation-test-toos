using VsAuto.Core.Abstractions;
using VsAuto.Core.Model;
using VsAuto.Engine;
using VsAuto.Engine.Handlers;
using VsAuto.Validators;
using Xunit;

namespace VsAuto.Tests;

public class EngineTests
{
    private static ExecutionEngine BuildEngine(IVsDriver driver, string workRoot)
    {
        var evidence = Path.Combine(workRoot, "evidence");
        return new ExecutionEngine(new EngineOptions
        {
            Driver = driver,
            Paths = new RunPaths(workRoot, workRoot, evidence),
            Environment = new Dictionary<string, string> { ["WorkDir_Root"] = workRoot },
            Log = new NullLog(),
            Handlers =
            [
                new LaunchVsHandler(), new NewProjectHandler(), new BuildHandler(),
                new RunHandler(), new ScreenshotHandler(), new WaitForHandler()
            ],
            Validators =
            [
                new CsprojPropertyValidator(), new BuildSucceededValidator(),
                new StdoutContainsValidator(), new ProcessExitCodeValidator()
            ],
            Evidence = new EvidenceCollector()
        });
    }

    private static TestCase ConsoleCase(string workRoot) => new()
    {
        Id = "TC11",
        Title = "Single TFM Console",
        Priority = "P0",
        Data = new Dictionary<string, object?>
        {
            ["projectName"] = "App",
            ["location"] = workRoot
        },
        Steps =
        [
            new Step { Action = "launch_vs" },
            new Step
            {
                Action = "new_project",
                With = new Dictionary<string, object?>
                {
                    ["template"] = "Console App",
                    ["name"] = "${data.projectName}",
                    ["location"] = "${data.location}"
                },
                Assert =
                [
                    new Assertion
                    {
                        Type = "csproj_property",
                        Params = new Dictionary<string, object?>
                        {
                            ["property"] = "TargetFramework",
                            ["equals"] = "net10.0"
                        }
                    }
                ]
            },
            new Step
            {
                Action = "build",
                Assert = [new Assertion { Type = "build_succeeded" }]
            },
            new Step
            {
                Action = "run",
                Assert =
                [
                    new Assertion
                    {
                        Type = "process_exit_code",
                        Params = new Dictionary<string, object?> { ["equals"] = "0" }
                    }
                ]
            }
        ]
    };

    [Fact]
    public async Task Passes_when_all_assertions_hold()
    {
        var work = Directory.CreateTempSubdirectory("vsauto-pass").FullName;
        var engine = BuildEngine(new FakeDriver(), work);

        var result = await engine.RunAsync(ConsoleCase(work));

        Assert.Equal(RunStatus.Passed, result.Status);
        Assert.All(result.Steps, s => Assert.Equal(RunStatus.Passed, s.Status));
    }

    [Fact]
    public async Task Fails_and_classifies_product_defect_on_tfm_mismatch()
    {
        var work = Directory.CreateTempSubdirectory("vsauto-defect").FullName;
        // Driver produces net99.0; the case expects net10.0 → ground-truth mismatch.
        var engine = BuildEngine(new FakeDriver { TargetFramework = "net99.0" }, work);

        var result = await engine.RunAsync(ConsoleCase(work));

        Assert.Equal(RunStatus.Failed, result.Status);
        var newProject = result.Steps.First(s => s.Action == "new_project");
        Assert.Equal(FailureClass.ProductDefect, newProject.Classification);
        // onFailure defaults to stop → later steps skipped.
        Assert.Contains(result.Steps, s => s.Status == RunStatus.Skipped);
    }

    [Fact]
    public async Task Build_failure_is_detected()
    {
        var work = Directory.CreateTempSubdirectory("vsauto-build").FullName;
        var engine = BuildEngine(new FakeDriver { BuildSucceeds = false }, work);

        var result = await engine.RunAsync(ConsoleCase(work));

        Assert.Equal(RunStatus.Failed, result.Status);
        var build = result.Steps.First(s => s.Action == "build");
        Assert.Equal(RunStatus.Failed, build.Status);
    }
}
