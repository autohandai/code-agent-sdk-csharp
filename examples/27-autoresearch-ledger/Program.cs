using Autohand.CodeAgentSdk;

await using var agent = await Agent.CreateAsync(new AgentOptions
{
    WorkingDirectory = ".",
});

if (!await agent.SupportsCommandAsync("/autoresearch"))
{
    Console.Error.WriteLine("The connected Autohand CLI does not expose /autoresearch.");
    return;
}

var started = await agent.StartAutoresearchAsync(new AutoresearchStartParams(
    "Reduce C# SDK test runtime without failures")
{
    MetricName = "total_ms",
    MetricUnit = "ms",
    Direction = "lower",
    MeasureCommand = "dotnet test",
    MaxIterations = 8,
    Sampling = new AutoresearchSamplingOptions { MinSamples = 3, MaxSamples = 7 },
    Constraints = [new AutoresearchConstraint("failures", "<=", 0)],
});

if (!started.Success)
{
    throw new InvalidOperationException(started.Error ?? "Autoresearch failed to start.");
}

var status = await agent.GetAutoresearchStatusAsync();
var history = await agent.GetAutoresearchHistoryAsync();
var pareto = await agent.GetAutoresearchParetoAsync();
var preview = await agent.PruneAutoresearchAsync(new AutoresearchPruneParams { DryRun = true });
Console.WriteLine(
    $"active={status.Active} attempts={history.Attempts.Count} pareto={pareto.AttemptIds.Count} " +
    $"prunableBytes={preview.BytesFreed}");
await agent.StopAutoresearchAsync();
