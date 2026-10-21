# Step control

Stop after completed tool results have been persisted, inspect those results,
and continue with the same agent. This requires a CLI that supports
`autohand.stepEnd`, `autohand.stepDecision`, and host-mode `stopWhen`.

```csharp
await using var agent = await Agent.CreateAsync(new AgentOptions
{
    WorkingDirectory = ".",
    Provider = "autohandai",
    Model = "fantail",
});

var result = await agent.RunAsync("Read README.md using read_file", new PromptOptions
{
    StopWhen = [StopConditions.IsStepCount(1)],
});

Console.WriteLine($"{result.Status}: {result.Steps.Count} completed steps");
foreach (var output in result.Steps.SelectMany(step => step.ToolResults))
    Console.WriteLine(output.Output ?? output.Error);

var continued = await agent.RunAsync("Summarize the saved result.");
Console.WriteLine(continued.Text);
```

`StopConditions.IsStepCount(n)` requires a positive count.
`StopConditions.HasToolCall(name)` trims the supplied name and checks the latest
completed step. Multiple conditions use OR: stop if any returns true. Conditions
receive a read-only snapshot of the current prompt's steps; each new prompt
starts a fresh step history while retaining the CLI conversation.

A custom `StopCondition` returns `ValueTask<bool>` and receives a cancellation
token. For example, a host can asynchronously inspect persisted tool results:

```csharp
StopCondition condition = async (context, cancellationToken) =>
{
    await Task.Delay(10, cancellationToken);
    return context.Steps[^1].ToolResults.Any(result => !result.Success);
};
var options = new PromptOptions { StopWhen = [condition] };
```

Callbacks stay in the host process. Only `{ "mode": "host" }` is sent as
`stopWhen`. Exceptions stop the CLI at the decision boundary and propagate to the
caller. Malformed step notifications and rejected decisions also fail the run.
Predicates should honor cancellation so their own work releases resources;
the SDK can cancel a run even when a predicate remains unresolved, and a late
predicate result cannot send another decision.

`RunResult.Steps` contains the completed steps and `Status` becomes `stopped`
when the CLI reports `reason: "stop_condition"`. Streaming consumers receive
typed `StepEndEvent` values with the same structured calls and results.

Use `await run.AbortAsync()` to cancel that run and await cleanup. Canceling a
queued run cannot abort the active run. A token supplied to `Agent.RunAsync`
or `Agent.StreamAsync` cancels its underlying run; disposing `Agent.StreamAsync`
also aborts unfinished work. A token supplied to `Run.WaitAsync` only cancels
that wait, and disposing an observer from `Run.StreamAsync` leaves its run active.

The low-level `PromptAsync` and `StreamPromptAsync` APIs share these stop
conditions and wait through terminal completion. For explicit acknowledgement
semantics, use `RequestAsync("autohand.prompt", parameters)` directly.

Run the example:

```sh
dotnet run --project examples/28-step-control/Autohand.Examples.StepControl.csproj
```
