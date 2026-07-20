namespace Autohand.CodeAgentSdk;

public sealed record GetSkillsRegistryParams(bool? ForceRefresh = null);

public sealed record CommunitySkill(
    string Id,
    string Name,
    string Description,
    string Category,
    IReadOnlyList<string>? Tags = null,
    double? Rating = null,
    int? DownloadCount = null,
    bool? IsFeatured = null,
    bool? IsCurated = null);

public sealed record SkillCategory(string Name, int Count);

public sealed record GetSkillsRegistryResult(
    bool Success,
    IReadOnlyList<CommunitySkill> Skills,
    IReadOnlyList<SkillCategory> Categories,
    string? Error = null);

[JsonConverter(typeof(SkillInstallScopeJsonConverter))]
public enum SkillInstallScope
{
    User,
    Project,
}

public sealed record InstallSkillParams(
    string SkillName,
    SkillInstallScope Scope,
    bool? Force = null);

public sealed record InstallSkillResult(
    bool Success,
    string? SkillName = null,
    string? Path = null,
    string? Error = null);

public sealed record McpServerSummary(string Name, string Status, int ToolCount);

public sealed record McpListServersResult(IReadOnlyList<McpServerSummary> Servers);

public sealed record McpListToolsParams(string? ServerName = null);

public sealed record McpToolSummary(string Name, string Description, string ServerName);

public sealed record McpListToolsResult(IReadOnlyList<McpToolSummary> Tools);

[JsonConverter(typeof(McpTransportKindJsonConverter))]
public enum McpTransportKind
{
    Stdio,
    Sse,
    Http,
}

public sealed record McpServerConfiguration(
    string Name,
    McpTransportKind Transport,
    string? Command = null,
    IReadOnlyList<string>? Args = null,
    string? Url = null,
    IReadOnlyDictionary<string, string>? Env = null,
    IReadOnlyDictionary<string, string>? Headers = null,
    bool? AutoConnect = null);

public sealed record McpGetServerConfigsResult(IReadOnlyList<McpServerConfiguration> Configs);

internal sealed class SkillInstallScopeJsonConverter : JsonConverter<SkillInstallScope>
{
    public override SkillInstallScope Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        reader.GetString() switch
        {
            "user" => SkillInstallScope.User,
            "project" => SkillInstallScope.Project,
            var value => throw new JsonException($"Unknown skill installation scope: {value}"),
        };

    public override void Write(
        Utf8JsonWriter writer,
        SkillInstallScope value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value == SkillInstallScope.User ? "user" : "project");
}

internal sealed class McpTransportKindJsonConverter : JsonConverter<McpTransportKind>
{
    public override McpTransportKind Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        reader.GetString() switch
        {
            "stdio" => McpTransportKind.Stdio,
            "sse" => McpTransportKind.Sse,
            "http" => McpTransportKind.Http,
            var value => throw new JsonException($"Unknown MCP transport: {value}"),
        };

    public override void Write(
        Utf8JsonWriter writer,
        McpTransportKind value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            McpTransportKind.Stdio => "stdio",
            McpTransportKind.Sse => "sse",
            McpTransportKind.Http => "http",
            _ => throw new JsonException($"Unknown MCP transport: {value}"),
        });
}
