// DrHook.Poc — SteppingHost
//
// A minimal CLI host designed as an inspection target for DrHook.
// Run this, note the PID, then use DrHook to observe and step through it.
//
// Three scenarios exercising the primary bug categories:
//   1. Tight computation loop — the infinite loop analogue, bounded
//   2. Recursive call stack — stack depth inspection
//   3. Caught exception — exception event observation
//
// For controlled stepping (DAP layer):
//   Set a breakpoint on any line below using drhook:step-launch.
//   Step through with drhook:step-next, inspecting state at each line.
//   This is epistemic coverage in practice: confirming your generative model
//   of what code does matches execution reality.

using System.Diagnostics;

Console.WriteLine("╔══════════════════════════════════════════════════════╗");
Console.WriteLine("║  DrHook.Poc — SteppingHost                           ║");
Console.WriteLine($"║  PID: {Environment.ProcessId,-46} ║");
Console.WriteLine($"║  Runtime: {Environment.Version,-42} ║");
Console.WriteLine("╚══════════════════════════════════════════════════════╝");
Console.WriteLine();
Console.WriteLine("Attach DrHook now. Press Enter to begin scenarios.");
Console.ReadLine();

// ── Scenario 1: Tight loop ──────────────────────────────────────────────
// BREAKPOINT TARGET: line below. Hypothesis: "counter increments rapidly,
// thread consumes >80% of samples in a 2s EventPipe window."
Console.WriteLine("[Scenario 1] Tight loop — 2 second CPU spin");
var sw = Stopwatch.StartNew();                // ← breakpoint here for stepping
long counter = 0;
while (sw.Elapsed.TotalSeconds < 2)
{
    counter++;
}
Console.WriteLine($"[Scenario 1] Complete. Iterations: {counter:N0}");
Console.WriteLine();

// ── Scenario 2: Deep recursion ──────────────────────────────────────────
// BREAKPOINT TARGET: Fibonacci method. Hypothesis: "call stack depth reaches
// 30+ frames during recursive descent."
Console.WriteLine("[Scenario 2] Recursive computation — Fibonacci(30)");
var result = Fibonacci(30);                   // ← breakpoint here for stepping
Console.WriteLine($"[Scenario 2] Complete. Fibonacci(30) = {result}");
Console.WriteLine();

// ── Scenario 3: Exception ───────────────────────────────────────────────
// BREAKPOINT TARGET: ThrowsOnPurpose. Hypothesis: "ExceptionStart event
// captured with type InvalidOperationException."
Console.WriteLine("[Scenario 3] Caught exception");
try
{
    ThrowsOnPurpose();                        // ← breakpoint here for stepping
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"[Scenario 3] Caught: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("[SteppingHost] All scenarios complete.");

// ── Methods ─────────────────────────────────────────────────────────────

static long Fibonacci(int n) => n <= 1 ? n : Fibonacci(n - 1) + Fibonacci(n - 2);

static void ThrowsOnPurpose() =>
    throw new InvalidOperationException("DrHook epistemic validation: exception deliberately raised");
