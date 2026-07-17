# Replayable Autoresearch Ledger

The C# SDK exposes Autohand's complete autoresearch JSON-RPC contract with typed
parameters, typed top-level results, and raw `JsonElement` records for immutable
evaluations, decisions, samples, comparisons, and prune candidates.

## Capability check

```csharp
if (await agent.SupportsCommandAsync("/autoresearch"))
{
    await agent.Autoresearch("Improve benchmark accuracy").WaitAsync();
}
```

## Start and inspect

```csharp
var started = await agent.StartAutoresearchAsync(new AutoresearchStartParams(
    "Reduce test runtime without regressions")
{
    MetricName = "total_ms",
    MetricUnit = "ms",
    Direction = "lower",
    MeasureCommand = "dotnet test",
    MaxIterations = 12,
    TimeoutMs = 60_000,
    FilesInScope = ["src", "tests"],
    SecondaryObjectives =
    [
        new AutoresearchSecondaryObjective("peak_memory_mb", "mb", "lower"),
    ],
    Constraints =
    [
        new AutoresearchConstraint("failures", "<=", 0),
    ],
    Sampling = new AutoresearchSamplingOptions
    {
        MinSamples = 3,
        MaxSamples = 7,
        ConfidenceThreshold = 0.9,
    },
});

var status = await agent.GetAutoresearchStatusAsync();
var history = await agent.GetAutoresearchHistoryAsync();
```

Stopping pauses without deleting the persisted `.auto/` state.

## Replay and decisions

```csharp
var replay = await agent.ReplayAutoresearchAsync(
    new AutoresearchReplayParams("attempt-1", "current"));
var rescored = await agent.RescoreAutoresearchAsync(
    new AutoresearchRescoreParams { AttemptId = "attempt-1" });
var comparison = await agent.CompareAutoresearchAsync(
    new AutoresearchCompareParams("attempt-1", "attempt-2"));
var pareto = await agent.GetAutoresearchParetoAsync();
```

Replay evaluates in an isolated worktree. Rescoring appends a new decision from
stored measurements and current policy without rewriting evaluations.

## Pin and prune safely

```csharp
await agent.PinAutoresearchAsync(new AutoresearchPinParams("attempt-1", true));
var preview = await agent.PruneAutoresearchAsync(
    new AutoresearchPruneParams { DryRun = true });

// Apply only after inspecting preview.Candidates.
var applied = await agent.PruneAutoresearchAsync(
    new AutoresearchPruneParams { DryRun = false, Yes = true });
```

Pinned and materialized candidates remain protected. Always preview first.
