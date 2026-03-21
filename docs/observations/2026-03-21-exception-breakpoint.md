# Exception Breakpoint — 2026-03-21

## Hypothesis

Setting an exception breakpoint with filter `all` will cause the debugger to break when `ThrowsOnPurpose()` throws `InvalidOperationException`, even though it's caught. The `$exception` DAP pseudo-variable should expose the exception details. After the break, stepping should reach the `catch` body.

## Procedure

1. **drhook:step-launch** — attached to SteppingHost PID 13784, breakpoint on line 108 (`ThrowsOnPurpose()` call), user selected scenario 3
2. **drhook:step-break-exception** — set exception breakpoint with filter `all`
3. **drhook:step-into** (step 1) — entered `ThrowsOnPurpose` at line 117
4. **drhook:step-next** (step 2) — throw executed, exception breakpoint fired, stopped at line 117 again
5. **drhook:step-vars** (depth 2) — inspected `$exception` pseudo-variable
6. **drhook:step-next** (step 3) — landed at line 110 (`catch` clause), call stack depth 7
7. **drhook:step-next** (step 4) — advanced to line 111 (catch body)
8. **drhook:step-vars** (depth 1) — both `$exception` and `ex` visible
9. **drhook:step-stop** — session ended, 4 total steps

## Observed

### Exception breakpoint fires on caught exceptions
The `all` filter breaks on the `throw` statement even though the exception is caught by the `try/catch` in `RunException`. This is the intended DAP behavior — `all` means all thrown exceptions regardless of handling.

### $exception pseudo-variable
DAP exposes the thrown exception as `$exception` in the Locals scope. At depth 2, the key fields:
- `_message`: "DrHook epistemic validation: exception deliberately raised"
- `HasBeenThrown`: `true`
- `Source`: "SteppingHost"
- `StackTrace`: points to line 117 in `ThrowsOnPurpose`
- `HResult`: `-2146233079` (COR_E_INVALIDOPERATION)

### Exception unwinding sequence
After the exception breakpoint fires at line 117:
1. step-next → line 110 (`catch (InvalidOperationException ex)`) — debugger stops at the catch clause itself
2. step-next → line 111 (catch body) — now inside the catch block
3. `ThrowsOnPurpose` remains on the call stack during unwinding (depth 7 vs 5 at entry)

### Both exception variables visible in catch
At step 4 (inside catch body), both are visible:
- `$exception` — DAP pseudo-variable, always present after an exception breakpoint
- `ex` — the user-declared catch variable

## Delta

- **Confirmed:** Exception breakpoint with `all` filter works correctly on caught exceptions
- **Confirmed:** `$exception` provides full exception details including message, HResult, stack trace
- **Refined:** After an exception breakpoint fires, step-next goes to the `catch` clause (line 110), not the catch body (line 111). Two steps needed to reach the handler code.
- **Refined:** During exception unwinding, the throwing method remains on the call stack (depth increases)

## Coverage

| Tool | Exercised | Result |
|------|-----------|--------|
| drhook:step-launch | Yes | Attached, breakpoint hit on line 108 |
| drhook:step-break-exception | Yes | `all` filter set, fired on caught exception |
| drhook:step-into | Yes | Entered ThrowsOnPurpose |
| drhook:step-next | Yes (x3) | Navigated throw → catch clause → catch body |
| drhook:step-vars | Yes (x2) | $exception at depth 2, both variables at depth 1 |
| drhook:step-stop | Yes | Clean teardown |
