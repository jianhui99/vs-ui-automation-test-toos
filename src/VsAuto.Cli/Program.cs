using VsAuto.Ai;
using VsAuto.Core.Abstractions;
using VsAuto.Core.Model;
using VsAuto.Core.Yaml;
using VsAuto.Driver.Simulation;
using VsAuto.Driver.Windows;
using VsAuto.Engine;
using VsAuto.Engine.Handlers;
using VsAuto.Reporting;
using VsAuto.Validators;

var parsed = CliArgs.Parse(args);
if (parsed is null)
{
    Console.WriteLine("""
        vsauto — Visual Studio UI automation runner

        Usage:
          vsauto run <case.yaml> [options]

        Options:
          --driver <simulation|windows>   Driver to use (default: simulation off-Windows, windows on Windows)
          --work <dir>                    Working dir root; bound to ${WorkDir_Root} (default: ./artifacts/work)
          --out <dir>                     Report output dir (default: ./reports/out)
          --data key=value                Override/append a case data variable (repeatable)
          --vs <version>                  Visual Studio version hint (windows driver)
        """);
    return 2;
}

var testCase = TestCaseLoader.LoadFile(parsed.CaseFile);

var workRoot = Path.GetFullPath(parsed.WorkDir);
var outDir = Path.GetFullPath(parsed.OutDir);
var runId = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
var runDir = Path.Combine(workRoot, testCase.Id, runId);
var evidenceDir = Path.Combine(runDir, "evidence");
Directory.CreateDirectory(evidenceDir);

// ${WorkDir_Root} and any --data overrides feed the variable resolver.
var env = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["WorkDir_Root"] = runDir
};
foreach (var (k, v) in parsed.Data)
    env[k] = v;

// Apply --data overrides onto the case's data block too (so ${data.x} sees them).
if (parsed.Data.Count > 0)
{
    var data = new Dictionary<string, object?>(testCase.Data);
    foreach (var (k, v) in parsed.Data)
        data[k] = v;
    testCase = Clone(testCase, data);
}

IVsDriver driver = SelectDriver(parsed.Driver);
ILlmProvider llm = (ILlmProvider?)ClaudeLlmProvider.FromEnvironment() ?? new NullLlmProvider();

Console.WriteLine($"Driver: {driver.Name} | AI: {llm.Name} | WorkDir_Root: {runDir}");

var engine = new ExecutionEngine(new EngineOptions
{
    Driver = driver,
    Paths = new RunPaths(runDir, runDir, evidenceDir),
    Environment = env,
    VsVersion = parsed.Vs,
    Handlers =
    [
        new LaunchVsHandler(),
        new NewProjectHandler(),
        new BuildHandler(),
        new RunHandler(),
        new ScreenshotHandler(),
        new WaitForHandler()
    ],
    Validators =
    [
        new CsprojPropertyValidator(),
        new BuildSucceededValidator(),
        new StdoutContainsValidator(),
        new ProcessExitCodeValidator(),
        new FileExistsValidator(),
        new FileContainsValidator(),
        new AiVisualValidator(llm)
    ],
    Evidence = new EvidenceCollector(),
    Llm = llm
});

CaseResult result;
try
{
    result = await engine.RunAsync(testCase);
}
finally
{
    await driver.DisposeAsync();
}

var jsonPath = await new JsonReporter().WriteAsync(result, outDir, CancellationToken.None);
var htmlPath = await new HtmlReporter().WriteAsync(result, outDir, CancellationToken.None);

Console.WriteLine();
Console.WriteLine($"Result : {result.Status}");
Console.WriteLine($"JSON   : {jsonPath}");
Console.WriteLine($"HTML   : {htmlPath}");

return result.Status == RunStatus.Failed ? 1 : 0;

// ---- helpers ----

static IVsDriver SelectDriver(string? requested)
{
    var choice = requested?.ToLowerInvariant()
        ?? (OperatingSystem.IsWindows() ? "windows" : "simulation");
    return choice switch
    {
        "windows" => new WindowsVsDriver(),
        "simulation" => new SimulationDriver(),
        _ => throw new ArgumentException($"Unknown driver '{requested}'.")
    };
}

static TestCase Clone(TestCase c, IReadOnlyDictionary<string, object?> data) => new()
{
    Id = c.Id,
    Title = c.Title,
    Priority = c.Priority,
    Feature = c.Feature,
    Classification = c.Classification,
    Tags = c.Tags,
    Targets = c.Targets,
    Data = data,
    Config = c.Config,
    Steps = c.Steps
};

internal sealed record CliArgs(
    string CaseFile,
    string? Driver,
    string WorkDir,
    string OutDir,
    string? Vs,
    IReadOnlyList<KeyValuePair<string, string>> Data)
{
    public static CliArgs? Parse(string[] args)
    {
        if (args.Length < 2 || args[0] != "run")
            return null;

        var caseFile = args[1];
        string? driver = null, vs = null;
        var workDir = "./artifacts/work";
        var outDir = "./reports/out";
        var data = new List<KeyValuePair<string, string>>();

        for (var i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--driver": driver = Next(args, ref i); break;
                case "--work": workDir = Next(args, ref i); break;
                case "--out": outDir = Next(args, ref i); break;
                case "--vs": vs = Next(args, ref i); break;
                case "--data":
                    var kv = Next(args, ref i);
                    var eq = kv.IndexOf('=');
                    if (eq > 0)
                        data.Add(new(kv[..eq], kv[(eq + 1)..]));
                    break;
            }
        }

        return new CliArgs(caseFile, driver, workDir, outDir, vs, data);
    }

    private static string Next(string[] args, ref int i)
        => ++i < args.Length ? args[i] : throw new ArgumentException("Missing argument value.");
}
