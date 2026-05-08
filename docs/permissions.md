# Permissions

The Autohand CLI asks before running shell commands, writing files, or taking sensitive actions. The SDK surfaces those requests as `PermissionRequestEvent`.

## Recommended Default

Keep permission handling interactive unless your host has a clear trust boundary:

```csharp
await sdk.SetPermissionModeAsync("interactive");
```

## Responding To Requests

```csharp
if (item is PermissionRequestEvent permission && permission.RequestId is not null)
{
    await sdk.PermissionResponseAsync(permission.RequestId, "allow_once");
}
```

Common decisions:

- `allow_once`
- `deny_once`

## Agent Helpers

```csharp
await agent.AllowPermissionAsync(requestId);
await agent.DenyPermissionAsync(requestId);
await agent.SuggestPermissionAlternativeAsync(requestId, "Run dotnet test instead.");
```

## Production Guidance

- Show the tool name and description to the user.
- Deny by default when request context is missing.
- Avoid blanket approval for file writes or shell commands.
- Strip secrets from logs before attaching them to issues.
- Use plan mode for discovery before allowing writes.
