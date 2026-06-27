# VsAuto — AI-Powered Visual Studio UI Automation

VsAuto runs your repetitive Visual Studio UI test cases automatically. Test cases are
written as **YAML** (no coding), executed against a real VS instance on Windows, validated
against ground truth (the generated `.csproj`, the build log, process exit codes), and
turned into **HTML + JSON reports** with screenshots and an AI-drafted bug summary when
something fails.

- **What & why** → [PRD.md](PRD.md) (the original requirements)
- **Architecture & design decisions** → [DESIGN.md](DESIGN.md)
- **This file** → how to install and run it

> AI uses the **GitHub Copilot CLI** you already have — **no API keys required**.

---

## Quick start (Windows company PC)

You've cloned the repo. Here's the whole path from zero to a report. Run these in
**PowerShell** from the repo root.

### 1. Install prerequisites

| Tool | Needed for | Install |
|---|---|---|
| **.NET 10 SDK** | building & running VsAuto | `winget install Microsoft.DotNet.SDK.10` |
| **Visual Studio** | the real VS automation (under test) | usually already installed on a QA box |
| **GitHub Copilot CLI** | AI failure analysis / bug summaries | `npm install -g @github/copilot` then `copilot` once to sign in |

Check they're available:

```powershell
dotnet --version      # expect 10.x
copilot --version     # optional; AI features use this
```

> No Copilot and no API key? VsAuto still runs fully — it just skips the AI commentary and
> relies on the deterministic checks. Nothing breaks.

### 2. Build and sanity-check

```powershell
dotnet build VsAuto.slnx
dotnet test  VsAuto.slnx      # 11 unit tests should pass
```

### 3. Run a test case

VsAuto ships with one worked example, [TC11](tests/cases/TC11_SingleTfm.yaml) (create a
console app → build → run → verify the Target Framework).

**A) Against real Visual Studio** (the actual use case on your Windows box):

```powershell
dotnet run --project src\VsAuto.Cli -- run tests\cases\TC11_SingleTfm.yaml --driver windows --vs 18.0
```

> First-run note: the FlaUI **New-Project** UIA flow has version-specific automation IDs
> that must be tuned to your VS image before real-IDE runs fully drive project creation
> (build/run validation already use MSBuild ground truth). See
> [WindowsVsDriver.cs](src/VsAuto.Driver.Windows/WindowsVsDriver.cs) `TODO`s. Use option B to
> see a green run today.

**B) Without driving the IDE** (fast smoke test — uses the `dotnet` CLI to simulate VS;
works even on a box without VS):

```powershell
dotnet run --project src\VsAuto.Cli -- run tests\cases\TC11_SingleTfm.yaml --driver simulation
```

On Windows the **default driver is `windows`**, so you can omit `--driver` for real runs.

### 4. Read the result

The run prints a summary and writes two reports:

```
Result : Passed
JSON   : reports\out\TC11.result.json     <- for CI / dashboards
HTML   : reports\out\TC11.report.html     <- open this in a browser
```

Open the HTML report to see each step, its assertions, duration, screenshots, and (on
failure) the AI analysis. If a step fails, a full reproduction bundle is saved under
`artifacts\work\<TC>\<run>\evidence\` (failure screenshot, build log, a `failure-*.txt`
descriptor with the failure classification).

**Exit code:** `0` = passed, `1` = failed — wire this into CI to gate a build.

---

## AI analysis via Copilot CLI (no API key)

If `copilot` is on your PATH, VsAuto uses it automatically for failure root-cause and
bug-summary drafting. Pick any model your Copilot subscription exposes:

```powershell
# Pin a model for this run:
dotnet run --project src\VsAuto.Cli -- run tests\cases\TC11_SingleTfm.yaml --ai-model claude-opus-4.8
dotnet run --project src\VsAuto.Cli -- run tests\cases\TC11_SingleTfm.yaml --ai-model gpt-5.5

