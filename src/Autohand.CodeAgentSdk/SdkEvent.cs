namespace Autohand.CodeAgentSdk;

public abstract record SdkEvent(string Type, JsonElement Raw);

public sealed record AgentStartEvent(JsonElement Raw) : SdkEvent("agent_start", Raw);

public sealed record AgentEndEvent(JsonElement Raw) : SdkEvent("agent_end", Raw);

public sealed record TurnStartEvent(JsonElement Raw) : SdkEvent("turn_start", Raw);

public sealed record TurnEndEvent(JsonElement Raw) : SdkEvent("turn_end", Raw);

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
            "turn_end" => new TurnEndEvent(raw),
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
            "autohand.error" => "error",
            _ => method.StartsWith("autohand.", StringComparison.Ordinal)
                ? method["autohand.".Length..]
                : method,
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
}

