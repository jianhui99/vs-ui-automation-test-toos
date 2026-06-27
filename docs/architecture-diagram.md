# Architecture Diagrams

## 1. Component / layer view

```mermaid
flowchart TB
    subgraph Catalog["Test Catalog (git)"]
        YAML["YAML test cases\n+ JSON Schema"]
    end

    subgraph Control["Control Plane"]
        ORCH["Orchestrator / Scheduler\n(selects cases, picks runners,\nfan-out across environments)"]
    end

    subgraph Runner["Runner Agent (per target machine)"]
        ENG["Execution Engine\n(Step Runner)"]
        DRV["VS Driver\nFlaUI/UIA3 + EnvDTE"]
        VAL["Validators\n(csproj / MSBuild / files / UI)"]
        EVID["Evidence Collector\n(screens, logs, video, ActivityLog)"]
        AISVC["AI Services\n(vision validate, RCA, bug draft,\nlocator self-heal)"]
    end

    subgraph Output["Reporting Plane"]
        STORE["Artifact Store\n(runs/<id>/...)"]
        REP["Reporter\nHTML + JSON, trends"]
        BUG["Bug draft\n(ADO / GitHub issue)"]
    end

    LLM["LLM Provider\n(Claude / Azure OpenAI)\nvia ILlmProvider"]

    YAML --> ORCH --> ENG
    ENG <--> DRV
    ENG --> VAL
    ENG --> EVID
    VAL --> AISVC
    EVID --> AISVC
    AISVC <--> LLM
    EVID --> STORE
    AISVC --> STORE
    STORE --> REP
    AISVC --> BUG
```

## 2. Single test-case execution flow (sequence)

```mermaid
sequenceDiagram
    participant O as Orchestrator
    participant E as Execution Engine
    participant D as VS Driver (FlaUI)
    participant V as Validators
    participant C as Evidence Collector
    participant A as AI Services
    participant R as Reporter

    O->>E: dispatch(TC11, env)
    E->>D: launch_vs(cleanInstance)
    D-->>E: VS ready (UIA root)
    loop each step
        E->>D: perform(action, params)
        D-->>E: ui result
        E->>V: assert(deterministic: csproj/build/file)
        alt assertion needs no ground truth
            E->>A: ai_visual(screenshot + UIA tree)
            A-->>E: advisory verdict
        end
        alt step fails
            E->>C: capture(screens, logs, files, ActivityLog)
            E->>A: analyze failure -> root cause + bug summary
            E->>E: retry/backoff or recover (restart VS / clean workdir)
        end
    end
    E->>C: finalize evidence bundle
    E->>R: emit result (pass/fail, duration, evidence, AI analysis)
    R-->>O: report ready
```

## 3. Multi-environment fan-out

```mermaid
flowchart LR
    O["Orchestrator"] -->|TC10,TC11,...| Q{{"Dispatch queue\n(per target matrix)"}}
    Q --> R1["Runner: Win11 25H2 x64"]
    Q --> R2["Runner: Win11 24H2 ARM64"]
    Q --> R3["Runner: Win Server 2025 ARM64"]
    R1 --> S["Artifact Store"]
    R2 --> S
    R3 --> S
    S --> AGG["Aggregated Report\n(per env + trend)"]
```

> **Parallelism rule:** one Visual Studio instance per runner/VM (serial within a box to
> avoid UIA contention and shared global VS state); parallelism comes from fanning the same
> case set across multiple machines/VMs.
