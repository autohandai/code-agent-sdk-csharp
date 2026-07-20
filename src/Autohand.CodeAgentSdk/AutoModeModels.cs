using System.Text.Json;
using System.Text.Json.Serialization;

namespace Autohand.CodeAgentSdk;

public sealed record AutoModeStartParams(string Prompt)
{
    public int? MaxIterations { get; init; }
    public string? CompletionPromise { get; init; }
    public bool? UseWorktree { get; init; }
    public int? CheckpointInterval { get; init; }
    public int? MaxRuntime { get; init; }
    public double? MaxCost { get; init; }
}

public sealed record AutoModeStartResult(
    bool Success,
    string? SessionId = null,
    string? Error = null);

[JsonConverter(typeof(AutoModeSessionStatusJsonConverter))]
public enum AutoModeSessionStatus
{
    Running,
    Paused,
    Completed,
    Cancelled,
    Failed,
}

public sealed record AutoModeCheckpoint(
    string Commit,
    string Message,
    string Timestamp);

public sealed record AutoModeState(
    string SessionId,
    AutoModeSessionStatus Status,
    int CurrentIteration,
    int MaxIterations,
    int FilesCreated,
    int FilesModified,
    string? Branch = null,
    AutoModeCheckpoint? LastCheckpoint = null);

public sealed record AutoModeStatusResult(
    bool Active,
    bool Paused,
    AutoModeState? State = null);

public sealed record AutoModeOperationResult(
    bool Success,
    string? Error = null);

internal sealed class AutoModeSessionStatusJsonConverter : JsonConverter<AutoModeSessionStatus>
{
    public override AutoModeSessionStatus Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        reader.GetString() switch
        {
            "running" => AutoModeSessionStatus.Running,
            "paused" => AutoModeSessionStatus.Paused,
            "completed" => AutoModeSessionStatus.Completed,
            "cancelled" => AutoModeSessionStatus.Cancelled,
            "failed" => AutoModeSessionStatus.Failed,
            var value => throw new JsonException($"Unknown auto-mode status: {value}"),
        };

    public override void Write(
        Utf8JsonWriter writer,
        AutoModeSessionStatus value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            AutoModeSessionStatus.Running => "running",
            AutoModeSessionStatus.Paused => "paused",
            AutoModeSessionStatus.Completed => "completed",
            AutoModeSessionStatus.Cancelled => "cancelled",
            AutoModeSessionStatus.Failed => "failed",
            _ => throw new JsonException($"Unknown auto-mode status: {value}"),
        });
}
