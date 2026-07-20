namespace Autohand.CodeAgentSdk;

public abstract record SdkEvent(string Type, JsonElement Raw);

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
    string? SessionId,
    int? Iteration,
    IReadOnlyList<string> Actions,
    long? TokensUsed,
    JsonElement Raw)
    : SdkEvent("automode_iteration", Raw);

public sealed record AutoModeCompleteEvent(
    string? SessionId,
    int? Iterations,
    int? FilesCreated,
    int? FilesModified,
    JsonElement Raw)
    : SdkEvent("automode_complete", Raw);

public sealed record AutoModeErrorEvent(string? SessionId, string? Error, JsonElement Raw)
    : SdkEvent("automode_error", Raw);

public sealed record HookPreToolEvent(
    string? ToolId,
    string? ToolName,
    IReadOnlyDictionary<string, JsonElement> Args,
    JsonElement Raw)
    : SdkEvent("hook_pre_tool", Raw);

public sealed record HookPostToolEvent(
    string? ToolId,
    string? ToolName,
    bool? Success,
    long? Duration,
    string? Output,
    JsonElement Raw)
    : SdkEvent("hook_post_tool", Raw);

public sealed record UnknownEvent(string EventType, JsonElement Raw) : SdkEvent(EventType, Raw);

internal static class SdkEventParser
{
    public static SdkEvent Parse(string method, JsonElement parameters)
    {
        var raw = parameters.ValueKind == JsonValueKind.Undefined
            ? default
            : parameters.Clone();
        var type = GetString(raw, "type") ?? MethodToType(method);

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
            "automode_iteration" => new AutoModeIterationEvent(
                GetString(raw, "sessionId"),
                GetInt(raw, "iteration"),
                GetStringList(raw, "actions"),
                GetLong(raw, "tokensUsed"),
                raw),
            "automode_complete" => new AutoModeCompleteEvent(
                GetString(raw, "sessionId"),
                GetInt(raw, "iterations"),
                GetInt(raw, "filesCreated"),
                GetInt(raw, "filesModified"),
                raw),
            "automode_error" => new AutoModeErrorEvent(
                GetString(raw, "sessionId"),
                GetString(raw, "error"),
                raw),
            "hook_pre_tool" => new HookPreToolEvent(
                GetString(raw, "toolId"),
                GetString(raw, "toolName"),
                GetObjectDictionary(raw, "args"),
                raw),
            "hook_post_tool" => new HookPostToolEvent(
                GetString(raw, "toolId"),
                GetString(raw, "toolName"),
                GetBool(raw, "success"),
                GetLong(raw, "duration"),
                GetString(raw, "output"),
                raw),
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
            value.TryGetInt64(out var parsed)
                ? parsed
                : null;
    }

    private static int? GetInt(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(property, out var value) &&
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

    private static double? GetDouble(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(property, out var value) &&
            value.TryGetDouble(out var parsed)
                ? parsed
                : null;
    }
}
