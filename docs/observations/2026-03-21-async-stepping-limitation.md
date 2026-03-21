# Async Stepping Limitation — 2026-03-21

## Hypothesis

Step-next across `await Task.Delay(100)` inside `SlowCountAsync` should land on line 150 (`counter += i + 1`). The call stack should change as the async state machine suspends and resumes via thread pool continuation.

## Procedure

1. **drhook:step-launch** — attached to SteppingHost PID 13784, breakpoint on line 140, user selected scenario 5
2. **drhook:step-into** (step 1) — entered `SlowCountAsync` at line 145
3. **drhook:step-next** (steps 2-6) — advanced through: counter init (146), for loop entry (147 col 10), for condition (147 col 21), loop body brace (148), await line (149)
4. **drhook:step-vars** — confirmed `steps=3`, `counter=0` at step 2
5. **drhook:step-next** (step 7) — attempted to step over `await Task.Delay(100)`
6. **drhook:step-out** (step 8) — attempted to escape native frame → failed
7. **drhook:step-next** — attempted again → failed
8. **drhook:step-continue** (step 9) — resumed execution, scenario completed
9. **drhook:step-stop** — session ended, 9 total steps

## Observed

### Successful async entry (steps 1-6)
- Step-into correctly entered `SlowCountAsync` through the async machinery
- Call stack showed 11 frames including `AsyncMethodBuilderCore.Start` and two nested `MoveNext()` state machines (`RunAsyncWithMutationAsync` and `SlowCountAsync`)
- Variables correctly reported `steps=3`, `counter=0`
- For loop stepping worked at sub-statement granularity (initializer, condition, body)

### Await stepping failure (step 7)
Step-next on `await Task.Delay(100)` did NOT land on line 150. Instead, the debugger followed the synchronous blocking path:

```
Monitor.Wait()
  ← ManualResetEventSlim.Wait()
    ← Task.SpinThenBlockingWait()
      ← Task.InternalWait()
        ← TaskAwaiter.HandleNonSuccessAndDebuggerNotification()
```

**Root cause:** In file-based programs with top-level `await`, the main thread synchronously blocks on the async state machine via `Task.InternalWait()`. The debugger's step-next follows the main thread into native `Monitor.Wait()` rather than tracking the async continuation.

### Native frame trap (steps 8-9)
Once inside `Monitor.Wait()`:
- **step-out** → `0x80004005` (can't step out of native frame)
- **step-next** → `0x80004005` (can't step through native code)
- **step-continue** → succeeded, but the scenario completed without hitting any breakpoint

The debugger becomes trapped in a native blocking call with no step-based escape. Only continue works.

## Delta

- **Confirmed:** Entering async methods via step-into works correctly, including the async state machine call stack
- **Confirmed:** Variables and sub-statement stepping work inside async methods before an await point
- **Falsified:** step-next does NOT step over `await` in file-based programs — it follows the synchronous blocking path instead of the async continuation
- **Discovered:** Native frame trap — once the debugger enters `Monitor.Wait()`, step-next and step-out both fail, only continue escapes

## Possible mitigations

1. **Pre-set breakpoint after await:** Before stepping over an await, set a breakpoint on the next line (e.g., line 150), then use continue instead of step-next. The continuation thread will hit the breakpoint.
2. **Console application vs file-based:** A standard `async Task Main()` entry point may behave differently — the async infrastructure might not block the main thread the same way.
3. **Document the limitation:** Async stepping requires a breakpoint-and-continue strategy rather than step-next across await points.

## Coverage

| Tool | Exercised | Result |
|------|-----------|--------|
| drhook:step-launch | Yes | Attached, breakpoint hit on line 140 |
| drhook:step-into | Yes | Entered async method through state machine |
| drhook:step-next | Yes (x6) | Worked in user code, failed across await and in native frame |
| drhook:step-out | Yes | Failed in native frame (0x80004005) |
| drhook:step-continue | Yes | Escaped native frame trap |
| drhook:step-vars | Yes | Correct locals inside async method |
| drhook:step-stop | Yes | Clean teardown |
