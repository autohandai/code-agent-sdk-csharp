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
    public bool Bare { get; init; }
    public bool? IdleLogout { get; init; }
    public bool? ContextCompact { get; init; }
    public bool? AgentsMdEnable { get; init; }
    public bool AgentsMdCreate { get; init; }
    public string? AgentsMdPath { get; init; }
    public bool AgentsMdAutoUpdate { get; init; }
    public bool PersistSession { get; init; }
    public string? SessionId { get; init; }
    public bool Resume { get; init; }
    public bool ContinueSession { get; init; }
    public string? SessionPath { get; init; }
    public int? AutoSaveInterval { get; init; }
    public int? MaxTokens { get; init; }
    public double? CompressionThreshold { get; init; }
    public double? SummarizationThreshold { get; init; }
    public int? MaxIterations { get; init; }
    public int? MaxRuntimeMinutes { get; init; }
    public decimal? MaxCost { get; init; }
    public string? Model { get; init; }
    public double? Temperature { get; init; }
    public string? SystemPrompt { get; init; }
    public string? AppendSystemPrompt { get; init; }
    public string? ForkSession { get; init; }
    public string? DisplayLanguage { get; init; }
    public string? SystemPromptFile { get; init; }
    public string? AppendSystemPromptFile { get; init; }
    public string? McpConfig { get; init; }
    public string? Agents { get; init; }
    public string? PluginDirectory { get; init; }
    public FeatureFlagSettings? Features { get; init; }
    public string? Yolo { get; init; }
    public int? YoloTimeoutSeconds { get; init; }
    public IReadOnlyList<string> AdditionalDirectories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Skills { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SkillSources { get; init; } = Array.Empty<string>();
    public bool InstallMissingSkills { get; init; }
    public IReadOnlyList<string> ExtraArgs { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string?> Environment { get; init; } =
        new Dictionary<string, string?>(StringComparer.Ordinal);
    public string? Provider { get; init; }
    public string? ApiKey { get; init; }
    public string? BaseUrl { get; init; }
    public string? AutohandAiPlan { get; init; }
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
    /// <summary>Host-only predicates evaluated after persisted tool results.</summary>
    [JsonIgnore]
    public IReadOnlyList<StopCondition> StopWhen { get; init; } = Array.Empty<StopCondition>();
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

public sealed record FeatureFlagSettings
{
    public string? Environment { get; init; }
    public IReadOnlyDictionary<string, string>? RemoteOverrides { get; init; }
    public bool? UsageV2 { get; init; }
    public bool? AwsBedrockProvider { get; init; }
    public bool? SlashGoal { get; init; }
    public bool? TokenUsageStatus { get; init; }
    public bool? ExperimentalFork { get; init; }
    public bool? ExperimentalClone { get; init; }
    public bool? ExperimentalHandoff { get; init; }
}
