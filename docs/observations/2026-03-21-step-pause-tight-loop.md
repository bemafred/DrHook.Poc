# Step-Pause Tight Loop — 2026-03-21

## Hypothesis

Step-pause can interrupt a tight CPU loop mid-execution. After pausing, set a breakpoint inside the loop and continue to reach inspectable user code with a large counter value.

## Procedure

1. **drhook:step-launch** — attached to SteppingHost PID 19969, breakpoint on line 83 (counter++), user selected scenario 1
2. **drhook:step-breakpoint** — moved breakpoint to line 1 (clearing loop breakpoint)
3. **drhook:step-continue** (waitForBreakpoint=false) — returned immediately, loop spinning freely
4. **drhook:step-pause** — interrupted the process
5. Landed in `PollGCWorker` runtime frame — step-next and step-out both failed (0x80004005)
6. **drhook:step-breakpoint** — set breakpoint on line 83 while paused
7. **drhook:step-continue** (waitForBreakpoint=true) — hit line 83
8. **drhook:step-vars** — `counter = 137,116,986`
9. **drhook:step-stop** — session ended

## Observed

### Step-pause works but lands in runtime frame
DAP pause interrupts the process at a GC safe point, not at a user code line. The stopped location is `System.Threading.Thread.<PollGC>g__PollGCWorker|67_0()` — .NET's GC polling mechanism that tight loops hit periodically. From this frame:
- **step-next** → `0x80004005` (native frame)
- **step-out** → `0x80004005` (native frame)

This is the same native frame trap seen with `Monitor.Wait()` in async stepping.

### Breakpoint escape pattern
Setting a breakpoint while paused and continuing to it reliably escapes the native frame:
1. `step-breakpoint` on line 83 (inside the loop) — verified even while process is paused
2. `step-continue` (waitForBreakpoint=true) — hits on next loop iteration
3. Variables now inspectable: `counter = 137,116,986` (137M iterations)

### Timing constraints
- 2-second loop: too short — MCP round-trip latency exceeds the loop duration
- 10-second loop: still too short — three sequential MCP calls (clear breakpoint, continue, pause) exceeded 10 seconds
- 30-second loop: sufficient — pause arrived with margin to spare

### waitForBreakpoint parameter
The `waitForBreakpoint` parameter on `step-continue` enables two distinct patterns:
- `waitForBreakpoint: true` — blocks until a breakpoint is hit (breakpoint-and-continue pattern)
- `waitForBreakpoint: false` — returns immediately (continue-then-pause pattern)

## Delta

- **Confirmed:** step-pause interrupts a tight loop mid-execution
- **Confirmed:** The breakpoint escape pattern (set breakpoint while paused → continue) works reliably
- **Refined:** Pause lands in GC polling frames, not user code — this is inherent to .NET's tight loop implementation. User code inspection requires the breakpoint escape.
- **Refined:** MCP tool call round-trip latency constrains the minimum pause window to ~15-20 seconds for three sequential calls

## Coverage

| Tool | Exercised | Result |
|------|-----------|--------|
| drhook:step-launch | Yes | Attached, breakpoint hit inside loop |
| drhook:step-breakpoint | Yes (x2) | Clear breakpoint + set while paused |
| drhook:step-continue | Yes (x2) | waitForBreakpoint false (immediate) and true (wait) |
| drhook:step-pause | Yes | Interrupted tight loop at GC safe point |
| drhook:step-vars | Yes | counter = 137,116,986 after loop spin |
| drhook:step-stop | Yes | Clean teardown |
