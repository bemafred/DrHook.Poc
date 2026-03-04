# ADR-002: Complete DAP Stepping Operations

**Status:** Accepted
**Date:** 2026-03-04
**Author:** Martin Fredriksson
**Component:** DrHook.Poc
**Supersedes:** None (extends ADR-001)

---

## Context

ADR-001 established the stepping layer with four tools: `step-launch`, `step-next`, `step-vars`, `step-stop`. This proved the hypothesis that controlled stepping via DAP works. However, three systematic gaps limit the inspection vocabulary:

### 1. No call-depth navigation

`step-next` (step-over) skips method calls entirely. The Fibonacci scenario in SteppingHost is uninspectable at depth — step-over at `Fibonacci(n - 1)` advances past the recursive call without entering it. Without step-into and step-out, Claude Code cannot follow execution through call chains or escape deep stacks.

### 2. No dynamic breakpoints

The only breakpoint is set at launch time. Mid-session breakpoints — conditional, function-based, or exception-triggered — are impossible. This forces a restart-and-reattach cycle for each new inspection point, breaking flow and losing accumulated context.

### 3. No flow control

After stepping, there is no way to resume execution (continue) or interrupt a running process (pause). This prevents the natural debugging workflow: set breakpoint, continue, inspect, set another breakpoint, continue.

---

## Decision

Expand the stepping layer to all standard DAP stepping operations supported by netcoredbg. This adds 7 new MCP tools (from 6 total to 13):

### Navigation

| Tool | DAP command | Purpose |
|------|-------------|---------|
| `drhook:step-into` | `stepIn` | Descend into method call on current line |
| `drhook:step-out` | `stepOut` | Return to caller frame |

### Flow control

| Tool | DAP command | Purpose |
|------|-------------|---------|
| `drhook:step-continue` | `continue` | Resume until next breakpoint |
| `drhook:step-pause` | `pause` | Interrupt running process |

### Breakpoints

| Tool | DAP command | Purpose |
|------|-------------|---------|
| `drhook:step-breakpoint` | `setBreakpoints` | Source line breakpoint (optional condition) |
| `drhook:step-break-function` | `setFunctionBreakpoints` | Method entry breakpoint (optional condition) |
| `drhook:step-break-exception` | `setExceptionBreakpoints` | Exception filter breakpoint |

---

## Design Choices

### Hypothesis on observation tools only

`step-into`, `step-out`, and `step-continue` accept an optional `hypothesis` field — they are observation operations that advance execution and reveal state. `step-pause` and the three breakpoint-setting tools do NOT accept hypothesis — they are control operations that configure the session without revealing state.

This follows the principle from ADR-001: hypothesis is required where there is something to observe.

### Exception filters (netcoredbg constraint)

Exception breakpoints use DAP's `exceptionBreakpointFilters` mechanism: `"all"` (break on every thrown exception) or `"user-unhandled"` (break on exceptions not caught in user code). These are the filters netcoredbg supports.

Type-specific exception breakpoints (e.g., "break only on `NullReferenceException`") require the `exceptionOptions` capability, which is not available in netcoredbg. This is noted as a future advantage of DrHook.Engine — a sovereign debug engine could support type-specific filtering via ICorDebug directly.

### Set-and-replace breakpoint semantics

DAP's `setBreakpoints` command replaces ALL breakpoints in a given source file with the new set. Similarly, `setFunctionBreakpoints` replaces all function breakpoints. This is by DAP design, not a DrHook limitation.

The current implementation sends a single breakpoint per call, which means setting a new breakpoint in the same file removes the previous one. A multi-breakpoint registry (tracking breakpoints per file and sending the full set on each call) is deferred — it adds complexity without blocking the POC hypothesis test.

### Operation field in responses

All stepping responses now include an `["operation"]` field (`"next"`, `"stepIn"`, `"stepOut"`, `"continue"`, `"pause"`, `"setBreakpoint"`, etc.). This disambiguates responses in logs and prepares for Mercury triple storage where the operation type would be a predicate.

---

## Workflow Examples

### Recursive descent (Fibonacci)

```
step-launch  → breakpoint at Fibonacci call site
step-into    → enter Fibonacci(n)
step-into    → enter Fibonacci(n-1) — one level deeper
step-vars    → inspect n, observe recursion depth
step-out     → return to Fibonacci(n) frame
step-out     → return to original call site
```

### Exception investigation

```
step-launch         → breakpoint near suspected throw
step-break-exception → filter: "all"
step-continue        → process runs until exception thrown
step-vars            → inspect state at throw site
step-out             → see where exception propagates
```

### Conditional breakpoint

```
step-launch      → initial breakpoint in loop
step-breakpoint  → line 36, condition: "counter > 1000000"
step-continue    → runs until condition met
step-vars        → inspect counter and surrounding state
```

---

## Consequences

**Positive:**
- Full stepping vocabulary — Claude Code can navigate any call depth, set mid-session breakpoints, and control execution flow
- The Fibonacci and exception scenarios in SteppingHost become fully inspectable
- Natural debugging workflow (set → continue → inspect → repeat) is now possible
- No new dependencies — all BCL only, following sovereignty constraint

**Negative / limitations:**
- Set-and-replace breakpoint semantics require awareness from the consumer — documented in tool descriptions
- Exception filter granularity is limited to `"all"` / `"user-unhandled"` (netcoredbg constraint)
- Additional tools increase the MCP tool surface — consumers must choose the right stepping operation
- Apple Silicon requires building netcoredbg from source — no pre-built ARM64 binaries available (see [Getting Started](../getting-started.md#netcoredbg-platform-details))

**Deferred:**
- Multi-breakpoint registry (tracking and managing breakpoints per file)
- Type-specific exception breakpoints (requires DrHook.Engine or netcoredbg extension)
- `evaluate` expression support (DAP `evaluate` command)
- Reverse debugging / step-back (requires specialized runtime support)
- Hit count breakpoints (DAP `hitCondition` field)
