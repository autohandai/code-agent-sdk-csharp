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
