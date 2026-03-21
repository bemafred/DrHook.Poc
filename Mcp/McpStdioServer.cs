using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DrHook.Poc.Mcp;

/// <summary>
/// Minimal MCP stdio server implementing JSON-RPC 2.0 over stdin/stdout.
/// BCL only — no MCP SDK dependency. The protocol is simple enough to own directly.
/// Handles: initialize, notifications/initialized, tools/list, tools/call.
/// </summary>
public sealed class McpStdioServer(string name, string version)
{
    private readonly Dictionary<string, ToolDefinition> _tools = new();

    public void RegisterTool(string name, string description, string inputSchema, Func<ToolArgs, CancellationToken, Task<string>> handler)
    {
        _tools[name] = new ToolDefinition(name, description.Trim(), inputSchema, handler);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        // Use UTF-8 without BOM — BOM bytes before JSON-RPC responses corrupt the framing.
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var stdin = new StreamReader(Console.OpenStandardInput(), utf8NoBom);
        var stdout = new StreamWriter(Console.OpenStandardOutput(), utf8NoBom) { AutoFlush = true };
        var stderr = new StreamWriter(Console.OpenStandardError(), utf8NoBom) { AutoFlush = true };

        await stderr.WriteLineAsync($"[DrHook] MCP stdio server {name} v{version} started");
        await stderr.WriteLineAsync($"[DrHook] Tools: {string.Join(", ", _tools.Keys)}");

        while (!ct.IsCancellationRequested)
        {
            var line = await stdin.ReadLineAsync(ct);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonObject? request = null;
            try
            {
                request = JsonNode.Parse(line) as JsonObject;
                if (request is null) continue;

                var id = request["id"];
                var method = request["method"]?.GetValue<string>() ?? "";

                // Notifications (no id) — acknowledge silently
                if (id is null && method == "notifications/initialized")
                {
                    await stderr.WriteLineAsync("[DrHook] Client initialized");
                    continue;
                }

                var @params = request["params"] as JsonObject;

                var response = method switch
                {
                    "initialize"  => HandleInitialize(id),
                    "tools/list"  => HandleToolsList(id),
                    "tools/call"  => await HandleToolsCallAsync(id, @params, stderr, ct),
                    _             => HandleUnknownMethod(id, method)
                };

                if (response is not null)
                    await stdout.WriteLineAsync(response);
            }
            catch (Exception ex)
            {
                var id = request?["id"];
                var error = ErrorResponse(id, -32603, ex.Message);
                await stdout.WriteLineAsync(error);
                await stderr.WriteLineAsync($"[DrHook] Error: {ex}");
            }
        }

        await stderr.WriteLineAsync("[DrHook] Server shutting down");
    }

    private string HandleInitialize(JsonNode? id) => JsonResponse(id, new JsonObject
    {
        ["protocolVersion"] = "2024-11-05",
        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
        ["serverInfo"] = new JsonObject { ["name"] = name, ["version"] = version }
    });

    private string HandleToolsList(JsonNode? id) => JsonResponse(id, new JsonObject
    {
        ["tools"] = new JsonArray(_tools.Values.Select(t => (JsonNode)new JsonObject
        {
            ["name"] = t.Name,
            ["description"] = t.Description,
            ["inputSchema"] = JsonNode.Parse(t.InputSchema)
        }).ToArray())
    });

    private async Task<string> HandleToolsCallAsync(JsonNode? id, JsonObject? @params, StreamWriter stderr, CancellationToken ct)
    {
        var toolName = @params?["name"]?.GetValue<string>() ?? throw new InvalidOperationException("Missing tool name");
        var arguments = @params?["arguments"] as JsonObject ?? new JsonObject();

        if (!_tools.TryGetValue(toolName, out var tool))
            return ErrorResponse(id, -32602, $"Unknown tool: {toolName}");

        await stderr.WriteLineAsync($"[DrHook] Calling {toolName}");
        var result = await tool.Handler(new ToolArgs(arguments), ct);
        await stderr.WriteLineAsync($"[DrHook] {toolName} complete");

        return JsonResponse(id, new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = result
            })
        });
    }

    private static string HandleUnknownMethod(JsonNode? id, string method) =>
        ErrorResponse(id, -32601, $"Method not found: {method}");

    private static string JsonResponse(JsonNode? id, JsonObject result) =>
        JsonSerializer.Serialize(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = result
        });

    private static string ErrorResponse(JsonNode? id, int code, string message) =>
        JsonSerializer.Serialize(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        });

    private record ToolDefinition(string Name, string Description, string InputSchema, Func<ToolArgs, CancellationToken, Task<string>> Handler);
}

/// <summary>
/// Thin accessor over the JSON arguments passed to a tool call.
/// </summary>
public sealed class ToolArgs(JsonObject args)
{
    public int GetInt(string key) =>
        ParseInt(args[key]) ?? throw new ArgumentException($"Missing required argument: {key}");

    public int GetIntOrDefault(string key, int defaultValue) =>
        ParseInt(args[key]) ?? defaultValue;

    private static int? ParseInt(JsonNode? node) => node switch
    {
        null => null,
        _ when node.GetValueKind() == JsonValueKind.Number => node.GetValue<int>(),
        _ when node.GetValueKind() == JsonValueKind.String
            && int.TryParse(node.GetValue<string>(), out var parsed) => parsed,
        _ => node.GetValue<int>(),  // let it throw with the original error for unexpected types
    };

    public string GetString(string key) =>
        args[key]?.GetValue<string>() ?? throw new ArgumentException($"Missing required argument: {key}");

    public string? GetStringOrDefault(string key, string? defaultValue) =>
        args[key]?.GetValue<string>() ?? defaultValue;

    public bool GetBool(string key) =>
        ParseBool(args[key]) ?? throw new ArgumentException($"Missing required argument: {key}");

    private static bool? ParseBool(JsonNode? node) => node switch
    {
        null => null,
        _ when node.GetValueKind() == JsonValueKind.True => true,
        _ when node.GetValueKind() == JsonValueKind.False => false,
        _ when node.GetValueKind() == JsonValueKind.String
            && bool.TryParse(node.GetValue<string>(), out var parsed) => parsed,
        _ => node.GetValue<bool>(),
    };

    public bool GetBoolOrDefault(string key, bool defaultValue) =>
        ParseBool(args[key]) ?? defaultValue;
}
