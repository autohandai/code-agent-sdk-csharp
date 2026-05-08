using System.Text.Json.Nodes;

namespace Autohand.CodeAgentSdk;

/// <summary>
/// Options used to start the Autohand CLI in JSON-RPC mode.
/// </summary>
public record AutohandOptions
{
    public string? WorkingDirectory { get; init; }
    public string? CliPath { get; init; }
    public bool Debug { get; init; }
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public bool Unrestricted { get; init; }
    public bool AutoMode { get; init; }
    public bool AutoSkill { get; init; }
    public bool AutoCommit { get; init; }
    public bool? ContextCompact { get; init; }
    public int? MaxIterations { get; init; }
    public int? MaxRuntimeMinutes { get; init; }
    public decimal? MaxCost { get; init; }
    public string? Model { get; init; }
    public double? Temperature { get; init; }
    public string? SystemPrompt { get; init; }
    public string? AppendSystemPrompt { get; init; }
    public string? Yolo { get; init; }
    public int? YoloTimeoutSeconds { get; init; }
    public IReadOnlyList<string> AdditionalDirectories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Skills { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExtraArgs { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string?> Environment { get; init; } =
        new Dictionary<string, string?>(StringComparer.Ordinal);
}

/// <summary>
/// High-level agent options. Instructions are appended to the default Autohand
/// system prompt instead of replacing the whole agent contract.
/// </summary>
public sealed record AgentOptions : AutohandOptions
{
    public string? Instructions { get; init; }
}

public sealed record ImageAttachment(string Data, string MimeType);

public sealed record PromptOptions
{
    public JsonObject? Context { get; init; }
    public IReadOnlyList<ImageAttachment> Images { get; init; } = Array.Empty<ImageAttachment>();
    public string? ThinkingLevel { get; init; }
    public JsonObject? Extra { get; init; }
}

public sealed record JsonRunOptions
{
    public string? SchemaName { get; init; }
    public object? Schema { get; init; }
    public string? OutputInstructions { get; init; }
    public JsonSerializerOptions? SerializerOptions { get; init; }
}

