namespace Autohand.CodeAgentSdk;

/// <summary>A tool call and its structured arguments.</summary>
public sealed record AgentStepToolCall(string? Id, string Tool, JsonElement Args);

/// <summary>A tool result already persisted by the CLI.</summary>
public sealed record AgentStepToolResult(string Tool, bool Success, string? Output, string? Error);

/// <summary>A completed tool step.</summary>
public sealed record AgentStep(int StepNumber, string? Thought,
    IReadOnlyList<AgentStepToolCall> ToolCalls, IReadOnlyList<AgentStepToolResult> ToolResults);

/// <summary>An immutable snapshot of completed steps in the current prompt.</summary>
public sealed record StopConditionContext
{
    public StopConditionContext(IEnumerable<AgentStep> steps) => Steps = Array.AsReadOnly(steps.ToArray());
    public IReadOnlyList<AgentStep> Steps { get; }
}

/// <summary>A host-only asynchronous decision made after persisted tool results.</summary>
public delegate ValueTask<bool> StopCondition(StopConditionContext context, CancellationToken cancellationToken);

/// <summary>Common stop conditions. Multiple conditions are combined with OR.</summary>
public static class StopConditions
{
    public static StopCondition IsStepCount(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        return (context, _) => ValueTask.FromResult(context.Steps.Count >= count);
    }

    public static StopCondition HasToolCall(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        var name = toolName.Trim();
        return (context, _) => ValueTask.FromResult(context.Steps.Count > 0
            && context.Steps[^1].ToolCalls.Any(call => call.Tool == name));
    }
}

/// <summary>A persisted step and its CLI decision identifier.</summary>
public sealed record StepEndEvent(string StepId, AgentStep Step, JsonElement Raw) : SdkEvent("step_end", Raw);

internal sealed record StepDecision(string StepId, bool Stop, Exception? Failure = null);

internal static class StepControl
{
    public static async Task<StepDecision> EvaluateAsync(string stepId, IReadOnlyList<StopCondition> conditions,
        StopConditionContext context, CancellationToken cancellationToken)
    {
        var failure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Evaluate(StopCondition condition)
        {
            try { return await condition(context, cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) { failure.TrySetResult(exception); return false; }
        }

        var decisions = Task.WhenAll(conditions.Select(Evaluate));
        await Task.WhenAny(decisions, failure.Task).WaitAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (failure.Task.IsCompletedSuccessfully) return new StepDecision(stepId, true, failure.Task.Result);
        return new StepDecision(stepId, (await decisions.ConfigureAwait(false)).Any(stop => stop));
    }

    public static string? Reason(JsonElement raw) => raw.ValueKind == JsonValueKind.Object
        && raw.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String
            ? reason.GetString() : null;

    public static bool IsTerminal(SdkEvent item) => item is TurnEndEvent or AgentEndEvent;

    public static StepEndEvent? Parse(JsonElement raw)
    {
        if (!Has(raw, "stepId", JsonValueKind.String) || !Has(raw, "timestamp", JsonValueKind.String)
            || !Has(raw, "step", JsonValueKind.Object)) return null;
        var step = raw.GetProperty("step");
        if (!Has(step, "stepNumber", JsonValueKind.Number) || !step.GetProperty("stepNumber").TryGetInt32(out var number)
            || number < 1 || !Has(step, "toolCalls", JsonValueKind.Array) || !Has(step, "toolResults", JsonValueKind.Array)
            || !OptionalString(step, "thought")) return null;
        var calls = new List<AgentStepToolCall>();
        foreach (var call in step.GetProperty("toolCalls").EnumerateArray())
        {
            if (!Has(call, "tool", JsonValueKind.String) || !Has(call, "args", JsonValueKind.Object)
                || !OptionalString(call, "id")) return null;
            calls.Add(new AgentStepToolCall(Text(call, "id"), Text(call, "tool")!, call.GetProperty("args").Clone()));
        }
        var results = new List<AgentStepToolResult>();
        foreach (var result in step.GetProperty("toolResults").EnumerateArray())
        {
            if (!Has(result, "tool", JsonValueKind.String)
                || !(Has(result, "success", JsonValueKind.True) || Has(result, "success", JsonValueKind.False))
                || !OptionalString(result, "output") || !OptionalString(result, "error")) return null;
            results.Add(new AgentStepToolResult(Text(result, "tool")!, result.GetProperty("success").GetBoolean(),
                Text(result, "output"), Text(result, "error")));
        }
        return new StepEndEvent(Text(raw, "stepId")!, new AgentStep(number, Text(step, "thought"),
            calls.AsReadOnly(), results.AsReadOnly()), raw);
    }

    private static bool Has(JsonElement value, string field, JsonValueKind kind) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(field, out var property) && property.ValueKind == kind;

    private static bool OptionalString(JsonElement value, string field) => value.ValueKind == JsonValueKind.Object
        && (!value.TryGetProperty(field, out var property) || property.ValueKind == JsonValueKind.String);

    private static string? Text(JsonElement value, string field) => value.TryGetProperty(field, out var property)
        ? property.GetString() : null;
}
