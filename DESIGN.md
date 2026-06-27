# DESIGN — AI-Powered Visual Studio UI Automation Platform

> Answers the brief in [README.md](README.md). Written from the stance of a senior
> architect: where the brief's assumptions are weak, this document says so and recommends
> a stronger path. The single biggest change from the implied stack: **drive the UI with
> FlaUI (UIA3), not WinAppDriver**, and keep **AI out of the click-driving hot path**.

## TL;DR of the recommendation

- **Deterministic core, AI assist.** UI Automation (UIA3 via FlaUI) performs actions and
  most assertions. AI is used for visual validation where there is no ground truth,
  failure root-cause analysis, bug-report drafting, and locator self-healing — never as
  the primary way to decide *where to click*. This keeps runs reproducible, cheap, and
  auditable while still capturing AI's flexibility.
- **Prefer ground truth over the screen.** To check a Target Framework, read the `.csproj`
  and the MSBuild log — don't OCR a dropdown. UI scraping and AI vision are fallbacks.
- **.NET / C# engine.** Same stack as the system under test; first-class UIA; trivial
  `dotnet`/MSBuild log parsing; EnvDTE interop when the UI is too fragile.
- **YAML test cases** with a fixed action vocabulary so QA authors new cases without code.
- **Runner-per-machine, fan-out for scale.** One VS instance per VM (serial within a box);
  parallelism across the environment matrix.

The 14 requested deliverables are answered in the numbered sections below.

---

## 1. Recommended architecture

A layered pipeline with a clear control plane / runner / reporting split:

```
Test Catalog (YAML in git)
        │
   Orchestrator/Scheduler ── selects cases, resolves env matrix, fans out
        │
   Runner Agent (one per target machine)
        ├─ Execution Engine (Step Runner): drives the step lifecycle
        ├─ VS Driver: FlaUI/UIA3 (+ EnvDTE fallback) — performs actions
        ├─ Validators: csproj / MSBuild / filesystem / UI / ai_visual
        ├─ Evidence Collector: screenshots, logs, video, ActivityLog.xml
        └─ AI Services: visual validate, RCA, bug draft, locator self-heal
        │
   Artifact Store ──► Reporter (HTML + JSON, trends) ──► optional Bug draft (ADO/GitHub)
```

Diagrams (component, sequence, fan-out) are in
[docs/architecture-diagram.md](docs/architecture-diagram.md).

**Why this shape.** The brief lists "AI" prominently, but an enterprise daily-regression
suite needs *determinism and reproducibility first*. Layering lets each concern evolve
independently: the action vocabulary, the validators, and the AI services each have their
own contracts, so VS UI churn touches the Driver only, and AI provider swaps touch
`AI Services` only.

**Data flow.** YAML case → resolved step list (with `${var}` substitution) → per-step
action on the Driver → assertions evaluated by Validators (deterministic first, AI last) →
evidence captured on failure → result object → Reporter. Everything a developer needs to
reproduce a failure (inputs, env, logs, screenshots, generated files) is bundled per run.

---

## 2. Architecture diagram

See [docs/architecture-diagram.md](docs/architecture-diagram.md) for three Mermaid views:
component/layer, single-case execution sequence, and multi-environment fan-out.

---

## 3. Component responsibilities

| Component | Responsibility | Key inputs | Key outputs |
|---|---|---|---|
| **Test Catalog** | Versioned YAML cases + schema | — | Case definitions |
| **Orchestrator/Scheduler** | Select cases, resolve target matrix, dispatch to runners, collect results | Case set, env inventory | Per-env run jobs |
| **Execution Engine (Step Runner)** | Step lifecycle: precondition → act → assert → retry/recover → evidence | Resolved steps | Step + case results |
| **VS Driver** | Translate actions into UIA operations; EnvDTE fallback; locator resolution | Action + params | UI state, element handles |
| **Validators** | Evaluate assertions, ground-truth first | Assertion spec, csproj/logs/files/UI | Pass/fail + detail |
| **Evidence Collector** | Capture screenshots, build logs, VS ActivityLog, generated files, optional video | Failure/step events | Evidence bundle |
| **AI Services** | Visual validation, failure RCA, bug-summary drafting, locator self-heal suggestions | Screenshot, UIA tree, logs, context | Verdicts (advisory), text, suggested locators |
| **Artifact Store** | Persist run artifacts under `runs/<runId>/` | Evidence, results | Durable artifacts |
| **Reporter** | Render HTML + JSON, aggregate trends | Results + artifacts | Reports |
| **LLM Provider (`ILlmProvider`)** | Pluggable model backend (Claude / Azure OpenAI) | Prompts + images | Model responses |

---

## 4. Technology comparison

