namespace Autohand.CodeAgentSdk;

/// <summary>Effective subagent metadata from the running CLI session.</summary>
public sealed record AgentInfo(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string> Tools,
    string? Model = null,
    string? Source = null,
    string? ExtensionId = null,
    string? ExtensionVersion = null,
    string? ExtensionScope = null);

internal sealed record SupportedAgentsResult(IReadOnlyList<AgentInfo> Agents);

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

public sealed record VscodeMcpInputSchema(
    IReadOnlyDictionary<string, object?> Properties,
    IReadOnlyList<string>? Required = null)
{
    [JsonPropertyName("type")]
    public string Type => "object";
}

public sealed record VscodeMcpTool(
    string Name,
    string Description,
    string ServerName,
    VscodeMcpInputSchema? InputSchema = null);

public sealed record VscodeMcpToolsParams(IReadOnlyList<VscodeMcpTool> Tools);

public sealed record VscodeMcpToolsResult(bool Success);

public sealed record McpInvocationResponseParams(
    string RequestId,
    bool Success,
    string? Result = null,
    string? Error = null);

public sealed record McpInvocationResponseResult(bool Success);

public sealed record LearnRecommendationParams(bool? Deep = null);

[JsonConverter(typeof(LearnAuditStatusJsonConverter))]
public enum LearnAuditStatus
{
    Redundant,
    Outdated,
    Conflicting,
}

public sealed record LearnAuditEntry(string Skill, LearnAuditStatus Status, string Reason);

public sealed record LearnRecommendationEntry(string Slug, double Score, string Reason);

public sealed record LearnRecommendationResult(
    bool Success,
    string ProjectSummary,
    IReadOnlyList<LearnAuditEntry> Audit,
    IReadOnlyList<LearnRecommendationEntry> Recommendations,
    string? GapAnalysis,
    string? Error = null);

internal sealed class LearnAuditStatusJsonConverter : JsonConverter<LearnAuditStatus>
{
    public override LearnAuditStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() switch
        {
            "redundant" => LearnAuditStatus.Redundant,
            "outdated" => LearnAuditStatus.Outdated,
            "conflicting" => LearnAuditStatus.Conflicting,
            var value => throw new JsonException($"Unknown learning audit status: {value}"),
        };

    public override void Write(Utf8JsonWriter writer, LearnAuditStatus value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString().ToLowerInvariant());
}

[JsonConverter(typeof(LearnUpdateStatusJsonConverter))]
public enum LearnUpdateStatus
{
    Updated,
    Unchanged,
    Failed,
}

public sealed record LearnUpdateEntry(string Name, LearnUpdateStatus Status);

public sealed record LearnUpdateResult(
    bool Success,
    int Updated,
    int Unchanged,
    IReadOnlyList<LearnUpdateEntry> Results,
    string? Error = null);

internal sealed class LearnUpdateStatusJsonConverter : JsonConverter<LearnUpdateStatus>
{
    public override LearnUpdateStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() switch
        {
            "updated" => LearnUpdateStatus.Updated,
            "unchanged" => LearnUpdateStatus.Unchanged,
            "failed" => LearnUpdateStatus.Failed,
            var value => throw new JsonException($"Unknown learning update status: {value}"),
        };

    public override void Write(Utf8JsonWriter writer, LearnUpdateStatus value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString().ToLowerInvariant());
}

[JsonConverter(typeof(LearnGenerationScopeJsonConverter))]
public enum LearnGenerationScope
{
    Project,
    User,
}

public sealed record LearnGenerationParams(LearnGenerationScope Scope);

public sealed record LearnGenerationResult(
    bool Success,
    string? SkillName = null,
    string? SkillPath = null,
    string? Error = null);

internal sealed class LearnGenerationScopeJsonConverter : JsonConverter<LearnGenerationScope>
{
    public override LearnGenerationScope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() switch
        {
            "project" => LearnGenerationScope.Project,
            "user" => LearnGenerationScope.User,
            var value => throw new JsonException($"Unknown learning generation scope: {value}"),
        };

    public override void Write(Utf8JsonWriter writer, LearnGenerationScope value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value == LearnGenerationScope.Project ? "project" : "user");
}

[JsonConverter(typeof(ToolRegistrySourceJsonConverter))]
public enum ToolRegistrySource
{
    Builtin,
    Meta,
    Extension,
}

[JsonConverter(typeof(ToolRegistryScopeJsonConverter))]
public enum ToolRegistryScope
{
    User,
    Project,
}

public sealed record ToolRegistryEntry(
    string Name,
    string Description,
    bool? RequiresApproval,
    string? ApprovalMessage,
    ToolRegistrySource Source,
    ToolRegistryScope? Scope,
    bool? Disabled,
    string? CreatedAt,
    int? SchemaVersion,
    string? HandlerPreview,
    string? ReuseHint,
    string? ExtensionId,
    string? ExtensionVersion);

public sealed record ToolRegistryDiagnostic(string File, string Reason);

public sealed record ToolsRegistryResult(
    IReadOnlyList<ToolRegistryEntry> Tools,
    IReadOnlyList<ToolRegistryDiagnostic> Diagnostics);

internal sealed class ToolRegistrySourceJsonConverter : JsonConverter<ToolRegistrySource>
{
    public override ToolRegistrySource Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() switch
        {
            "builtin" => ToolRegistrySource.Builtin,
            "meta" => ToolRegistrySource.Meta,
            "extension" => ToolRegistrySource.Extension,
            var value => throw new JsonException($"Unknown tool registry source: {value}"),
        };

    public override void Write(Utf8JsonWriter writer, ToolRegistrySource value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString().ToLowerInvariant());
}

internal sealed class ToolRegistryScopeJsonConverter : JsonConverter<ToolRegistryScope>
{
    public override ToolRegistryScope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() switch
        {
            "user" => ToolRegistryScope.User,
            "project" => ToolRegistryScope.Project,
            var value => throw new JsonException($"Unknown tool registry scope: {value}"),
        };

    public override void Write(Utf8JsonWriter writer, ToolRegistryScope value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString().ToLowerInvariant());
}

public sealed record ContextCompactionResult(bool Enabled);
