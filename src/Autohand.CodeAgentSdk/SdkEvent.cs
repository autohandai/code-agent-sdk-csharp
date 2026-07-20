namespace Autohand.CodeAgentSdk;

public abstract record SdkEvent(string Type, JsonElement Raw)
{
    /// <summary>The CLI-provided event timestamp, or null for legacy/malformed payloads.</summary>
    public string? Timestamp =>
        Raw.ValueKind == JsonValueKind.Object &&
        Raw.TryGetProperty("timestamp", out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

public sealed record AgentStartEvent(JsonElement Raw) : SdkEvent("agent_start", Raw);

public sealed record AgentEndEvent(JsonElement Raw) : SdkEvent("agent_end", Raw);

public sealed record TurnStartEvent(JsonElement Raw) : SdkEvent("turn_start", Raw);

public sealed record TurnEndEvent(
    long? TokensUsed,
    string? TokensUsageStatus,
    long? DurationMs,
    double? ContextPercent,
    JsonElement Raw)
    : SdkEvent("turn_end", Raw);

public sealed record MessageStartEvent(JsonElement Raw) : SdkEvent("message_start", Raw);

public sealed record MessageUpdateEvent(string? Delta, JsonElement Raw)
    : SdkEvent("message_update", Raw);

public sealed record MessageEndEvent(string? Content, JsonElement Raw)
    : SdkEvent("message_end", Raw);

public sealed record ToolStartEvent(string? ToolName, string? Description, JsonElement Raw)
    : SdkEvent("tool_start", Raw);

public sealed record ToolUpdateEvent(string? ToolName, string? Output, JsonElement Raw)
    : SdkEvent("tool_update", Raw);

public sealed record ToolEndEvent(string? ToolName, bool? Success, JsonElement Raw)
    : SdkEvent("tool_end", Raw);

public sealed record PermissionRequestEvent(
    string? RequestId,
    string? Tool,
    string? Description,
    JsonElement Raw)
    : SdkEvent("permission_request", Raw);

public sealed record ErrorEvent(string? Message, JsonElement Raw) : SdkEvent("error", Raw);

public sealed record AutoresearchEvent(
    string? Phase,
    string? Operation,
    bool? Success,
    string? AttemptId,
    bool? Applied,
    JsonElement Raw)
    : SdkEvent("autoresearch", Raw);

public sealed record AutoModeIterationEvent(
    string SessionId,
    int Iteration,
    IReadOnlyList<string> Actions,
    long? TokensUsed,
    JsonElement Raw)
    : SdkEvent("automode_iteration", Raw);

public sealed record AutoModeCompleteEvent(
    string SessionId,
    int Iterations,
    int FilesCreated,
    int FilesModified,
    JsonElement Raw)
    : SdkEvent("automode_complete", Raw);

public sealed record AutoModeErrorEvent(string SessionId, string Error, JsonElement Raw)
    : SdkEvent("automode_error", Raw);

public sealed record HookPreToolEvent(
    string ToolId,
    string ToolName,
    IReadOnlyDictionary<string, JsonElement> Args,
    JsonElement Raw)
    : SdkEvent("hook_pre_tool", Raw);

public sealed record HookPostToolEvent(
    string ToolId,
    string ToolName,
    bool Success,
    double Duration,
    string? Output,
    JsonElement Raw)
    : SdkEvent("hook_post_tool", Raw);

public sealed record HookPrePromptEvent(
    string Instruction,
    IReadOnlyList<string> MentionedFiles,
    JsonElement Raw)
    : SdkEvent("hook_pre_prompt", Raw);

public enum TokenUsageStatus
{
    Actual,
    Unavailable,
}

public sealed record HookPostResponseEvent(
    long TokensUsed,
    TokenUsageStatus? TokensUsageStatus,
    int ToolCallsCount,
    double Duration,
    JsonElement Raw)
    : SdkEvent("hook_post_response", Raw);

public sealed record McpInvocationRequestEvent(
    string RequestId,
    string ToolName,
    IReadOnlyDictionary<string, JsonElement> Args,
    JsonElement Raw)
    : SdkEvent("mcp_invocation_request", Raw);

public sealed record McpToolsChangedEvent(
    IReadOnlyList<McpToolSummary> Tools,
    JsonElement Raw)
    : SdkEvent("mcp_tools_changed", Raw);

public enum LearnProgressStatus
{
    Analyzing,
    LoadingRegistry,
    Evaluating,
    Generating,
    Updating,
}

public sealed record LearnProgressEvent(LearnProgressStatus Status, JsonElement Raw)
    : SdkEvent("learn_progress", Raw);

public sealed record UnknownEvent(string EventType, JsonElement Raw) : SdkEvent(EventType, Raw);

internal static class SdkEventParser
{
    public static SdkEvent Parse(string method, JsonElement parameters)
    {
        var raw = parameters.ValueKind == JsonValueKind.Undefined
            ? default
            : parameters.Clone();
        var methodType = MethodToType(method);
        var type = IsStrictFeatureType(methodType) ? methodType : GetString(raw, "type") ?? methodType;

        return type switch
        {
            "agent_start" => new AgentStartEvent(raw),
            "agent_end" => new AgentEndEvent(raw),
            "turn_start" => new TurnStartEvent(raw),
            "turn_end" => new TurnEndEvent(
                GetLong(raw, "tokensUsed"),
                GetString(raw, "tokensUsageStatus"),
                GetLong(raw, "durationMs"),
                GetDouble(raw, "contextPercent"),
                raw),
            "message_start" => new MessageStartEvent(raw),
            "message_update" => new MessageUpdateEvent(GetString(raw, "delta"), raw),
            "message_end" => new MessageEndEvent(GetString(raw, "content"), raw),
            "tool_start" => new ToolStartEvent(
                GetString(raw, "toolName") ?? GetString(raw, "tool_name"),
                GetString(raw, "description"),
                raw),
            "tool_update" => new ToolUpdateEvent(
                GetString(raw, "toolName") ?? GetString(raw, "tool_name"),
                GetString(raw, "output") ?? GetString(raw, "delta"),
                raw),
            "tool_end" => new ToolEndEvent(
                GetString(raw, "toolName") ?? GetString(raw, "tool_name"),
                GetBool(raw, "success"),
                raw),
            "permission_request" => new PermissionRequestEvent(
                GetString(raw, "requestId") ?? GetString(raw, "request_id"),
                GetString(raw, "tool"),
                GetString(raw, "description"),
                raw),
            "error" => new ErrorEvent(GetString(raw, "message") ?? GetString(raw, "error"), raw),
            "autoresearch" => new AutoresearchEvent(
                GetString(raw, "phase") ?? AutoresearchPhase(method),
                GetString(raw, "operation"),
                GetBool(raw, "success"),
                GetString(raw, "attemptId"),
                GetBool(raw, "applied"),
                raw),
            "automode_iteration" when IsValidAutoModeIteration(raw) => new AutoModeIterationEvent(
                GetString(raw, "sessionId")!,
                GetInt(raw, "iteration")!.Value,
                GetStringList(raw, "actions"),
                GetLong(raw, "tokensUsed"),
                raw),
            "automode_iteration" => new UnknownEvent(method, raw),
            "automode_complete" when IsValidAutoModeComplete(raw) => new AutoModeCompleteEvent(
                GetString(raw, "sessionId")!,
                GetInt(raw, "iterations")!.Value,
                GetInt(raw, "filesCreated")!.Value,
                GetInt(raw, "filesModified")!.Value,
                raw),
            "automode_complete" => new UnknownEvent(method, raw),
            "automode_error" when IsValidAutoModeError(raw) => new AutoModeErrorEvent(
                GetString(raw, "sessionId")!,
                GetString(raw, "error")!,
                raw),
            "automode_error" => new UnknownEvent(method, raw),
            "hook_pre_tool" when IsValidHookPreTool(raw) => new HookPreToolEvent(
                GetString(raw, "toolId")!,
                GetString(raw, "toolName")!,
                GetObjectDictionary(raw, "args"),
                raw),
            "hook_pre_tool" => new UnknownEvent(method, raw),
            "hook_post_tool" when IsValidHookPostTool(raw) => new HookPostToolEvent(
                GetString(raw, "toolId")!,
                GetString(raw, "toolName")!,
                GetBool(raw, "success")!.Value,
                GetDouble(raw, "duration")!.Value,
                GetString(raw, "output"),
                raw),
            "hook_post_tool" => new UnknownEvent(method, raw),
            "hook_pre_prompt" when IsValidHookPrePrompt(raw) => new HookPrePromptEvent(
                GetString(raw, "instruction")!,
                GetStringList(raw, "mentionedFiles"),
                raw),
            "hook_pre_prompt" => new UnknownEvent(method, raw),
            "hook_post_response" when IsValidHookPostResponse(raw) => new HookPostResponseEvent(
                GetLong(raw, "tokensUsed")!.Value,
                ParseTokenUsageStatus(GetString(raw, "tokensUsageStatus")),
                GetInt(raw, "toolCallsCount")!.Value,
                GetDouble(raw, "duration")!.Value,
                raw),
            "hook_post_response" => new UnknownEvent(method, raw),
            "mcp_invocation_request" when IsValidMcpInvocationRequest(raw) => new McpInvocationRequestEvent(
                GetString(raw, "requestId")!,
                GetString(raw, "toolName")!,
                GetObjectDictionary(raw, "args"),
                raw),
            "mcp_invocation_request" => new UnknownEvent(method, raw),
            "mcp_tools_changed" when IsValidMcpToolsChanged(raw) => new McpToolsChangedEvent(
                GetMcpTools(raw), raw),
            "mcp_tools_changed" => new UnknownEvent(method, raw),
            "learn_progress" when IsValidLearnProgress(raw) => new LearnProgressEvent(
                ParseLearnProgressStatus(GetString(raw, "status"))!.Value, raw),
            "learn_progress" => new UnknownEvent(method, raw),
            _ => new UnknownEvent(type, raw),
        };
    }

    private static string MethodToType(string method) =>
        method switch
        {
            "autohand.agentStart" => "agent_start",
            "autohand.agentEnd" => "agent_end",
            "autohand.turnStart" => "turn_start",
            "autohand.turnEnd" => "turn_end",
            "autohand.messageStart" => "message_start",
            "autohand.messageUpdate" => "message_update",
            "autohand.messageEnd" => "message_end",
            "autohand.toolStart" => "tool_start",
            "autohand.toolUpdate" => "tool_update",
            "autohand.toolEnd" => "tool_end",
            "autohand.permissionRequest" => "permission_request",
            "autohand.autoresearch.start" => "autoresearch",
            "autohand.autoresearch.status" => "autoresearch",
            "autohand.autoresearch.pause" => "autoresearch",
            "autohand.autoresearch.event" => "autoresearch",
            "autohand.automode.iteration" => "automode_iteration",
            "autohand.automode.complete" => "automode_complete",
            "autohand.automode.error" => "automode_error",
            "autohand.hook.preTool" => "hook_pre_tool",
            "autohand.hook.postTool" => "hook_post_tool",
            "autohand.hook.prePrompt" => "hook_pre_prompt",
            "autohand.hook.postResponse" => "hook_post_response",
            "autohand.mcp.invokeRequest" => "mcp_invocation_request",
            "autohand.mcp.toolsChanged" => "mcp_tools_changed",
            "autohand.learn.progress" => "learn_progress",
            "autohand.error" => "error",
            _ => method.StartsWith("autohand.", StringComparison.Ordinal)
                ? method["autohand.".Length..]
                : method,
        };

    private static string? AutoresearchPhase(string method) =>
        method switch
        {
            "autohand.autoresearch.start" => "start",
            "autohand.autoresearch.status" => "status",
            "autohand.autoresearch.pause" => "pause",
            _ => null,
        };

    private static bool IsStrictFeatureType(string type) =>
        type is
            "automode_iteration" or
            "automode_complete" or
            "automode_error" or
            "hook_pre_tool" or
            "hook_post_tool" or
            "hook_pre_prompt" or
            "hook_post_response" or
            "mcp_invocation_request" or
            "mcp_tools_changed" or
            "learn_progress";

    private static bool IsValidAutoModeIteration(JsonElement raw) =>
        HasTimestamp(raw) &&
        HasString(raw, "sessionId") &&
        HasInt32(raw, "iteration") &&
        HasStringArray(raw, "actions") &&
        HasOptionalInt64(raw, "tokensUsed");

    private static bool IsValidAutoModeComplete(JsonElement raw) =>
        HasTimestamp(raw) &&
        HasString(raw, "sessionId") &&
        HasInt32(raw, "iterations") &&
        HasInt32(raw, "filesCreated") &&
        HasInt32(raw, "filesModified");

    private static bool IsValidAutoModeError(JsonElement raw) =>
        HasTimestamp(raw) && HasString(raw, "sessionId") && HasString(raw, "error");

    private static bool IsValidHookPreTool(JsonElement raw) =>
        HasTimestamp(raw) &&
        HasString(raw, "toolId") &&
        HasString(raw, "toolName") &&
        HasObject(raw, "args");

    private static bool IsValidHookPostTool(JsonElement raw) =>
        HasTimestamp(raw) &&
        HasString(raw, "toolId") &&
        HasString(raw, "toolName") &&
        HasBool(raw, "success") &&
        HasNumber(raw, "duration") &&
        HasOptionalString(raw, "output");

    private static bool IsValidHookPrePrompt(JsonElement raw) =>
        HasTimestamp(raw) && HasString(raw, "instruction") && HasStringArray(raw, "mentionedFiles");

    private static bool IsValidHookPostResponse(JsonElement raw) =>
        HasTimestamp(raw) &&
        HasInt64(raw, "tokensUsed") &&
        HasOptionalTokenUsageStatus(raw) &&
        HasInt32(raw, "toolCallsCount") &&
        HasNumber(raw, "duration");

    private static bool IsValidMcpInvocationRequest(JsonElement raw) =>
        HasTimestamp(raw) &&
        HasString(raw, "requestId") &&
        HasString(raw, "toolName") &&
        HasObject(raw, "args");

    private static bool IsValidMcpToolsChanged(JsonElement raw)
    {
        if (!HasTimestamp(raw) ||
            !raw.TryGetProperty("tools", out var tools) ||
            tools.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return tools.EnumerateArray().All(tool =>
            HasString(tool, "name") &&
            HasString(tool, "description") &&
            HasString(tool, "serverName"));
    }

    private static bool IsValidLearnProgress(JsonElement raw) =>
        HasTimestamp(raw) &&
        HasString(raw, "status") &&
        ParseLearnProgressStatus(GetString(raw, "status")) is not null;

    private static bool HasTimestamp(JsonElement raw) => HasString(raw, "timestamp");

    private static bool HasString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String;

    private static bool HasBool(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False;

    private static bool HasInt32(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out _);

    private static bool HasInt64(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out _);

    private static bool HasNumber(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out _);

    private static bool HasObject(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Object;

    private static bool HasOptionalString(JsonElement element, string property) =>
        !element.TryGetProperty(property, out var value) ||
        value.ValueKind is JsonValueKind.Null or JsonValueKind.String;

    private static bool HasOptionalInt64(JsonElement element, string property) =>
        !element.TryGetProperty(property, out var value) ||
        value.ValueKind == JsonValueKind.Null ||
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _);

    private static bool HasOptionalTokenUsageStatus(JsonElement element)
    {
        if (!element.TryGetProperty("tokensUsageStatus", out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        return value.ValueKind == JsonValueKind.String &&
            ParseTokenUsageStatus(value.GetString()) is not null;
    }

    private static bool HasStringArray(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Array &&
        value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String);

    private static string? GetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static bool? GetBool(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static long? GetLong(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt64(out var parsed)
                ? parsed
                : null;
    }

    private static int? GetInt(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var parsed)
                ? parsed
                : null;
    }

    private static IReadOnlyList<string> GetStringList(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, JsonElement> GetObjectDictionary(
        JsonElement element,
        string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, JsonElement>();
        }

        return value.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone());
    }

    private static TokenUsageStatus? ParseTokenUsageStatus(string? value) =>
        value switch
        {
            "actual" => TokenUsageStatus.Actual,
            "unavailable" => TokenUsageStatus.Unavailable,
            _ => null,
        };

    private static IReadOnlyList<McpToolSummary> GetMcpTools(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("tools", out var tools) ||
            tools.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<McpToolSummary>();
        foreach (var tool in tools.EnumerateArray())
        {
            var name = GetString(tool, "name");
            var description = GetString(tool, "description");
            var serverName = GetString(tool, "serverName");
            if (name is not null && description is not null && serverName is not null)
            {
                result.Add(new McpToolSummary(name, description, serverName));
            }
        }

        return result;
    }

    private static LearnProgressStatus? ParseLearnProgressStatus(string? value) =>
        value switch
        {
            "analyzing" => LearnProgressStatus.Analyzing,
            "loading-registry" => LearnProgressStatus.LoadingRegistry,
            "evaluating" => LearnProgressStatus.Evaluating,
            "generating" => LearnProgressStatus.Generating,
            "updating" => LearnProgressStatus.Updating,
            _ => null,
        };

    private static double? GetDouble(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out var parsed)
                ? parsed
                : null;
    }
}
