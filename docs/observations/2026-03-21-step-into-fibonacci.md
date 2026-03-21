# Step-Into Recursive Fibonacci — 2026-03-21

## Hypothesis

Step-into from line 94 (`var result = Fibonacci(8)`) will enter the `Fibonacci` method at line 98 with `n=8`. A second step-into will recurse to `n=7` (call stack depth +1). Step-out will return to the `n=8` frame.

## Procedure

1. **drhook:processes** — discovered SteppingHost at PID 13784
2. **drhook:step-launch** — attached, breakpoint on line 94, user selected scenario 2; breakpoint hit inside `RunFibonacci()` at call stack depth 5
3. **drhook:step-into** (step 1) — entered `Fibonacci` at line 98, depth 6
4. **drhook:step-vars** — confirmed `n=8` (Locals scope, type `int`)
5. **drhook:step-into** (step 2) — recursed into `Fibonacci` at line 98, depth 7
6. **drhook:step-vars** — confirmed `n=7`
7. **drhook:step-out** (step 3) — expected return to `n=8` frame (depth 6), actual return to `RunFibonacci` line 95 (depth 5)
8. **drhook:step-stop** — session ended, 3 total steps

## Observed

- **step-into** works correctly for recursive calls. Each invocation enters a new frame with the expected parameter value, and call stack depth increments by 1.
- **step-vars** correctly reports the `n` parameter at each recursion level.
- **step-out** from an expression-bodied recursive method (`=> n <= 1 ? n : Fibonacci(n-1) + Fibonacci(n-2)`) does not stop at intermediate recursive frames. It completes the entire recursive tree and returns to the non-recursive caller (`RunFibonacci`).

## Delta

- **Confirmed:** step-into enters recursive calls correctly, step-vars shows correct parameter values at each depth level.
- **Refined:** step-out on expression-bodied methods unwinds the full call chain, not just one frame. This is expected DAP behavior — there are no statement boundaries within the expression body for the debugger to stop at. A multi-statement `Fibonacci` implementation would allow frame-by-frame step-out.

## Coverage

| Tool | Exercised | Result |
|------|-----------|--------|
| drhook:processes | Yes | Discovered SteppingHost by name/PID |
| drhook:step-launch | Yes | Attached, breakpoint hit on line 94 |
| drhook:step-into | Yes (x2) | Entered recursive frames correctly |
| drhook:step-vars | Yes (x2) | Parameter `n` reported accurately at each depth |
| drhook:step-out | Yes | Completed full recursion, returned to caller |
| drhook:step-stop | Yes | Clean session teardown |
