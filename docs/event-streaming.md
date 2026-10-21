# Event Streaming

`StreamPromptAsync()` starts a prompt and yields events as they arrive.
Both it and `PromptAsync()` wait for the terminal event and the prompt RPC
response. Acknowledgement alone does not complete a prompt. Disposing a prompt
stream early aborts its turn and drains terminal notifications before the next
queued prompt starts; if cleanup cannot finish within five seconds, the SDK
stops the CLI process.

`EventsAsync()` creates an independent observer subscription, so multiple
observers and a prompt stream each receive the same notifications. Each
subscription retains at most 1,024 unread events and drops the oldest item when
that limit is reached. Stopping the SDK or losing the CLI stdout stream wakes
blocked observers; a later restart uses a fresh event generation.

## Basic Pattern

```csharp
await foreach (var item in sdk.StreamPromptAsync("Explain closures in one sentence."))
{
    if (item is MessageUpdateEvent message)
    {
        Console.Write(message.Delta);
    }
}
```

## Event Types

- `MessageUpdateEvent`: token or text delta.
- `MessageEndEvent`: final message content.
- `ToolStartEvent`: a tool started.
- `ToolUpdateEvent`: streaming tool output.
- `ToolEndEvent`: a tool completed.
- `StepEndEvent`: persisted tool calls and results, with a host decision boundary
  when [stop conditions](./step-control.md) are configured.
- `PermissionRequestEvent`: host approval is required.
- `ErrorEvent`: agent or transport error.
- `TurnEndEvent`: typed `TokensUsed`, `TokensUsageStatus`, `DurationMs`, and
  `ContextPercent` values when the CLI reports them, plus the terminal `Reason`.
- `AutoresearchEvent`: lifecycle and ledger-operation notifications with typed
  phase, operation, success, attempt ID, and applied state.

## Typed CLI Hook Notifications

The SDK maps all 16 `autohand.hook.*` notifications to typed records:

| RPC method | Event record |
| --- | --- |
| `autohand.hook.preTool` | `HookPreToolEvent` |
| `autohand.hook.postTool` | `HookPostToolEvent` |
| `autohand.hook.fileModified` | `HookFileModifiedEvent` |
| `autohand.hook.prePrompt` | `HookPrePromptEvent` |
| `autohand.hook.postResponse` | `HookPostResponseEvent` |
| `autohand.hook.sessionError` | `HookSessionErrorEvent` |
| `autohand.hook.stop` | `HookStopEvent` |
| `autohand.hook.sessionStart` | `HookSessionStartEvent` |
| `autohand.hook.sessionEnd` | `HookSessionEndEvent` |
| `autohand.hook.subagentStop` | `HookSubagentStopEvent` |
| `autohand.hook.permissionRequest` | `HookPermissionRequestEvent` |
| `autohand.hook.notification` | `HookNotificationEvent` |
| `autohand.hook.contextCompacted` | `HookContextCompactedEvent` |
| `autohand.hook.contextOverflow` | `HookContextOverflowEvent` |
| `autohand.hook.contextWarning` | `HookContextWarningEvent` |
| `autohand.hook.contextCritical` | `HookContextCriticalEvent` |

```csharp
if (item is HookContextWarningEvent warning)
{
    Console.WriteLine($"{warning.RemainingTokens} tokens remain");
}
```

Unknown notification methods and recognized hooks whose payload fails
validation remain observable as `UnknownEvent`. For malformed known hooks,
`EventType` retains the exact RPC method; in every fallback, `Raw` is a cloned
`JsonElement` preserving the original top-level params shape and values.

Context counts (`CroppedCount`, `TokensBefore`, `TokensAfter`, and
`RemainingTokens`) must be non-negative integers representable by `Int64`.
Fractions, negative values, or values outside that range use the raw fallback.
`UsagePercent` must be finite and non-negative.

## Handling Permissions While Streaming

```csharp
await foreach (var item in sdk.StreamPromptAsync("Run the relevant tests."))
{
    if (item is PermissionRequestEvent permission && permission.RequestId is not null)
    {
        await sdk.PermissionResponseAsync(permission.RequestId, "allow_once");
    }
}
```

Production hosts should route permission requests to a human, policy engine, or trusted automation boundary.

## Collecting Final Text

Use `Agent` and `Run` when you want streaming and a final result:

```csharp
var run = agent.Send("Summarize this repository.");
await foreach (var item in run.StreamAsync())
{
    Console.WriteLine(item.Type);
}
var result = await run.WaitAsync();
Console.WriteLine(result.Text);
```
