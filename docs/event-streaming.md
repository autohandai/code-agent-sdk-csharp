# Event Streaming

`StreamPromptAsync()` starts a prompt and yields events as they arrive.

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
- `PermissionRequestEvent`: host approval is required.
- `ErrorEvent`: agent or transport error.

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
