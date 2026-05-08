# Error Handling

SDK errors fall into four categories.

## Transport Errors

The CLI subprocess could not start, disconnected, or returned invalid JSON.

Common causes:

- `AUTOHAND_CLI_PATH` points to a missing binary.
- The CLI is not authenticated.
- The provider config is invalid.

## Request Timeouts

Requests time out according to `AutohandOptions.RequestTimeout`.

```csharp
var options = new AutohandOptions
{
    RequestTimeout = TimeSpan.FromMinutes(2),
};
```

## JSON-RPC Errors

The CLI rejected a request. Examples:

- calling a control method before `StartAsync()`;
- sending an expired permission request id;
- setting an unsupported model.

`RpcException` exposes `Code`, `RpcMessage`, and `RpcData`.

## Agent Events

Agent loop failures may arrive as `ErrorEvent` values in the stream. Handle them in your event loop and show enough context for users to recover.

## Recovery Patterns

- Stop and restart the SDK after transport failures.
- Use `InterruptAsync()` or `Run.AbortAsync()` for user cancellation.
- Keep final summaries honest when checks fail.
- Keep raw `JsonElement` event data available for debugging advanced CLI behavior.
