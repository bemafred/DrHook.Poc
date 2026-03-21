# Async Stepping Fixed — 2026-03-21

## Hypothesis

With two fixes (UpdateActiveThread from stopped events, ContinueAsync waits for breakpoint), setting a breakpoint after an await and using continue-to-breakpoint should reliably step through async code across await points.

## Procedure

1. **drhook:step-launch** — attached to SteppingHost PID 13784, breakpoint on line 150 (`counter += i + 1`, after `await Task.Delay(100)`), user selected scenario 5
2. **drhook:step-vars** — confirmed `i=0, counter=0` (first iteration)
3. **drhook:step-next** (step 1) — executed `counter += i + 1`, advanced to line 151
4. **drhook:step-continue** — waited for breakpoint, hit line 150 again
5. **drhook:step-vars** — confirmed `i=1, counter=1` (second iteration)
6. **drhook:step-continue** — waited for breakpoint, hit line 150 again
7. **drhook:step-vars** — confirmed `i=2, counter=3` (third/final iteration)
8. **drhook:step-stop** — session ended

## Observed

### Breakpoint on continuation thread works
Every breakpoint hit landed on the async continuation thread with the correct call stack:
```
SlowCountAsync.MoveNext()          ← user code
  ExecutionContext.RunInternal()
    AsyncStateMachineBox.MoveNext()
      AwaitTaskContinuation.RunOrScheduleAction()
        Task.RunContinuations()
```

This is the thread pool continuation path — fundamentally different from the blocked main thread (`Monitor.Wait()`) seen in the previous session.

### State mutation across await points
| Iteration | i | counter (before) | counter (after) | Prediction |
|-----------|---|-------------------|-----------------|------------|
| 1st       | 0 | 0                 | 1               | Correct    |
| 2nd       | 1 | 1                 | 3               | Correct    |
| 3rd       | 2 | 3                 | (not executed)  | Correct    |

### Continue-to-breakpoint is reliable
`ContinueAsync` now waits for the stopped event, updates the active thread, and returns `currentState`. Two successive continues across await points both worked perfectly — each returning the correct location and allowing immediate variable inspection.

## Delta

- **Confirmed:** Both fixes (UpdateActiveThread + ContinueAsync waits) together solve async stepping
- **Confirmed:** Breakpoint-after-await strategy works — no need to step-next across await points
- **Confirmed:** Variable inspection works on thread pool continuation threads
- **Upgraded:** The async stepping limitation from the previous observation is now resolved. Async stepping is fully functional via the continue-to-breakpoint pattern.

## Fixes applied

1. **UpdateActiveThread** — extracts `threadId` from every DAP stopped event and updates `_activeThreadId`. Applied to all step methods (next, into, out), continue, pause, and launch.
2. **ContinueAsync waits for stopped** — now calls `WaitForStoppedAsync` + `UpdateActiveThread` + `GetCurrentStateAsync`, returning the current state instead of just "running". The CancellationToken is the timeout escape if no breakpoint is hit.

## Coverage

| Tool | Exercised | Result |
|------|-----------|--------|
| drhook:step-launch | Yes | Breakpoint hit on continuation thread |
| drhook:step-next | Yes | Mutation executed correctly |
| drhook:step-continue | Yes (x2) | Waited for breakpoint across await, returned state |
| drhook:step-vars | Yes (x3) | Correct locals at each iteration |
| drhook:step-stop | Yes | Clean teardown |
