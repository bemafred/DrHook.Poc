# Mutable State Depth Inspection — 2026-03-21

## Hypothesis

Breakpoint on line 126 halts before the first `Add`. Stepping through three `Add` calls and inspecting with `depth: 2` and `depth: 3` should show the `List<Person>` growing and reveal Person fields (Name, Age) at sufficient depth. Also validates the JSON type coercion fix for integer parameters.

## Procedure

1. **drhook:step-launch** — attached to SteppingHost PID 13784, breakpoint on line 126, user selected scenario 4
2. **drhook:step-vars** (depth 2) — inspected empty list before first Add
3. **drhook:step-next** (step 1) — stepped over `people.Add(new Person("Alice", 30))`
4. **drhook:step-vars** (depth 2) — inspected list after first Add
5. **drhook:step-next** (step 2) — stepped over Bob Add
6. **drhook:step-next** (step 3) — stepped over Carol Add
7. **drhook:step-vars** (depth 3) — inspected final list state
8. **drhook:step-stop** — session ended, 3 total steps

## Observed

### Coercion fix verified
- `depth: 2` and `depth: 3` both accepted without error (previously failed with `An element of type 'String' cannot be converted to a 'System.Int32'`)
- Fix: `ParseInt` in `McpStdioServer.cs` checks `JsonValueKind` and coerces string→int at the MCP boundary

### Mutable state tracking
- **Before first Add:** `Count: 0`, `_size: 0`, `Capacity: 0`, `_items: Person[0]`
- **After first Add:** `Count: 1`, `_size: 1`, `Capacity: 4`, `_items: Person[4]` — List allocated backing array on first Add
- **After all three Adds:** `Count: 3`, `_size: 3`, `_version: 3`, `_items[0-2]: {Person}`, `_items[3]: null`

### Noise in variable output (depth 3)
Two sources of noise obscure the signal:

1. **Indexer properties** (`Item`, `System.Collections.IList.Item`) — netcoredbg evaluates these without an index argument, producing `TargetParameterCountException` objects that expand into 25+ exception fields each at depth 3
2. **Self-referential properties** (`SyncRoot` → same `List<Person>`) — the visited-set in `ExpandVariableAsync` guards against infinite recursion via `variablesReference`, but `SyncRoot` returns the same logical object through a different DAP reference, causing redundant expansion

### Depth vs signal
- Depth 2: sees List internals (`_items`, `Count`, etc.) but Person elements show as `{Person}` without fields
- Depth 3: sees array elements `[0]`, `[1]`, `[2]` as `{Person}` but still no Name/Age — would need depth 4
- Each depth level multiplies noise from interface members and self-references

## Delta

- **Confirmed:** Coercion fix works. Mutable state is accurately tracked across step-next operations.
- **Confirmed:** step-next reliably advances one statement at a time through sequential code.
- **Refined:** Useful depth for collection inspection is deeper than expected (depth 4 for record fields inside a List). Noise grows exponentially with depth — filtering is needed before deeper inspection becomes practical.

## Coverage

| Tool | Exercised | Result |
|------|-----------|--------|
| drhook:step-launch | Yes | Attached, breakpoint hit on line 126 |
| drhook:step-next | Yes (x3) | Advanced through three Add calls |
| drhook:step-vars | Yes (x3) | depth 2 and depth 3 accepted; observed list growth |
| drhook:step-stop | Yes | Clean session teardown |

## Action items

- Filter out indexer properties that throw `TargetParameterCountException`
- Consider filtering self-referential properties (`SyncRoot`, interface reimplementations)
- Consider a smarter depth strategy that prioritizes user-defined types over BCL internals
