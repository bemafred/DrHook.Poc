# ADR-001: DrHook.Poc — Runtime Inspection and Controlled Stepping via MCP stdio

**Status:** Accepted  
**Date:** 2026-03-04  
**Author:** Martin Fredriksson  
**Component:** DrHook.Poc  
**Target:** .NET 10 LTS  

---

## Context

During Mercury development, two bug categories proved systematically resistant to log-based debugging:

- **Infinite loops** — no output, no signal, only timeout
- **Deadlocks / concurrency faults** — TOCTOU races visible only after the fact, if at all

Claude Code's default strategy — inserting `Console.WriteLine` calls — is structurally blind between instrumented points. For the above categories, the signal arrives too late or not at all.

A deeper observation motivates this ADR: the current AI coding discipline stops at green tests. This is Engineering only — known knowns, verified. A human developer who steps through working code after tests pass is not debugging — they are confirming that their generative model matches execution reality. This is Epistemics in practice, and it is absent from AI-assisted development workflows.

---

## The Feedback Loop

The compiler/lint/test feedback loop already works reliably with LLMs:

| Signal | Coverage | LLM capability |
|--------|----------|----------------|
| Compilation failure | Static structure | Proven — works well |
| Lint warning | Style / static analysis | Proven — works well |
| Test failure | Expected dynamic behaviour | Proven — works well |
| Runtime exception | Unexpected dynamic behaviour | Partial |
| **Infinite loop / deadlock** | **Unexpected non-termination** | **Gap — no signal** |
| **Working code behaviour** | **Causal understanding** | **Gap — never attempted** |

DrHook closes both gaps: the diagnostic gap (broken code with no signal) and the epistemic gap (working code with no understanding).

**Compile → Test → Inspect.** The third step has never existed in AI coding workflows.

---

## Decision

Build **DrHook.Poc** as an MCP stdio server with two layers:

### Layer 1: Observation (EventPipe / DiagnosticsClient)

Passive observation of a running process. Answers: "What was the process doing during this window?"

