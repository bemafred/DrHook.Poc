# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

DrHook.Poc is an MCP stdio server that provides passive runtime observation (EventPipe) and controlled stepping (DAP/netcoredbg) of running .NET 10 processes. It returns structured diagnostic data to Claude Code, closing the "Compile → Test → **Inspect**" loop.

## Build & Run

```bash
# Build (warnings are errors — no separate lint step)
dotnet build DrHook.Poc.csproj

# Register as MCP server in Claude Code (DLL path avoids 30s timeout)
claude mcp add drhook --transport stdio -- dotnet <clone-dir>/bin/Debug/net10.0/DrHook.Poc.dll

# Run directly (speaks JSON-RPC 2.0 on stdin/stdout, logs to stderr)
dotnet run --project DrHook.Poc

# Run the inspection target host (note the printed PID)
dotnet run --project DrHook.Poc/Host/SteppingHost.cs
```

**Prerequisites:** .NET 10 SDK, `netcoredbg` on PATH (or set `DRHOOK_NETCOREDBG_PATH`). Apple Silicon Macs require building netcoredbg from source — see `docs/getting-started.md`.

No test project exists. No solution file — single `.csproj` at root.

## Architecture

Two layers, both exposed as MCP tools via a hand-rolled JSON-RPC 2.0 server:

**Observation layer** (`Diagnostics/`) — Passive. `StackInspector` attaches via `DiagnosticsClient`, captures EventPipe trace (thread samples, exceptions, GC, contention), and returns structured summaries with anomaly flags. Never returns raw traces. `ProcessAttacher` discovers .NET processes and captures assembly versions.

**Stepping layer** (`Stepping/`) — Controlled. `DapClient` spawns `netcoredbg --interpreter=vscode` and speaks DAP over stdin/stdout with `Content-Length` framing. `SteppingSessionManager` orchestrates the lifecycle: launch → attach → breakpoint → continue → step → inspect vars → stop. Tracks hypothesis and step count per session.

**MCP server** (`Mcp/McpStdioServer.cs`) — JSON-RPC 2.0 over stdio, BCL only (no MCP SDK). Handles `initialize`, `tools/list`, `tools/call`. Tool registration happens in `Program.cs`.

**Entry point** (`Program.cs`) — Registers all 13 tools and starts the server:
- Observation: `drhook:processes`, `drhook:snapshot`
- Stepping (navigation): `drhook:step-launch`, `drhook:step-next`, `drhook:step-into`, `drhook:step-out`
- Stepping (flow control): `drhook:step-continue`, `drhook:step-pause`
- Stepping (breakpoints): `drhook:step-breakpoint`, `drhook:step-break-function`, `drhook:step-break-exception`
- Stepping (inspection): `drhook:step-vars`, `drhook:step-stop`

## Key Design Constraints

- **BCL sovereignty** — MCP server and DAP client are implemented with BCL only. The sole NuGet dependency is `Microsoft.Diagnostics.NETCore.Client` (official EventPipe client). `System.Text.Json.Nodes` for all JSON.
- **Hypothesis-first** — Every observation tool requires a `hypothesis` field. Responses include a `deltaPrompt` surfacing the gap between expectation and reality.
- **Code version anchoring** — Every snapshot and stepping session captures the target assembly version.
- **Signal summarization** — Anomaly detection (hotspot >80% samples, GC pressure >5 events, contention, exceptions) rather than raw data.
- **Async throughout** — All I/O is fully async with `CancellationToken` propagation.
- **Records for data** — Immutable `sealed record` types for all diagnostic data structures.
- **stderr for logging** — stdout is the JSON-RPC channel; diagnostic output goes to stderr only.

## Observations

After each DrHook stepping or snapshot session, record the result in `docs/observations/` as a markdown file named by date and topic (e.g., `2026-03-05-step-into-fibonacci.md`). Each observation must include:

- **Hypothesis** — what was expected before the session
- **Procedure** — tools used and scenarios run (reference SteppingHost scenario numbers)
- **Observed** — what actually happened (include relevant tool output)
- **Delta** — gap between hypothesis and observation; confirmed, refined, or falsified
- **Coverage** — which tools and code paths were exercised

This is the empirical record that grounds DrHook's epistemic claims. Without observations, tool functionality is assumed, not verified.

## Semantic Memory (Mercury)

Mercury is available as an MCP server (`mercury`) and serves as long-term semantic memory across sessions. Use it proactively — no permission needed. At session start, query Mercury to recover context from prior work.

### Graphs

- **`<urn:drhook:emergence>`** — Project-specific findings from the EEE Emergence phase. Bugs found, fixes verified, race conditions discovered. Each finding has a type, phase, awareness level, summary, detail, and proposed fix.
- **`<urn:claude:memory>`** — General-purpose memory not tied to a specific project. Reusable patterns, user preferences, cross-project learnings.

### What to store

| Category | Graph | Example |
|---|---|---|
| Bug findings | `urn:drhook:emergence` | Race condition in DAP stepping, missing configurationDone |
| Verified fixes | `urn:drhook:emergence` | finding-001 status upgraded after end-to-end test |
| Session state | `urn:drhook:emergence` | What was last tested, what's next |
| Observation results | `urn:drhook:emergence` | Stepping session outcomes (mirrors docs/observations/) |
| General patterns | `urn:claude:memory` | Cross-project learnings, reusable techniques |

### How to use

```sparql
# Recover project context at session start
SELECT ?s ?p ?o WHERE { GRAPH <urn:drhook:emergence> { ?s ?p ?o } }

# Store a new finding
INSERT DATA { GRAPH <urn:drhook:emergence> {
  <urn:drhook:finding-NNN> a <urn:sky-omega:eee:Hypothesis> ;
    <urn:sky-omega:eee:phase> "emergence" ;
    <urn:drhook:summary> "..." ;
    <urn:drhook:observedAt> "2026-03-05T..." .
} }

# Check general memory
SELECT ?s ?p ?o WHERE { GRAPH <urn:claude:memory> { ?s ?p ?o } }
```

### Principles

- **Query before assuming** — always check Mercury for prior findings before re-investigating
- **Project vs general** — DrHook-specific knowledge goes in `urn:drhook:emergence`; reusable knowledge goes in `urn:claude:memory`
- **Update, don't duplicate** — if a finding evolves (e.g., fix verified), update the existing triple rather than creating a new one
- **Grounding labels** — use `urn:sky-omega:eee:awareness` values: `unknown-unknown`, `known-unknown`, `unknown-known`, `known-known`
- **Timestamps** — every finding gets `urn:drhook:observedAt` with an ISO 8601 datetime

## Reference

- [ADR-001](docs/adrs/ADR-001-drhook-poc-hypothesis.md) — Full architecture decision, falsification criteria, sovereign port path
- [ADR-002](docs/adrs/ADR-002-complete-dap-stepping-operations.md) — Complete DAP stepping operations (step-into, step-out, continue, pause, breakpoints, exception breakpoints)
- [Observations](docs/observations/) — Empirical results from stepping and snapshot sessions