# Or set it once for the session:
$env:VSAUTO_COPILOT_MODEL = "claude-opus-4.8"
```

Use the model name exactly as Copilot lists it (check your Copilot model picker if a name
is rejected). The Copilot CLI path is **text-based** (it analyzes the error, build output,
and context — not the screenshot pixels); deterministic checks remain the source of truth
either way.

---

## Command reference

```
dotnet run --project src\VsAuto.Cli -- run <case.yaml> [options]
```

| Option | Meaning | Default |
|---|---|---|
| `--driver windows\|simulation` | Drive real VS (FlaUI) or simulate via dotnet CLI | `windows` on Windows |
| `--ai auto\|copilot\|claude\|none` | AI backend | `auto` (Copilot if installed) |
| `--ai-model <name>` | Model for Copilot CLI (e.g. `claude-opus-4.8`, `gpt-5.5`) | Copilot default |
| `--vs <version>` | Visual Studio version hint for the windows driver | — |
| `--work <dir>` | Working-dir root (bound to `${WorkDir_Root}` in cases) | `.\artifacts\work` |
| `--out <dir>` | Report output folder | `.\reports\out` |
| `--data key=value` | Override/add a case variable (repeatable) | — |

---

## Add your own test case (no code)

Create a `.yaml` file in `tests\cases\`. Copy [TC11_SingleTfm.yaml](tests/cases/TC11_SingleTfm.yaml)
and edit it — it's validated against [the schema](tests/cases/_schema/test-case.schema.json).

```yaml
id: TC30
title: My console app check
priority: P1
feature: Project Creation
data:
  projectName: MyApp
  location: "${WorkDir_Root}/TC30"
steps:
  - action: launch_vs
    with: { cleanInstance: true }
  - action: new_project
    with: { template: "Console App", name: "${data.projectName}", location: "${data.location}" }
    assert:
      - type: csproj_property
        property: TargetFramework
        equals: "net10.0"
  - action: build
    assert:
      - type: build_succeeded
  - action: run
    assert:
      - type: process_exit_code
        equals: 0
```

Then:

```powershell
dotnet run --project src\VsAuto.Cli -- run tests\cases\TC30.yaml
```

**Implemented actions:** `launch_vs`, `new_project`, `build`, `run`, `screenshot`,
`wait_for`. (The schema also reserves `open_solution`, `debug`, `open_test_explorer`,
`edit_file`, etc. for upcoming phases — see [DESIGN.md](DESIGN.md) §11.)
**Implemented assertions:** `csproj_property`, `build_succeeded`, `stdout_contains`,
`process_exit_code`, `file_exists`, `file_contains`, `ai_visual` (advisory).

Need a new action? Add one `IStepHandler` class — the engine needs no changes (see
[DESIGN.md](DESIGN.md) §6/§14).

---

## Project layout

```
src\
  VsAuto.Core/              models, interfaces, YAML loader, variable resolver
  VsAuto.Engine/            step runner: lifecycle, retry, recovery, classification
  VsAuto.Validators/        ground-truth assertions (csproj/build/file/exit/ai_visual)
  VsAuto.Ai/                ILlmProvider: Copilot CLI (default), Claude, null
  VsAuto.Reporting/         JSON + HTML reporters
  VsAuto.Driver.Simulation/ cross-platform driver (dotnet CLI) — CI & no-VS boxes
  VsAuto.Driver.Windows/    FlaUI/UIA3 driver — drives real Visual Studio
  VsAuto.Cli/               the `vsauto run ...` entry point
tests\
  VsAuto.Tests/             unit tests
  cases/                    YAML test cases + JSON schema
```

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| `copilot --version` not found | AI is optional — runs continue without it. Install `@github/copilot` and run `copilot` once to sign in to enable AI. |
| Copilot rejects `--ai-model` value | Use the exact model name from your Copilot model picker. |
| `Could not locate devenv.exe` | The windows driver uses `vswhere`; pass `--vs <version>` or confirm VS is installed. |
| New-Project step does nothing on real VS | The FlaUI New-Project UIA flow has version-specific automation IDs to tune on your VS image — see the `TODO` in [WindowsVsDriver.cs](src/VsAuto.Driver.Windows/WindowsVsDriver.cs). Use `--driver simulation` meanwhile. |
| Want to verify without VS | Use `--driver simulation`. |