- Attaches via `DiagnosticsClient` (Microsoft.Diagnostics.NETCore.Client)
- Captures EventPipe trace: thread samples, exceptions, GC events, lock contention
- Returns **summarized** diagnostic data, not raw traces (Cross-LLM refinement #1)
- Anchors every observation with assembly version (Cross-LLM refinement #2)
- Requires a hypothesis from the consumer (Cross-LLM refinement #3)

### Layer 2: Stepping (DAP / netcoredbg)

Controlled execution. Answers: "What does this code do, line by line?"

- Uses netcoredbg (Samsung, MIT license) as the debug engine
- Speaks DAP (Debug Adapter Protocol) over stdio — DrHook owns the DAP client (BCL only)
- Set breakpoint → halt → inspect variables → step → inspect again
- Claude Code narrates execution in the terminal in real time

### Tool Surface (POC scope)

| Tool | Layer | Description |
|------|-------|-------------|
| `drhook:processes` | — | List running .NET processes |
| `drhook:snapshot` | Observation | Summarized EventPipe capture with hypothesis |
| `drhook:step-launch` | Stepping | Attach, set breakpoint, run to it |
| `drhook:step-next` | Stepping | Step one line, return state |
| `drhook:step-vars` | Stepping | Inspect local variables |
| `drhook:step-stop` | Stepping | End session, prompt for reflection |

---

## Cross-LLM Refinements

Four refinements derived from cross-model assessment (ChatGPT, Grok, Gemini, Perplexity):

### 1. Signal Summarization

**Risk identified:** Raw EventPipe traces overwhelm LLM context and reasoning capacity.

**Mitigation:** StackInspector produces structured summaries — thread state overview, hotspot detection (single thread >80% of samples), GC pressure flags, contention counts, anomaly list. Claude Code receives a diagnosis-ready artifact, not a trace dump.

### 2. Code Version Anchoring

**Risk identified (Gemini, Perplexity):** Bitemporal desync — observations stored in Mercury become toxic if the code they describe has been refactored.

**Mitigation:** Every observation and stepping session captures the assembly version of the target process. When Mercury stores the triple, the code version is part of the provenance. Old observations are queryable but contextually grounded in their code version.

### 3. Hypothesis Requirement

**Risk identified (Gemini — "Oracle Fallacy"):** An LLM might pattern-match stack traces rather than genuinely reason about them, moving hallucination up one abstraction level.

**Mitigation:** Every observation tool requires a `hypothesis` field — the consumer must state what they expect BEFORE inspecting. The delta between expectation and reality is captured explicitly. Without a prior expectation, an observation has no epistemic value. The response includes a `deltaPrompt` encouraging the consumer to articulate what surprised them.

### 4. Falsification Criteria

**Risk identified (ChatGPT, Perplexity):** Feedback ossification — incorrect causal narratives stored in Mercury could self-reinforce over time.

**Mitigation:** The long hypothesis must be falsifiable. Specific criteria:

- If after 50 inspection sessions, DrHook + Mercury shows no measurable improvement in diagnostic accuracy over baseline Claude Code (no DrHook), the hypothesis that runtime observation enables causal model building is weakened.
- If after 200 sessions, accumulated observations do not compound — i.e. session 200 is not meaningfully faster or more accurate at diagnosis than session 50 — the memory compounding hypothesis is falsified.
- If Claude Code's hypotheses (captured via the hypothesis field) do not improve in accuracy over time, the system is pattern-matching traces, not building causal models (Oracle Fallacy confirmed).

These criteria must be measured, not asserted.

---

## Sovereignty Stack

| Layer | Component | Owner | Cross-platform |
|-------|-----------|-------|---------------|
| MCP protocol | McpStdioServer | DrHook (BCL) | Yes — stdio JSON |
| DAP client | DapClient | DrHook (BCL) | Yes — stdio JSON |
| Passive observation | EventPipe / DiagnosticsClient | .NET runtime + official client | Yes — by design |
| Controlled stepping | netcoredbg (MIT) | Samsung (vendorable) | Yes — Win/Mac/Linux |
| Runtime interface | dbgshim | Ships with .NET | Yes — every runtime |

DrHook owns Layers 1 and 2. Layers 3–5 are either open-source or ship with the runtime.

---

## Transport: DOTNET_DiagnosticPorts / EventPipe

The .NET runtime exposes a diagnostic IPC channel via `DOTNET_DiagnosticPorts`. EventPipe is the native cross-platform tracing mechanism. All official diagnostic tools build on it.

**Sovereignty rationale:** The capability is in the runtime. DrHook speaks directly to it. No vsdbg, no IDE dependency, no proprietary tooling.

**Package note:** `Microsoft.Diagnostics.NETCore.Client` is the official Microsoft client library for the diagnostic IPC protocol — not a third-party wrapper. The IPC protocol is open and documented; a future pure-BCL implementation is possible and consistent with sovereignty principles.

---

## netcoredbg: Bridge and Blueprint

netcoredbg (Samsung, MIT license) is used for controlled stepping. It interfaces with dbgshim and ICorDebug (the CLR's native debugging interface) and exposes DAP over stdio.

**Apple Silicon note:** Samsung does not publish pre-built ARM64 macOS binaries. On Apple Silicon Macs, netcoredbg must be built from source (CMake + clang). Rosetta 2 is not viable — an x86_64 netcoredbg cannot attach to an ARM64 .NET process because the CoreCLR debugging libraries (dbgshim, mscordbi) must match the host architecture. See [Getting Started](../getting-started.md#netcoredbg-platform-details) for build instructions.

**Critical insight: netcoredbg is the blueprint for its own replacement.**

The C++ source (~50K lines) documents exactly which ICorDebug methods to call, in what order, with what state transitions. A sovereign C# port — **DrHook.Engine** — would reimplement this logic via P/Invoke to dbgshim, following the same interop pattern proven by Minerva.Interop.Poc (Metal/CUDA/Accelerate via P/Invoke).

**dbgshim is the true sovereign surface.** A small native library shipping with every .NET runtime, with approximately a dozen exported functions that bootstrap ICorDebug access. This is the stable, documented, platform-independent entry point. DrHook.Engine would target this surface directly.

**The strategic sequence:**

1. **DrHook.Poc** — netcoredbg validates the hypothesis
2. **DrHook v1** — netcoredbg as layer 3, both observation and stepping working
3. **DrHook.Engine** — sovereign C# port of netcoredbg core via P/Invoke to dbgshim. netcoredbg removed. DrHook fully sovereign.

**The development of DrHook.Engine would itself be assisted by DrHook** — the cognitive loop builds the tool that enables the cognitive loop. This is not poetry; it is a practical development strategy.

---

## Hosting Patterns

Three first-class hosting patterns:

1. **Test runner host** — DrHook attaches to the test runner process; the test provides controlled conditions
2. **Single-file stepping host** — minimal harness for one inspection scenario; persists as a documented epistemic instrument
3. **CLI host** — for interaction-boundary scenarios not expressible as unit tests

`Host/SteppingHost.cs` exercises all three target bug categories: tight loops, deep recursion, caught exceptions.

---

## Epistemic Coverage

> **Epistemic coverage:** a semantic map of which code paths have been observed under inspection versus which are passing tests but uninspected.

Line coverage measures whether code was executed. Epistemic coverage measures whether execution was *understood*. A path with 100% line coverage and 0% epistemic coverage means tests pass but neither human nor LLM has a verified causal model of why.

DrHook + Mercury accumulates epistemic coverage as triples over time. This is the distress ontology in practice — a queryable record of what the system didn't understand about itself.

Cross-LLM validation: Gemini frames this as a "trust metric" — measuring alignment between an AI's internal logic and execution reality. Perplexity frames it as "cognitive observability." Both framings are compatible and strengthen the concept.

---

## Mercury Integration (deferred from POC)

The POC returns JSON to Claude Code. The v1 component stores each session as triples:

```turtle
drhook:session-20260304-001
    a drhook:InspectionSession ;
    drhook:pid 42301 ;
    drhook:assemblyVersion "1.0.0.0" ;
    drhook:capturedAt "2026-03-04T14:22:00Z"^^xsd:dateTime ;
    drhook:hypothesis "Thread should be idle between queries" ;
    drhook:anomaly "HOTSPOT: Thread 7 consumed 94% of samples" ;
    drhook:delta "Hypothesis falsified — background indexing active" ;
    drhook:eeephase eee:Epistemics ;
    drhook:component sky:Mercury .
```

---

## The Long Hypothesis

If the POC proves that:
1. DrHook can reliably capture execution state (both passive and controlled)
2. Claude Code can reason meaningfully about that state to form causal hypotheses
3. Mercury can accumulate those observations as compounding semantic memory

Then DrHook + Mercury constitutes empirical evidence that an LLM can build genuine causal models of code through repeated observation — moving from statistical pattern matching to grounded mechanical understanding.

### Falsification Risks (explicitly named)

- **Oracle Fallacy:** The LLM pattern-matches traces instead of reasoning causally. Detected by: hypothesis accuracy not improving over time.
- **Feedback ossification:** Incorrect causal narratives self-reinforce in Mercury. Detected by: diagnostic accuracy plateauing or degrading despite increasing observations.
- **Heisenberg problem:** EventPipe/DAP attachment perturbs timing enough to hide concurrency bugs. Bounded risk — EventPipe overhead is low by design.
- **State explosion:** Runtime state space overwhelms LLM reasoning capacity. Mitigated by signal summarization (refinement #1).

The POC is the first experimental apparatus for testing this hypothesis.

---

## Consequences

**Positive:**
- Closes the final gap in the LLM coding feedback loop (diagnostic)
- Introduces the first "Inspect" step in AI coding workflows (epistemic)
- Establishes epistemic coverage as a first-class development metric
- Provides v2.0 diagnostic infrastructure for Lucy, James, Sky, Minerva
- Sovereign — no proprietary dependencies, cross-platform
- Clear upgrade path to fully sovereign DrHook.Engine via dbgshim

**Negative / risks:**
- netcoredbg is a runtime dependency (mitigated: MIT, vendorable, replaceable)
- EventPipe symbol resolution requires additional work beyond POC
- Context window cost for stepping sessions must be managed
- Falsification risks must be actively measured, not assumed away

**Deferred:**
- Mercury triple storage of inspection sessions
- Epistemic coverage metrics and queries
- DrHook.Engine (sovereign C# port via dbgshim P/Invoke)
- CI/CD integration (resource cost management)
- Cross-component diagnostic sessions for v2.0 cognitive layer

---

## Naming

**DrHook** — a hook is what a debugger does to a running process. Load-bearing, not decorative.

**DrHook.Engine** — the future sovereign debug engine. Port of netcoredbg to C#, interfacing with dbgshim via P/Invoke. The engine speaks to the runtime; DrHook speaks to the engine.
