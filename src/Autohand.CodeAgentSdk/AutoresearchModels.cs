using System.Text.Json.Serialization;

namespace Autohand.CodeAgentSdk;

public sealed record GoalParams
{
    [JsonPropertyName("objective")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Objective { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; init; }

    [JsonPropertyName("token_budget")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TokenBudget { get; init; }

    [JsonPropertyName("time_budget_seconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TimeBudgetSeconds { get; init; }

    [JsonPropertyName("min_tokens_before_wrap_up")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MinTokensBeforeWrapUp { get; init; }

    [JsonPropertyName("min_time_seconds_before_wrap_up")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MinTimeSecondsBeforeWrapUp { get; init; }
}

public sealed record GoalUpdateParams
{
    [JsonPropertyName("objective")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Objective { get; init; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; init; }

    [JsonPropertyName("token_budget")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public NullableUpdate<long> TokenBudget { get; init; }

    [JsonPropertyName("time_budget_seconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public NullableUpdate<long> TimeBudgetSeconds { get; init; }

    [JsonPropertyName("min_tokens_before_wrap_up")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public NullableUpdate<long> MinTokensBeforeWrapUp { get; init; }

    [JsonPropertyName("min_time_seconds_before_wrap_up")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public NullableUpdate<long> MinTimeSecondsBeforeWrapUp { get; init; }
}

[JsonConverter(typeof(NullableUpdateJsonConverterFactory))]
public readonly record struct NullableUpdate<T>
    where T : struct
{
    private NullableUpdate(NullableUpdateAction action, T value)
    {
        Action = action;
        Value = value;
    }

    internal NullableUpdateAction Action { get; }
    internal T Value { get; }

    public static NullableUpdate<T> Unchanged() => default;

    public static NullableUpdate<T> Clear() => new(NullableUpdateAction.Clear, default);

    public static NullableUpdate<T> Set(T value) => new(NullableUpdateAction.Set, value);
}

internal enum NullableUpdateAction
{
    Unchanged,
    Clear,
    Set,
}

internal sealed class NullableUpdateJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType &&
        typeToConvert.GetGenericTypeDefinition() == typeof(NullableUpdate<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];
        return (JsonConverter)Activator.CreateInstance(
            typeof(NullableUpdateJsonConverter<>).MakeGenericType(valueType))!;
    }

    private sealed class NullableUpdateJsonConverter<T> : JsonConverter<NullableUpdate<T>>
        where T : struct
    {
        public override NullableUpdate<T> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return NullableUpdate<T>.Clear();
            }

            var value = JsonSerializer.Deserialize<T>(ref reader, options);
            return NullableUpdate<T>.Set(value);
        }

        public override void Write(
            Utf8JsonWriter writer,
            NullableUpdate<T> value,
            JsonSerializerOptions options)
        {
            if (value.Action is NullableUpdateAction.Unchanged or NullableUpdateAction.Clear)
            {
                writer.WriteNullValue();
                return;
            }

            JsonSerializer.Serialize(writer, value.Value, options);
        }
    }
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
