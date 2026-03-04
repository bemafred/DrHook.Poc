# DrHook.Poc

**Proof of concept — .NET runtime inspection and controlled stepping via MCP stdio**

Part of [Sky Omega](https://github.com/bemafred/sky-omega) · MIT License · .NET 10 LTS

---

## Hypothesis

Can an MCP server provide both **passive observation** (EventPipe) and **controlled stepping** (DAP/netcoredbg) of a running .NET 10 process, returning structured diagnostic data to Claude Code?

If yes: the final gap in the LLM coding feedback loop is closed, and a new discipline is established.

---

## The Gap

Current AI coding stops at green tests. Two gaps remain:

**Diagnostic gap** — infinite loops and deadlocks produce no signal. Log-based debugging cannot detect what it cannot reach. DrHook fills this gap with passive runtime observation.

**Epistemic gap** — working code that passes tests has never been inspected by an AI agent. No AI coding workflow includes stepping through working code to confirm the generative model matches execution reality. DrHook fills this gap with controlled stepping.

**Compile → Test → Inspect.** The third step.

---

## Two Layers

### Observation (EventPipe / DiagnosticsClient)

Passive. Answers: "What was the process doing during this window?"

Uses the .NET runtime's native diagnostic IPC channel. Cross-platform, sovereign — no vsdbg, no proprietary tooling.

### Stepping (DAP / netcoredbg)

Controlled. Answers: "What does this code do, line by line?"

Uses netcoredbg (Samsung, MIT license) speaking the Debug Adapter Protocol. Claude Code steps through code and narrates execution in the terminal. DrHook owns the DAP client (BCL only). netcoredbg is the debug engine — and the blueprint for its own sovereign replacement ([see ADR-001](docs/adrs/ADR-001-drhook-poc-hypothesis.md)).

---

## MCP Tools

| Tool | Layer | Description |
|------|-------|-------------|
| `drhook:processes` | — | List running .NET processes |
| `drhook:snapshot` | Observation | Summarized EventPipe capture with anomaly detection |
| `drhook:step-launch` | Stepping | Attach to process, set breakpoint, halt at it |
| `drhook:step-next` | Stepping | Step over one line, return current state |
| `drhook:step-into` | Stepping | Step into method call on current line |
| `drhook:step-out` | Stepping | Step out to caller frame |
| `drhook:step-continue` | Stepping | Resume execution until next breakpoint |
| `drhook:step-pause` | Stepping | Interrupt running process immediately |
| `drhook:step-breakpoint` | Stepping | Set source line breakpoint (optional condition) |
| `drhook:step-break-function` | Stepping | Set function entry breakpoint (optional condition) |
| `drhook:step-break-exception` | Stepping | Set exception filter breakpoint (`all` / `user-unhandled`) |
| `drhook:step-vars` | Stepping | Inspect local variables at current position |
| `drhook:step-stop` | Stepping | End session, detach from process |

Every observation tool requires a **hypothesis** — state what you expect before inspecting. The delta between expectation and reality is the epistemic value.

---

## Quick Start

> See the **[Getting Started Tutorial](docs/getting-started.md)** for detailed platform-specific installation, end-to-end walkthrough, and all 13 tools.

### Prerequisites

- .NET 10 SDK
- [netcoredbg](https://github.com/Samsung/netcoredbg) installed and on PATH (for stepping layer)
  - **Apple Silicon (M1/M2/M3/M4):** must be [built from source](docs/getting-started.md#netcoredbg-platform-details) — no pre-built ARM64 binaries are published

### 1. Build and register as MCP server

```bash
dotnet build DrHook.Poc.csproj
claude mcp add drhook --transport stdio -- dotnet <clone-dir>/bin/Debug/net10.0/DrHook.Poc.dll
```

Replace `<clone-dir>` with the absolute path to your clone. Using the DLL directly skips build and project resolution, avoiding Claude Code's 30-second MCP connection timeout. Rebuild with `dotnet build` after code changes.

### 2. Start the stepping host

```bash
dotnet run --project DrHook.Poc/Host/SteppingHost.cs
# Note the PID
```

### 3. Observe (passive)

```
Use drhook:snapshot against PID <pid> for 2000ms.
My hypothesis: "The tight loop scenario will show one thread consuming >80% of samples."
What do you observe? Does it match?
```

### 4. Step (controlled)

```
Use drhook:step-launch to attach to PID <pid>.
Set a breakpoint at SteppingHost.cs line 31.
My hypothesis: "counter starts at 0 and increments in a tight loop."
Step through 5 lines and tell me what you see.
```

---

## Cross-LLM Refinements

This POC incorporates refinements from assessment by ChatGPT, Grok, Gemini, and Perplexity:

1. **Signal summarization** — structured summaries with anomaly flags, not raw traces
2. **Code version anchoring** — assembly version captured with every observation
3. **Hypothesis requirement** — forces epistemic discipline, prevents Oracle Fallacy
4. **Falsification criteria** — explicit thresholds for validating or rejecting the long hypothesis

See [ADR-001](docs/adrs/ADR-001-drhook-poc-hypothesis.md) for details.

---

## Epistemic Coverage

> Which code paths have been **observed under inspection** versus which merely pass tests?

Line coverage: was the code executed?  
Epistemic coverage: was the execution **understood**?

100% line coverage + 0% epistemic coverage = tests pass but nobody knows why.

---

## Sovereignty Stack

| Layer | Component | Sovereign? |
|-------|-----------|-----------|
| MCP protocol | McpStdioServer (BCL) | ✓ Owned |
| DAP client | DapClient (BCL) | ✓ Owned |
| Observation | EventPipe / DiagnosticsClient | ✓ Runtime native |
| Stepping | netcoredbg (MIT) | ○ Open source, vendorable |
| Runtime interface | dbgshim | ✓ Ships with .NET |

**Sovereign upgrade path:** netcoredbg is the blueprint for **DrHook.Engine** — a C# port via P/Invoke to dbgshim, following the pattern proven by Minerva.Interop.Poc. The same interop approach that works for Metal/CUDA/Accelerate works for the CLR debugging interface.

---

## Architecture

```
DrHook.Poc/
  Program.cs                        — MCP stdio entry, 13 tools registered
  Mcp/
    McpStdioServer.cs               — JSON-RPC 2.0 over stdio (BCL only)
  Diagnostics/
    ProcessAttacher.cs              — Process discovery + version capture
    StackInspector.cs               — EventPipe observation + summarization
  Stepping/
    DapClient.cs                    — DAP protocol client (BCL only)
    NetCoreDbgLocator.cs            — Cross-platform binary discovery
    SteppingSessionManager.cs       — Session lifecycle + hypothesis tracking
  Host/
    SteppingHost.cs                 — CLI inspection target (3 scenarios)
  docs/
    getting-started.md              — Full tutorial: install, build, run, register
    adrs/
      ADR-001-drhook-poc-hypothesis.md
      ADR-002-complete-dap-stepping-operations.md
    observations/                   — Empirical results as they accumulate
```

---

## The Long Hypothesis

Repeated runtime observation + persistent semantic memory (Mercury) = an LLM that builds genuine causal models of code behaviour.

Not through training. Through experience.

DrHook is the first experimental apparatus for testing this.

See [ADR-001](docs/adrs/ADR-001-drhook-poc-hypothesis.md) for the full epistemic framing, falsification criteria, and sovereign port path.