| Technology | Use? | Rationale |
|---|---|---|
| **Windows UI Automation (UIA3)** | ✅ Core | The supported, modern accessibility API; exposes VS's element tree (WPF-based VS surfaces it well). Fast, in-process, reliable. |
| **FlaUI** | ✅ Core | Best-maintained .NET wrapper over UIA3; clean API, screenshot support, patterns (Invoke/Value/Expand). Chosen Driver foundation. |
| **WinAppDriver** | ❌ Avoid | Effectively unmaintained, Appium/WebDriver indirection adds latency and a moving part, weak modern-Windows/ARM64 story. Its only edge (Selenium-style API) isn't worth the fragility. |
| **EnvDTE / VS automation (DTE)** | ✅ Selective | Use for operations that are painful/fragile via UI: open solution, build, query project properties, manipulate the running IDE. Complements UIA; not a full replacement (some dialogs aren't DTE-addressable). |
| **MSBuild / `dotnet` CLI + binlog** | ✅ Core (validation) | Ground truth for build success and detected TFM. Far more reliable than reading the Output window. |
| **PowerShell** | ✅ Infra only | Environment prep, VS install detection (`vswhere`), VM/runner setup, cleanup. Not for in-IDE assertions. |
| **Computer Vision / OCR** | ⚠️ Fallback | Only when an element has no UIA identity (custom-drawn surfaces, splash states). Brittle; quarantine behind a capability flag. |
| **LLM (Claude / Azure OpenAI)** | ✅ Assist | Screenshot understanding, failure RCA, bug-summary drafting, locator self-heal. Behind `ILlmProvider`. |
| **AI Agents (autonomous tool-driving)** | ⚠️ Narrow | Useful for *exploratory* triage and self-heal suggestions, **not** for executing the daily deterministic suite. Keep agency advisory + logged. |
| **MCP** | ⚠️ Optional | A clean way to expose the engine's actions/queries to an AI agent (e.g., for interactive debugging or future agentic exploration). Not required for the deterministic path. |
| **Appium-Windows** | ❌ Avoid | Same family as WinAppDriver; no advantage over FlaUI here. |

---

## 5. Folder structure

```
.
├─ README.md                     # original brief
├─ DESIGN.md                     # this document
├─ docs/
│  └─ architecture-diagram.md
├─ tests/
│  └─ cases/
│     ├─ _schema/test-case.schema.json
│     └─ TC11_SingleTfm.yaml      # worked example
├─ src/                          # (Phase 0 scaffold — see §11)
│  ├─ VsAuto.Engine/             # step runner, lifecycle, retry/recovery
│  ├─ VsAuto.Driver/             # FlaUI/UIA + EnvDTE, locator resolution
│  ├─ VsAuto.Validators/         # csproj/msbuild/file/ui/ai assertions
│  ├─ VsAuto.Ai/                 # ILlmProvider + Claude/AzureOpenAI impls
│  ├─ VsAuto.Evidence/           # screenshots, logs, video, ActivityLog
│  ├─ VsAuto.Reporting/          # HTML+JSON reporter, trends
│  ├─ VsAuto.Orchestrator/       # selection, env matrix, fan-out
│  └─ VsAuto.Cli/                # `vsauto run tests/cases/TC11_SingleTfm.yaml`
├─ runners/                      # PowerShell/VM provisioning per environment
├─ config/
│  ├─ appsettings.json           # defaults
│  └─ environments/*.json        # per-target (VS version, arch, paths)
├─ artifacts/runs/<runId>/       # generated evidence (gitignored)
└─ reports/out/                  # generated reports (gitignored)
```

---

## 6. Test case format

**YAML**, validated by [tests/cases/_schema/test-case.schema.json](tests/cases/_schema/test-case.schema.json),
with a fixed **action vocabulary** so QA writes data, not code. Worked example:
[tests/cases/TC11_SingleTfm.yaml](tests/cases/TC11_SingleTfm.yaml).

A case has: `id`, `title`, `priority`, `feature`, optional `targets` (env matrix), `data`
(variables referenced as `${...}`), `config` (onFailure / timeout / retries), and `steps`.
Each step has an `action`, a `with` parameter block, optional per-step `retries`/`timeout`,
and an `assert` list.

**Action vocabulary (extensible):** `launch_vs`, `open_solution`, `new_project`,
`set_option`, `edit_file`, `replace_file`, `build`, `run`, `debug`, `open_test_explorer`,
`run_tests`, `wait_for`, `screenshot`.

**Assertion types, ground-truth-first:** `csproj_property`, `build_succeeded`,
`file_exists`, `file_contains`, `process_exit_code`, `stdout_contains`, `ui_element_exists`,
`ui_text_equals`, and `ai_visual` (advisory by default). New scenarios are added by writing
YAML; new *capabilities* (a brand-new action) are the only thing needing engine code — and
that's a plugin, not a core change (see §10/§14 extensibility).

---

## 7. Execution engine design

**Step lifecycle:** resolve `${vars}` → check precondition → perform action via Driver →
evaluate assertions (deterministic first, `ai_visual` last) → on failure: retry with
exponential backoff up to `retries` → on exhausted retries: capture evidence, run AI RCA,
then **recover** or **abort** per `onFailure`.

**Validation** runs after each action; an assertion marked `advisory: true` (typical for
`ai_visual`) records a finding but never fails the run on its own.

**Retry & timeouts:** per-step and per-case timeouts; backoff between retries; idempotent
actions are safe to retry (e.g., `wait_for`), stateful ones (`new_project`) recover by
cleaning the workdir first.

**Error classification:** `Infrastructure` (VS didn't launch, machine issue → retry/requeue),
`ProductDefect` (assertion failed against ground truth → real bug candidate),
`Flake` (passed on retry → quarantine signal), `AutomationError` (locator broke → self-heal
path). Classification drives both reporting and whether a bug is drafted.

**Recovery:** restart VS with a clean user hive, delete/recreate the workdir, kill orphaned
`devenv`/`MSBuild` processes between cases.

**Parallel execution:** **one VS per runner/VM, serial within a box** — VS holds global
state (settings hive, single foreground UIA session) that makes intra-machine parallelism
unreliable. Scale by fanning the case set across machines/VMs via the Orchestrator.

---

## 8. AI workflow

**Where AI runs:** at validation time (only for `ai_visual` assertions), and on failure
(RCA + bug draft), plus an offline self-heal path when a locator stops resolving.

**Inputs:** the failing/checkpoint screenshot, a serialized UIA subtree (element names,
control types, automation ids), the build/IDE logs, and the step context (what we tried,
expected vs actual).

**Outputs:**
- *Visual verdict* — pass/fail + reason (advisory unless a step explicitly opts in).
- *Failure root cause* — concise hypothesis tying evidence to a likely defect or flake.
- *Bug summary* — title, repro steps (derived from the executed YAML), env, expected vs
  actual, attached evidence — ready to paste into ADO/GitHub.
- *Locator self-heal* — suggested replacement locator when the old one fails; **proposed,
  not auto-applied** (goes through human review — see §14).

**Provider abstraction:** `ILlmProvider` with Claude and Azure OpenAI implementations,
selectable per environment. Multimodal (image + text) is required.

**Guardrails / where AI must NOT be used:**
- Not for deciding clicks in the deterministic suite (reproducibility).
- Not as the sole authority for a pass/fail when ground truth exists (always prefer
  csproj/build/file checks).
- Not to auto-modify test cases or locators without review.
- Every AI call's prompt, inputs, and output are logged for audit and cost tracking.

---

## 9. Logging strategy

- **Execution logs:** structured (JSON) per step — action, params, timings, result,
  classification. Human-readable console mirror.
- **Screenshots:** on every step boundary at low cost + full-resolution on failure; stored
  under `runs/<id>/screens/` with step correlation ids.
- **Video:** optional per-case screen capture (off by default for cost; on for P0/flaky).
- **Build logs:** MSBuild **binlog** + text; the authoritative build/TFM source.
- **Visual Studio logs:** collect `ActivityLog.xml` (`devenv /log`) and relevant `%TEMP%`
  diagnostics into the bundle.
- **Error collection / repro bundle:** generated project files, the resolved YAML, env
  descriptor, all logs, and screenshots zipped per failing case so a developer can repro.

---

## 10. Reporting strategy

- **Per-run report (HTML + JSON):** passed/failed counts, per-case duration, environment
  (OS, arch, VS version), screenshots, failure reason + classification, AI analysis, and
  the suggested bug summary.
- **Machine-readable JSON** for CI gates and trend aggregation across daily runs (pass
  rate, flake rate, duration trend per case/env).
- **Bug drafting:** optional one-click creation of an Azure DevOps / GitHub work item from
  the AI bug summary, with evidence attached — created as draft for human confirmation.

---

## 11. Implementation phases (MVP → Production)

- **Phase 0 — Walking skeleton (MVP).** `src/` scaffold + CLI; load YAML + schema validate;
  FlaUI driver for `launch_vs`/`new_project`/`build`/`run`; `csproj_property` +
  `build_succeeded` validators; JSON result + minimal console report. **Exit:** TC11 runs
  end-to-end on one machine and reads the TFM from the generated `.csproj`.
- **Phase 1 — Breadth + evidence.** Add TC10, TC14; Evidence Collector (screens, binlog,
  ActivityLog); HTML reporter.
- **Phase 2 — AI assist.** `ILlmProvider` (Claude), `ai_visual` advisory verdicts, failure
  RCA + bug-summary drafting, locator self-heal *suggestions*.
- **Phase 3 — Scale.** Orchestrator with env matrix + fan-out across VMs; trend dashboard;
  flake quarantine.
- **Phase 4 — Hard cases + hardening.** TC22 (Test Explorer), TC21 (Live Unit Testing),
  TC20 (debug profiles / launchSettings sync), TC07 (compatibility warning); secrets,
  RBAC, golden VM images, CI integration.

---

## 12. Risks & trade-offs

| Risk | Impact | Mitigation |
|---|---|---|
| VS UI churn across versions/updates | Locators break | Ground-truth-first assertions; locators centralized in Driver; AI self-heal suggestions; pin VS versions per env |
| ARM64 coverage | Tooling gaps | FlaUI/UIA is arch-agnostic; validate runners on ARM64 early; avoid x64-only native deps |
| AI non-determinism & cost | Flaky/expensive verdicts | AI advisory-only by default; deterministic checks authoritative; cache/log all calls; gate video + AI on P0/flaky |
| Timing flakiness | False failures | Explicit `wait_for`, retries with backoff, flake classification + quarantine |
| Single-VS-per-box throughput | Slower runs | Horizontal fan-out across VMs; prioritize P0 set |
| First-run-default state (TC10) | State leakage | `cleanInstance` user hive per case where the test depends on first-run defaults |

---

## 13. Alternative approaches (and why not)

- **Pure script, no AI:** simplest and most reproducible, but loses the brief's goals —
  visual validation, RCA, and bug drafting. We keep the deterministic core *and* add AI as
  assist, getting both.
- **Record/replay (coded UI style):** fast to author, extremely brittle to UI change and
  hard to review in git. Rejected in favor of declarative YAML + stable Driver locators.
- **WinAppDriver / Appium-Windows:** added indirection and weak maintenance; FlaUI is a
  better-maintained, lower-latency path to the same UIA tree.
- **Fully agentic computer-use driving the IDE:** flexible but non-deterministic, slow, and
  costly for a *daily* suite; valuable only for exploratory triage. Kept as an optional MCP
  surface, not the execution path.

---

## 14. Best practices for enterprise adoption

- **Cases in git, reviewed like code** — PRs, schema validation in CI, ownership per feature.
- **Golden environments** — versioned VM images per target (OS/arch/VS) for reproducibility.
- **Stable locators centralized** — all element identification in the Driver; AI self-heal
  produces a PR a human approves before locators change (no silent drift).
- **Flake quarantine** — auto-quarantine cases that only pass on retry; report separately.
- **Secrets & access** — LLM keys and runner creds in a vault; least-privilege runners.
- **Cost & audit** — log every AI call (prompt, image refs, tokens, cost); budget alerts.
- **CI integration** — nightly fan-out, JSON gate, trend dashboard; P0 set as a fast BVT.
- **Extensibility contract** — new scenarios = new YAML (no code). New *actions* are
  plugins implementing a Driver/step interface; the core engine stays closed to change.

---

## Running the MVP (Phase 0 — implemented)

The walking skeleton in `src/` is built and runs end-to-end (.NET 10). On macOS/Linux the
cross-platform **simulation driver** exercises the full pipeline via the `dotnet` CLI; on
Windows the **FlaUI driver** drives the real IDE (same engine, swapped driver).

```bash
dotnet build VsAuto.slnx                       # builds all 9 projects (Windows driver stubs off-Windows)
dotnet test VsAuto.slnx                         # 11 unit tests (resolver, loader, validators, engine)

# Run TC11 end-to-end. WorkDir_Root is bound automatically; --data overrides case variables.
dotnet run --project src/VsAuto.Cli -- run tests/cases/TC11_SingleTfm.yaml --data expectedTfm=net10.0
# → Passed; writes reports/out/TC11.result.json and TC11.report.html

# Same case expecting net11.0 on a net10 SDK → detected as a ProductDefect, evidence captured:
dotnet run --project src/VsAuto.Cli -- run tests/cases/TC11_SingleTfm.yaml
# → Failed (exit 1), classification ProductDefect, failure bundle under artifacts/work/TC11/<run>/evidence
```

To run against real Visual Studio on Windows: `--driver windows --vs 18.0`. Set
`ANTHROPIC_API_KEY` to activate the Claude AI provider for advisory visual checks and
bug-summary drafting (otherwise the null provider keeps everything deterministic).

## Verification checklist for this design pass

- [x] All 14 requested deliverables answered (§1–§14).
- [x] `tests/cases/TC11_SingleTfm.yaml` validates against the JSON Schema.
- [x] MVP solution builds (`dotnet build`) and unit tests pass (11/11).
- [x] TC11 runs end-to-end (pass path green; mismatch path classified as ProductDefect with evidence).
- [x] Phase 0 references only components defined in §1–§3.
