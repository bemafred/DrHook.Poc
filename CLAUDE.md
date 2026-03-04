# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

DrHook.Poc is an MCP stdio server that provides passive runtime observation (EventPipe) and controlled stepping (DAP/netcoredbg) of running .NET 10 processes. It returns structured diagnostic data to Claude Code, closing the "Compile → Test → **Inspect**" loop.

## Build & Run

```bash
# Build (warnings are errors — no separate lint step)
dotnet build DrHook.Poc.csproj

# Register as MCP server in Claude Code
claude mcp add drhook --transport stdio -- dotnet run --project DrHook.Poc

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

## Reference

- [ADR-001](docs/adrs/ADR-001-drhook-poc-hypothesis.md) — Full architecture decision, falsification criteria, sovereign port path
- [ADR-002](docs/adrs/ADR-002-complete-dap-stepping-operations.md) — Complete DAP stepping operations (step-into, step-out, continue, pause, breakpoints, exception breakpoints)
