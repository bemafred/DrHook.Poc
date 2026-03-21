# Noise Filter Verification — 2026-03-21

## Hypothesis

After adding `IsNoiseVariable` filtering (TargetParameterCountException, SyncRoot, interface reimplementations, Static members), the depth 3 and depth 4 output for `List<Person>` should be clean enough to read the actual Person fields without wading through BCL noise.

## Procedure

1. **drhook:step-launch** — attached to SteppingHost PID 13784 (same process, reconnected DrHook MCP with new build), breakpoint on line 126
2. **drhook:step-next** (x3) — stepped through all three Add calls (Alice, Bob, Carol)
3. **drhook:step-vars** (depth 3) — inspected list structure
4. **drhook:step-vars** (depth 4) — inspected Person record fields
5. **drhook:step-stop** — session ended

## Observed

### Depth 3 — before filtering (previous session)
~80 lines of JSON including:
- `TargetParameterCountException` objects (25+ fields each, x2)
- `SyncRoot` expanding into full copy of list internals
- 8 interface reimplementation properties (`IList.IsReadOnly`, `ICollection.IsSynchronized`, etc.)
- `Static members` with `s_emptyArray`

### Depth 3 — after filtering
5 children: `_items` (with 4 array elements), `_size: 3`, `_version: 3`, `Capacity: 4`, `Count: 3`. Clean and readable.

### Depth 4 — Person fields visible
Each Person element expanded to show:
- `Name: "Alice"` / `"Bob"` / `"Carol"`
- `Age: 30` / `25` / `42`
- `EqualityContract: {System.RuntimeType}` (record-synthesized, minor noise)

`[3]` correctly shows `null` (unused backing array slot).

## Delta

- **Confirmed:** Noise filter eliminates the three major noise sources without losing any diagnostic signal
- **Confirmed:** Depth 4 is now sufficient to inspect record fields inside a `List<T>` — previously impractical due to noise volume
- **Minor refinement:** `EqualityContract` (compiler-synthesized for records) could be added to the filter, but its presence is small and occasionally informative

## Coverage

| Tool | Exercised | Result |
|------|-----------|--------|
| drhook:step-launch | Yes | Attached with updated DrHook build |
| drhook:step-next | Yes (x3) | Stepped through sequential mutations |
| drhook:step-vars | Yes (x2) | depth 3 and depth 4 both clean |
| drhook:step-stop | Yes | Clean teardown |

## Filtering rules applied

| Filter | What it removes | Why |
|--------|----------------|-----|
| `type == TargetParameterCountException` | Failed indexer property evaluations | netcoredbg can't evaluate indexed properties without arguments |
| `name.StartsWith("System.Collections.")` | Interface reimplementations | Duplicates of primary members (`IsReadOnly`, `IsSynchronized`, etc.) |
| `name == "SyncRoot"` | Self-referential BCL property | Expands into redundant copy of the parent object |
| `name == "Static members"` | Type-level static fields | Rarely useful for runtime instance inspection |
