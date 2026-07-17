using System.Text.Json.Serialization;

namespace Autohand.CodeAgentSdk;

public sealed record GoalParams
{
    [JsonPropertyName("objective")]
    public string? Objective { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("token_budget")]
    public long? TokenBudget { get; init; }

    [JsonPropertyName("time_budget_seconds")]
    public long? TimeBudgetSeconds { get; init; }

    [JsonPropertyName("min_tokens_before_wrap_up")]
    public long? MinTokensBeforeWrapUp { get; init; }

    [JsonPropertyName("min_time_seconds_before_wrap_up")]
    public long? MinTimeSecondsBeforeWrapUp { get; init; }
}

public sealed record AutoresearchSubagentOptions
{
    public bool? IdeaGeneration { get; init; }
    public bool? MeasurementAnalysis { get; init; }
    public bool? Finalization { get; init; }
}

public sealed record AutoresearchSecondaryObjective(
    string Name,
    string Unit,
    string Direction);

public sealed record AutoresearchConstraint(
    string MetricName,
    string Operator,
    double Threshold);

public sealed record AutoresearchSamplingOptions
{
    public int? MinSamples { get; init; }
    public int? MaxSamples { get; init; }
    public double? ConfidenceThreshold { get; init; }
}

public sealed record AutoresearchRetentionOptions
{
    public long? MaxArtifactBytes { get; init; }
    public int? MaxArtifactAgeDays { get; init; }
}

public sealed record AutoresearchStartParams(string Objective)
{
    public int? MaxIterations { get; init; }
    public long? TimeoutMs { get; init; }
    public string? MetricName { get; init; }
    public string? MetricUnit { get; init; }
    public string? Direction { get; init; }
    public string? MeasureCommand { get; init; }
    public string? MeasureScript { get; init; }
    public string? ChecksCommand { get; init; }
    public string? ChecksScript { get; init; }
    public IReadOnlyList<string>? FilesInScope { get; init; }
    public AutoresearchSubagentOptions? Subagents { get; init; }
    public IReadOnlyList<AutoresearchSecondaryObjective>? SecondaryObjectives { get; init; }
    public IReadOnlyList<AutoresearchConstraint>? Constraints { get; init; }
    public AutoresearchSamplingOptions? Sampling { get; init; }
    public AutoresearchRetentionOptions? Retention { get; init; }
    public IReadOnlyList<string>? EnvironmentAllowlist { get; init; }
}

public sealed record AutoresearchState(
    bool Active,
    string Goal,
    int Iteration,
    int MaxIterations);

public sealed record AutoresearchStartResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public string? Instruction { get; init; }
    public bool? Active { get; init; }
    public AutoresearchState? State { get; init; }
    public string? StatusText { get; init; }
    public int? RunsLogged { get; init; }
    public IReadOnlyList<JsonElement>? Attempts { get; init; }
    public IReadOnlyList<string>? ParetoAttemptIds { get; init; }
    public string? Error { get; init; }
}

public sealed record AutoresearchStatusResult
{
    public bool Success { get; init; }
    public bool Active { get; init; }
    public AutoresearchState? State { get; init; }
    public string? StatusText { get; init; }
    public int RunsLogged { get; init; }
    public IReadOnlyList<JsonElement>? Attempts { get; init; }
    public IReadOnlyList<string>? ParetoAttemptIds { get; init; }
    public string? Error { get; init; }
}

public sealed record AutoresearchStopResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public bool? Active { get; init; }
    public AutoresearchState? State { get; init; }
    public string? StatusText { get; init; }
    public int? RunsLogged { get; init; }
    public IReadOnlyList<JsonElement>? Attempts { get; init; }
    public IReadOnlyList<string>? ParetoAttemptIds { get; init; }
    public string? Error { get; init; }
}

public sealed record AutoresearchHistoryResult(
    bool Success,
    IReadOnlyList<JsonElement> Attempts,
    string? Error = null);

public sealed record AutoresearchReplayParams(string AttemptId, string? Evaluator = null);

public sealed record AutoresearchReplayResult
{
    public bool Success { get; init; }
    public string? AttemptId { get; init; }
    public string? EvaluatorMode { get; init; }
    public IReadOnlyDictionary<string, double>? Metrics { get; init; }
    public IReadOnlyList<JsonElement>? Samples { get; init; }
    public JsonElement? Decision { get; init; }
    public IReadOnlyList<string>? DriftWarnings { get; init; }
    public string? Error { get; init; }
}

public sealed record AutoresearchRescoreParams
{
    public string? AttemptId { get; init; }
    public bool? All { get; init; }
}

public sealed record AutoresearchRescoreResult(
    bool Success,
    IReadOnlyList<JsonElement> Decisions,
    string? Error = null);

public sealed record AutoresearchCompareParams(string LeftAttemptId, string RightAttemptId);

public sealed record AutoresearchCompareResult(
    bool Success,
    JsonElement? Comparison = null,
    string? Error = null);

public sealed record AutoresearchParetoResult(
    bool Success,
    IReadOnlyList<string> AttemptIds,
    string? Error = null);

public sealed record AutoresearchPinParams(string AttemptId, bool Pinned);

public sealed record AutoresearchPinResult(
    bool Success,
    string AttemptId,
    bool Pinned,
    string? Error = null);

public sealed record AutoresearchPruneParams
{
    public bool? DryRun { get; init; }
    public bool? Yes { get; init; }
}

public sealed record AutoresearchPruneResult
{
    public bool Success { get; init; }
    public bool Applied { get; init; }
    public IReadOnlyList<JsonElement> Candidates { get; init; } = Array.Empty<JsonElement>();
    public long BytesFreed { get; init; }
    public long RemainingBytes { get; init; }
    public string? Error { get; init; }
}
