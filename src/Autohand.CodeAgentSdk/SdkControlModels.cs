namespace Autohand.CodeAgentSdk;

/// <summary>Result returned after acknowledging receipt of a permission prompt.</summary>
public sealed record PermissionAcknowledgementResult(bool Success);

/// <summary>Result returned after resolving a directory-access prompt.</summary>
public sealed record DirectoryAccessResponseResult(bool Success);

/// <summary>Result returned after acknowledging receipt of a directory-access prompt.</summary>
public sealed record DirectoryAccessAcknowledgementResult(bool Success);

[JsonConverter(typeof(ChangesDecisionActionJsonConverter))]
public enum ChangesDecisionAction
{
    AcceptAll,
    RejectAll,
    AcceptSelected,
}

public sealed record ChangesDecisionParams(
    string BatchId,
    ChangesDecisionAction Action,
    IReadOnlyList<string>? SelectedChangeIds = null);

public sealed record ChangesDecisionError(string ChangeId, string Error);

public sealed record ChangesDecisionResult(
    bool Success,
    int AppliedCount,
    int SkippedCount,
    IReadOnlyList<ChangesDecisionError>? Errors = null);

internal sealed class ChangesDecisionActionJsonConverter : JsonConverter<ChangesDecisionAction>
{
    public override ChangesDecisionAction Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() switch
        {
            "accept_all" => ChangesDecisionAction.AcceptAll,
            "reject_all" => ChangesDecisionAction.RejectAll,
            "accept_selected" => ChangesDecisionAction.AcceptSelected,
            var value => throw new JsonException($"Unknown changes decision action: {value}"),
        };

    public override void Write(Utf8JsonWriter writer, ChangesDecisionAction value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            ChangesDecisionAction.AcceptAll => "accept_all",
            ChangesDecisionAction.RejectAll => "reject_all",
            ChangesDecisionAction.AcceptSelected => "accept_selected",
            _ => throw new JsonException($"Unknown changes decision action: {value}"),
        });
}

public sealed record SessionHistoryParams(int? Page = null, int? PageSize = null);

[JsonConverter(typeof(SessionHistoryStatusJsonConverter))]
public enum SessionHistoryStatus
{
    Active,
    Completed,
    Crashed,
}

public sealed record SessionHistoryEntry(
    string SessionId,
    string CreatedAt,
    string LastActiveAt,
    string ProjectName,
    string Model,
    int MessageCount,
    SessionHistoryStatus Status);

public sealed record SessionHistoryResult(
    IReadOnlyList<SessionHistoryEntry> Sessions,
    int CurrentPage,
    int TotalPages,
    int TotalItems);

internal sealed class SessionHistoryStatusJsonConverter : JsonConverter<SessionHistoryStatus>
{
    public override SessionHistoryStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() switch
        {
            "active" => SessionHistoryStatus.Active,
            "completed" => SessionHistoryStatus.Completed,
            "crashed" => SessionHistoryStatus.Crashed,
            var value => throw new JsonException($"Unknown session history status: {value}"),
        };

    public override void Write(Utf8JsonWriter writer, SessionHistoryStatus value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString().ToLowerInvariant());
}

[JsonConverter(typeof(SessionMessageRoleJsonConverter))]
public enum SessionMessageRole
{
    User,
    Assistant,
    System,
    Tool,
}

public sealed record SessionToolCall(
    string Id,
    string Name,
    IReadOnlyDictionary<string, JsonElement> Args);

public sealed record SessionMessage(
    string Id,
    SessionMessageRole Role,
    string Content,
    string Timestamp,
    IReadOnlyList<SessionToolCall>? ToolCalls = null);

public abstract record SessionDetailsResult(bool Success);

public sealed record SessionDetailsSuccess(
    string SessionId,
    string ProjectName,
    string Model,
    int MessageCount,
    string Status,
    string CreatedAt,
    string LastActiveAt,
    string? Summary,
    IReadOnlyList<SessionMessage> Messages,
    string WorkspaceRoot) : SessionDetailsResult(true);

public sealed record SessionDetailsFailure(string Error) : SessionDetailsResult(false);

internal sealed record SessionDetailsPayload(
    bool Success,
    string? SessionId,
    string? ProjectName,
    string? Model,
    int MessageCount,
    string? Status,
    string? CreatedAt,
    string? LastActiveAt,
    string? Summary,
    IReadOnlyList<SessionMessage>? Messages,
    string? WorkspaceRoot);

internal sealed class SessionMessageRoleJsonConverter : JsonConverter<SessionMessageRole>
{
    public override SessionMessageRole Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() switch
        {
            "user" => SessionMessageRole.User,
            "assistant" => SessionMessageRole.Assistant,
            "system" => SessionMessageRole.System,
            "tool" => SessionMessageRole.Tool,
            var value => throw new JsonException($"Unknown session message role: {value}"),
        };

    public override void Write(Utf8JsonWriter writer, SessionMessageRole value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString().ToLowerInvariant());
}

public sealed record SessionAttachmentResult(
    bool Success,
    string? SessionId = null,
    string? WorkspaceRoot = null,
    int? MessageCount = null,
    string? Error = null);

public sealed record YoloModeParams(string Pattern, int? TimeoutSeconds = null);

public sealed record YoloModeResult(bool Success, int? ExpiresIn = null);
