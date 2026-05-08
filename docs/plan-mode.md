# Plan Mode

Plan mode keeps the agent in a read-only planning posture. Use it for discovery, architecture review, and implementation planning before allowing writes or commands.

## Enable Plan Mode

```csharp
await using var sdk = new AutohandSdk(new AutohandOptions
{
    WorkingDirectory = ".",
});

await sdk.StartAsync();
await sdk.SetPlanModeAsync(true);
```

## Two-Phase Workflow

1. Start in plan mode.
2. Ask the agent to inspect and produce a plan.
3. Stop and review the plan.
4. Disable plan mode for the approved implementation.
5. Handle permissions explicitly during execution.

```csharp
await sdk.SetPlanModeAsync(true);
// discovery prompt
await sdk.SetPlanModeAsync(false);
// implementation prompt
```

Plan mode and permission mode are separate. Plan mode controls which tools are available; permission mode controls whether individual tool calls require approval.
