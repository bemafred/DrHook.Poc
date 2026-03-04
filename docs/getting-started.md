# Getting Started with DrHook.Poc

This tutorial walks you through installing prerequisites, building DrHook, registering it as an MCP server in Claude Code, and running your first observation and stepping sessions.

---

## 1. What You'll Set Up

Two components:

- **DrHook MCP server** — an MCP stdio server that provides passive runtime observation (EventPipe) and controlled stepping (DAP/netcoredbg) of running .NET 10 processes
- **SteppingHost** — a minimal CLI program with three inspection scenarios (tight loop, recursion, exception) that serves as the inspection target

Once registered, Claude Code gains 13 diagnostic tools — the "Inspect" step after Compile and Test.

---

## 2. Prerequisites

### Per-platform installation

| Tool | macOS | Windows | Linux |
|------|-------|---------|-------|
| .NET 10 SDK | `brew install dotnet-sdk` or [download](https://dotnet.microsoft.com/download) | `winget install Microsoft.DotNet.SDK.10` or [download](https://dotnet.microsoft.com/download) | `apt install dotnet-sdk-10.0` / `dnf install dotnet-sdk-10.0` or [download](https://dotnet.microsoft.com/download) |
| netcoredbg | `brew install netcoredbg` or [GitHub release](https://github.com/Samsung/netcoredbg/releases) | [GitHub release](https://github.com/Samsung/netcoredbg/releases) → extract to a directory on PATH | [GitHub release](https://github.com/Samsung/netcoredbg/releases) → extract to `/usr/local/netcoredbg/` |
| Claude Code | `npm i -g @anthropic-ai/claude-code` | `npm i -g @anthropic-ai/claude-code` | `npm i -g @anthropic-ai/claude-code` |
| Git | Pre-installed (Xcode CLT) | `winget install Git.Git` | `apt install git` / `dnf install git` |

### netcoredbg platform details

**macOS (Homebrew):**
```bash
brew install netcoredbg
# Installs to /opt/homebrew/bin/netcoredbg (Apple Silicon) or /usr/local/bin/netcoredbg (Intel)
```

**macOS (manual):**
```bash
# Download from https://github.com/Samsung/netcoredbg/releases
# Extract and move to a location on PATH:
sudo mkdir -p /usr/local/netcoredbg
sudo tar -xzf netcoredbg-osx-*.tar.gz -C /usr/local/netcoredbg
sudo ln -s /usr/local/netcoredbg/netcoredbg /usr/local/bin/netcoredbg
```

**Windows:**
```powershell
# Download from https://github.com/Samsung/netcoredbg/releases
# Extract to C:\Program Files\netcoredbg\ or C:\Tools\netcoredbg\
# Add the directory to your PATH environment variable
```

**Linux:**
```bash
# Download from https://github.com/Samsung/netcoredbg/releases
sudo mkdir -p /usr/local/netcoredbg
sudo tar -xzf netcoredbg-linux-*.tar.gz -C /usr/local/netcoredbg
sudo ln -s /usr/local/netcoredbg/netcoredbg /usr/local/bin/netcoredbg
```

### Environment variable override

If netcoredbg is installed in a non-standard location, set:

```bash
export DRHOOK_NETCOREDBG_PATH=/path/to/netcoredbg
```

DrHook searches these paths automatically (see `Stepping/NetCoreDbgLocator.cs`):

| Platform | Search paths |
|----------|-------------|
| macOS | `/usr/local/bin/netcoredbg`, `/opt/homebrew/bin/netcoredbg`, `~/.dotnet/tools/netcoredbg`, `/usr/local/netcoredbg/netcoredbg` |
| Windows | `~\.dotnet\tools\netcoredbg\netcoredbg.exe`, `C:\Program Files\netcoredbg\netcoredbg.exe`, `C:\Tools\netcoredbg\netcoredbg.exe` |
| Linux | `/usr/local/bin/netcoredbg`, `/usr/bin/netcoredbg`, `~/.dotnet/tools/netcoredbg`, `/usr/local/netcoredbg/netcoredbg` |

### Verify installation

```bash
dotnet --version       # Should show 10.x
netcoredbg --version   # Should print version info
claude --version       # Should print Claude Code version
```

---

## 3. Clone & Build

```bash
git clone https://github.com/bemafred/DrHook.Poc.git
cd DrHook.Poc
dotnet build DrHook.Poc.csproj
```

The project builds with warnings-as-errors. A clean build confirms all dependencies are resolved. There is no solution file — the single `.csproj` at the root is the build target.

---

## 4. Register as Project MCP Server

Register DrHook as a **project-scoped** MCP server in Claude Code:

```bash
claude mcp add drhook --transport stdio -- dotnet run --project /absolute/path/to/DrHook.Poc
```

Replace `/absolute/path/to/DrHook.Poc` with the actual path to your clone.

### Verify registration

```bash
claude mcp list
```

You should see `drhook` listed with transport `stdio`.

### Project vs global scope

By default, `claude mcp add` registers the server at **project scope** (stored in `.claude/mcp.json` in the project directory). This means DrHook is available only when Claude Code runs in this project.

To register globally (available in all projects):

```bash
claude mcp add --scope global drhook --transport stdio -- dotnet run --project /absolute/path/to/DrHook.Poc
```

---

## 5. Run the Inspection Target

Open a **separate terminal** and start SteppingHost:

```bash
cd DrHook.Poc
dotnet run Host/SteppingHost.cs
```

> **Note:** SteppingHost uses .NET 10 file-level execution (`dotnet run <file>.cs`). It is intentionally excluded from the main `.csproj` because it has competing top-level statements. Do not try to build it with `dotnet build`.
>
> **Debug symbols:** `dotnet run` defaults to the `Debug` configuration. Portable PDB files are emitted automatically, so netcoredbg can set source-line breakpoints without any extra flags.

Expected output:

```
╔══════════════════════════════════════════════════════╗
║  DrHook.Poc — SteppingHost                          ║
║  PID: 12345                                         ║
║  Runtime: 10.0.0                                    ║
╚══════════════════════════════════════════════════════╝

Attach DrHook now. Press Enter to begin scenarios.
```

The banner shows the PID, but you don't need to copy it manually — `drhook:processes` discovers running .NET processes and returns their PIDs (see [section 6](#6-try-it-observation-passive)). Keep this terminal open. Don't press Enter yet.

### The three scenarios

SteppingHost exercises three inspection targets:

1. **Tight loop** (line 32) — 2-second CPU spin. Observation hypothesis: thread consumes >80% of samples.
2. **Deep recursion** (line 45) — `Fibonacci(30)`. Stepping hypothesis: call stack depth reaches 30+ frames.
3. **Caught exception** (line 55) — `InvalidOperationException` thrown and caught. Exception breakpoint target.

---

## 6. Try It: Observation (Passive)

Start Claude Code in the DrHook project directory, then ask it to use the observation tools.

### Discover processes

Prompt Claude Code:

```
Use drhook:processes to list running .NET processes.
```

Expected response: a JSON array of running .NET processes with PIDs, names, and assembly versions.

### Capture a snapshot

First, go to the SteppingHost terminal and press Enter to start the scenarios. Then quickly prompt Claude Code:

```
Use drhook:snapshot against PID <pid> for 2000ms.
My hypothesis: "The tight loop scenario will show one thread consuming >80% of samples."
```

Expected response: a structured summary containing:
- `threadSummaries` — per-thread sample counts
- `hotspots` — functions with disproportionate sample share
- `anomalies` — flags for hotspot detection, GC pressure, contention
- `deltaPrompt` — the gap between your hypothesis and observed reality

---

## 7. Try It: Stepping (Controlled)

This walkthrough uses the full stepping vocabulary. Start SteppingHost fresh (restart it if needed).

### Launch a stepping session

First, discover the PID:

```
Use drhook:processes to find the SteppingHost process.
```

Then launch with the returned PID:

```
Use drhook:step-launch to attach to the SteppingHost process.
Set the breakpoint at Host/SteppingHost.cs line 32.
My hypothesis: "Execution halts before the tight loop begins."
```

Press Enter in the SteppingHost terminal after attaching. DrHook will halt at the breakpoint.

### Step over a line

```
Use drhook:step-next.
My hypothesis: "counter is initialized to 0."
```

Returns: current source location, line content, and step count.

### Step into a method

When you reach the Fibonacci call (line 45), step into it:

```
Use drhook:step-into.
My hypothesis: "We enter the Fibonacci method with n=30."
```

Returns: source location inside the Fibonacci method body.

### Inspect variables

```
Use drhook:step-vars.
```

Returns: local variable names, types, and values at the current frame.

### Step out of a method

```
Use drhook:step-out.
My hypothesis: "We return to the call site at SteppingHost.cs line 45."
```

Returns: source location at the caller frame.

### End the session

```
Use drhook:step-stop.
```

Returns: session summary including total step count and hypothesis history.

---

## 8. Try It: Breakpoints & Flow Control

Start a fresh stepping session (restart SteppingHost, use `drhook:processes` to find the PID, launch a new session).

### Set a conditional breakpoint

```
Use drhook:step-breakpoint at Host/SteppingHost.cs line 36 with condition "counter > 1000000".
```

> **Note:** DAP uses set-and-replace semantics. Setting a breakpoint in a file replaces all previous breakpoints in that file.

### Continue to breakpoint

```
Use drhook:step-continue.
My hypothesis: "The loop will run until counter exceeds 1,000,000, then halt."
```

The process runs freely until the condition is met.

### Inspect state at the breakpoint

```
Use drhook:step-vars.
```

Confirm that `counter` is greater than 1,000,000.

### Set an exception breakpoint

```
Use drhook:step-break-exception with filter "all".
```

### Continue to the exception

```
Use drhook:step-continue.
My hypothesis: "Execution halts when ThrowsOnPurpose raises InvalidOperationException."
```

### Set a function breakpoint

```
Use drhook:step-break-function for function "Fibonacci".
```

### Pause a running process

If you used `step-continue` and no breakpoint is being hit:

```
Use drhook:step-pause.
```

Immediately halts the process and returns the current source location.

---

## 9. All 13 Tools

| Tool | Layer | Description | Required params |
|------|-------|-------------|-----------------|
| `drhook:processes` | — | List running .NET processes | — |
| `drhook:snapshot` | Observation | EventPipe capture with anomaly detection | `pid`, `hypothesis` |
| `drhook:step-launch` | Stepping | Attach to process, set breakpoint, halt | `pid`, `sourceFile`, `line`, `hypothesis` |
| `drhook:step-next` | Stepping | Step over one line | — |
| `drhook:step-into` | Stepping | Step into method call on current line | — |
| `drhook:step-out` | Stepping | Step out to caller frame | — |
| `drhook:step-continue` | Stepping | Resume until next breakpoint | — |
| `drhook:step-pause` | Stepping | Interrupt running process | — |
| `drhook:step-breakpoint` | Stepping | Set source line breakpoint (optional condition) | `sourceFile`, `line` |
| `drhook:step-break-function` | Stepping | Set function entry breakpoint (optional condition) | `functionName` |
| `drhook:step-break-exception` | Stepping | Set exception filter breakpoint | `filter` |
| `drhook:step-vars` | Stepping | Inspect local variables at current position | — |
| `drhook:step-stop` | Stepping | End session, detach from process | — |

Navigation tools (`step-next`, `step-into`, `step-out`, `step-continue`) accept an optional `hypothesis` parameter. Breakpoint-setting tools and `step-pause` do not — they are control operations, not observation operations.

---

## 10. Troubleshooting

### netcoredbg not found

**Symptom:** `FileNotFoundException: netcoredbg not found`

**Fix:** Install netcoredbg (see [section 2](#2-prerequisites)) or set the override:

```bash
export DRHOOK_NETCOREDBG_PATH=/path/to/netcoredbg
```

### Permission denied on macOS

**Symptom:** macOS blocks netcoredbg from running (Gatekeeper / SIP).

**Fix:**

```bash
# Remove quarantine attribute from the downloaded binary
xattr -d com.apple.quarantine /path/to/netcoredbg
```

Or approve it in System Settings > Privacy & Security after the first blocked launch.

### "Session already active"

**Symptom:** `step-launch` returns an error about an active session.

**Fix:** Call `drhook:step-stop` to end the existing session before launching a new one.

### Process exited before attach

**Symptom:** `step-launch` fails because the target process is no longer running.

**Fix:** Start SteppingHost **before** launching a DrHook stepping session. Don't press Enter in SteppingHost until after DrHook has attached.

### SteppingHost won't build with the main project

**Symptom:** Build errors about competing top-level statements.

**Explanation:** SteppingHost is excluded from `DrHook.Poc.csproj` because both `Program.cs` and `SteppingHost.cs` use top-level statements. Run it standalone using .NET 10 file-level execution:

```bash
dotnet run Host/SteppingHost.cs
```

### No .NET processes found

**Symptom:** `drhook:processes` returns an empty list.

**Fix:** Ensure the target process is a .NET process and is currently running. SteppingHost must be started before calling `drhook:processes`.
