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

var server = new McpStdioServer("drhook", "0.3.0");
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

server.RegisterTool(
    name: "drhook:step-into",
    description: """
        Step INTO the method call on the current line. Descends into the callee.
        Use this to follow execution into a method — e.g. entering a recursive call
        or library method to see what happens inside. Contrast with drhook:step-next
        which steps OVER calls.
        """,
    inputSchema: """
    {
      "type": "object",
      "properties": {
        "hypothesis": { "type": "string", "description": "What you expect inside the method (optional but valuable)" }
      },
      "required": []
    }
    """,
    handler: async (args, ct) =>
    {
        var hypothesis = args.GetStringOrDefault("hypothesis", null);
        return await steppingSession.StepIntoAsync(hypothesis, ct);
    });

server.RegisterTool(
    name: "drhook:step-out",
    description: """
        Step OUT of the current method, returning to the caller frame.
        Use this to escape deep call stacks — e.g. after stepping into a recursive
        call, step-out returns to the frame that made the call.
        """,
    inputSchema: """
    {
      "type": "object",
      "properties": {
        "hypothesis": { "type": "string", "description": "What you expect at the return site (optional but valuable)" }
      },
      "required": []
    }
    """,
    handler: async (args, ct) =>
    {
        var hypothesis = args.GetStringOrDefault("hypothesis", null);
        return await steppingSession.StepOutAsync(hypothesis, ct);
    });

server.RegisterTool(
    name: "drhook:step-continue",
    description: """
        Resume execution until the next breakpoint is hit. The process runs freely.
        Use after setting a breakpoint with drhook:step-breakpoint or drhook:step-break-function.
        Use drhook:step-pause to interrupt if no breakpoint is hit.
        """,
    inputSchema: """
    {
      "type": "object",
      "properties": {
        "hypothesis": { "type": "string", "description": "What you expect at the next breakpoint (optional but valuable)" }
      },
      "required": []
    }
    """,
    handler: async (args, ct) =>
    {
        var hypothesis = args.GetStringOrDefault("hypothesis", null);
        return await steppingSession.ContinueAsync(hypothesis, ct);
    });

server.RegisterTool(
    name: "drhook:step-pause",
    description: """
        Pause a running process immediately. Use after drhook:step-continue when
        you need to interrupt execution — e.g. to inspect a tight loop or when
        no breakpoint was hit. Returns the current source location.
        """,
    inputSchema: """
    {
      "type": "object",
      "properties": {}
    }
    """,
    handler: async (args, ct) =>
    {
        return await steppingSession.PauseAsync(ct);
    });

server.RegisterTool(
    name: "drhook:step-breakpoint",
    description: """
        Set a source breakpoint at a specific file and line. Optionally conditional.
        WARNING: DAP uses set-and-replace semantics — this replaces ALL breakpoints
        in the specified file. Multi-breakpoint-per-file registry is deferred.
        Use drhook:step-continue to run to the breakpoint.
        """,
    inputSchema: """
    {
      "type": "object",
      "properties": {
        "sourceFile": { "type": "string", "description": "Absolute path to the source file" },
        "line": { "type": "integer", "description": "Line number for the breakpoint" },
        "condition": { "type": "string", "description": "Optional condition expression (e.g. 'counter > 1000')" }
      },
      "required": ["sourceFile", "line"]
    }
    """,
    handler: async (args, ct) =>
    {
        var sourceFile = args.GetString("sourceFile");
        var line = args.GetInt("line");
        var condition = args.GetStringOrDefault("condition", null);
        return await steppingSession.SetBreakpointAsync(sourceFile, line, condition, ct);
    });

server.RegisterTool(
    name: "drhook:step-break-function",
    description: """
        Set a function breakpoint by method name. Stops at method entry.
        Optionally conditional. WARNING: DAP uses set-and-replace semantics —
        this replaces ALL function breakpoints.
        Use drhook:step-continue to run to the breakpoint.
        """,
    inputSchema: """
    {
      "type": "object",
      "properties": {
        "functionName": { "type": "string", "description": "Fully qualified or simple method name (e.g. 'Fibonacci' or 'MyNamespace.MyClass.Fibonacci')" },
        "condition": { "type": "string", "description": "Optional condition expression" }
      },
      "required": ["functionName"]
    }
    """,
    handler: async (args, ct) =>
    {
        var functionName = args.GetString("functionName");
        var condition = args.GetStringOrDefault("condition", null);
        return await steppingSession.SetFunctionBreakpointAsync(functionName, condition, ct);
    });

server.RegisterTool(
    name: "drhook:step-break-exception",
    description: """
        Set an exception breakpoint using DAP exception filters.
        Stops execution when an exception matching the filter is thrown.
        Filters are constrained by netcoredbg: 'all' (every thrown exception)
        or 'user-unhandled' (exceptions not caught in user code).
        Type-specific exception breakpoints require DrHook.Engine (deferred).
        """,
    inputSchema: """
    {
      "type": "object",
      "properties": {
        "filter": {
          "type": "string",
          "enum": ["all", "user-unhandled"],
          "description": "Exception filter: 'all' breaks on every throw, 'user-unhandled' breaks only on exceptions not caught in user code"
        }
      },
      "required": ["filter"]
    }
    """,
    handler: async (args, ct) =>
    {
        var filter = args.GetString("filter");
        return await steppingSession.SetExceptionBreakpointAsync(filter, ct);
    });

await server.RunAsync(CancellationToken.None);
