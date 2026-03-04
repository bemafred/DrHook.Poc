using DrHook.Poc.Diagnostics;
using DrHook.Poc.Mcp;
using DrHook.Poc.Stepping;

// DrHook.Poc — MCP stdio server
//
// Two layers:
//   Observation — EventPipe/DiagnosticsClient, passive, sovereign (BCL + official client)
//   Stepping    — DAP/netcoredbg, controlled, cross-platform (MIT, blueprint for port)
//
// Hypothesis: Can an MCP server provide both passive observation AND controlled stepping
// of a running .NET 10 process, returning structured diagnostic data to Claude Code?
//
// Cross-LLM refinements applied:
//   1. Signal summarization — structured summaries, not raw frames
//   2. Code version anchoring — assembly version captured with every observation
//   3. Hypothesis field — consumer states expectations before inspecting
//   4. Falsification criteria — tracked in ADR-001

var server = new McpStdioServer("drhook", "0.2.0");
var steppingSession = new SteppingSessionManager();

// ─── Observation layer (EventPipe) ──────────────────────────────────────────

server.RegisterTool(
    name: "drhook:processes",
    description: "List running .NET processes available for inspection.",
    inputSchema: """
    {
      "type": "object",
      "properties": {}
    }
    """,
    handler: async (args, ct) =>
    {
        var attacher = new ProcessAttacher();
        var processes = await attacher.ListDotNetProcessesAsync(ct);
        return processes.ToJson();
    });

server.RegisterTool(
    name: "drhook:snapshot",
    description: """
        Capture a summarized observation of a running .NET process via EventPipe.
        Passive — does not halt or modify execution. Returns structured summary
        with thread states, hotspots, exceptions, GC pressure, and anomaly flags.
        Requires a hypothesis: state what you expect BEFORE inspecting.
        """,
    inputSchema: """
    {
      "type": "object",
      "properties": {
        "pid": { "type": "integer", "description": "Target process ID" },
        "durationMs": { "type": "integer", "description": "Trace duration in milliseconds", "default": 2000 },
        "hypothesis": { "type": "string", "description": "What you expect to observe. Required — forces epistemic discipline." }
      },
      "required": ["pid", "hypothesis"]
    }
    """,
    handler: async (args, ct) =>
    {
        var pid = args.GetInt("pid");
        var durationMs = args.GetIntOrDefault("durationMs", 2000);
        var hypothesis = args.GetString("hypothesis");

        var inspector = new StackInspector();
        var snapshot = await inspector.CaptureAsync(pid, durationMs, hypothesis, ct);
        return snapshot.ToJson();
    });

// ─── Stepping layer (DAP / netcoredbg) ─────────────────────────────────────

server.RegisterTool(
    name: "drhook:step-launch",
    description: """
        Launch a controlled stepping session against a .NET process.
        Uses netcoredbg (MIT, DAP over stdio). Sets an initial breakpoint and runs to it.
        The process halts at the breakpoint — use drhook:step-next to advance.
        """,
    inputSchema: """
    {
      "type": "object",
      "properties": {
        "pid": { "type": "integer", "description": "Target process ID to attach to" },
        "sourceFile": { "type": "string", "description": "Source file path for the initial breakpoint" },
        "line": { "type": "integer", "description": "Line number for the initial breakpoint" },
        "hypothesis": { "type": "string", "description": "What you expect to observe at this breakpoint" }
      },
      "required": ["pid", "sourceFile", "line", "hypothesis"]
    }
    """,
    handler: async (args, ct) =>
    {
        var pid = args.GetInt("pid");
        var sourceFile = args.GetString("sourceFile");
        var line = args.GetInt("line");
        var hypothesis = args.GetString("hypothesis");

        return await steppingSession.LaunchAsync(pid, sourceFile, line, hypothesis, ct);
    });

server.RegisterTool(
    name: "drhook:step-next",
    description: """
        Step one line in the active stepping session. Returns current source location,
        local variables, and their values. This is the core inspection tool —
        Claude Code narrates execution line by line in the terminal.
        """,
    inputSchema: """
    {
      "type": "object",
      "properties": {
        "hypothesis": { "type": "string", "description": "What you expect on the next line (optional but valuable)" }
      },
      "required": []
    }
    """,
    handler: async (args, ct) =>
    {
        var hypothesis = args.GetStringOrDefault("hypothesis", null);
        return await steppingSession.StepNextAsync(hypothesis, ct);
    });

server.RegisterTool(
    name: "drhook:step-vars",
    description: "Inspect local variables at the current stepping position.",
    inputSchema: """
    {
      "type": "object",
      "properties": {
        "depth": { "type": "integer", "description": "Object inspection depth (default 1)", "default": 1 }
      },
      "required": []
    }
    """,
    handler: async (args, ct) =>
    {
        var depth = args.GetIntOrDefault("depth", 1);
        return await steppingSession.InspectVariablesAsync(depth, ct);
    });

server.RegisterTool(
    name: "drhook:step-stop",
    description: "End the active stepping session and detach from the process.",
    inputSchema: """
    {
      "type": "object",
      "properties": {}
    }
    """,
    handler: async (args, ct) =>
    {
        return await steppingSession.StopAsync(ct);
    });

await server.RunAsync(CancellationToken.None);
