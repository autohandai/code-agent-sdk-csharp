# Configuration

The C# SDK keeps configuration close to the Autohand CLI contract. Most options become CLI flags when the subprocess starts.

## Basic Configuration

```csharp
var options = new AgentOptions
{
    WorkingDirectory = ".",
    Model = "fantail2",
    Skills = new[] { "csharp", "testing" },
    Instructions = "Prefer safe, idiomatic C#.",
};
```

`CliPath` can be set directly or with `AUTOHAND_CLI_PATH` in examples.

## Provider Credentials

Provider credentials are owned by the Autohand CLI, not the SDK. Configure them in `~/.autohand/config.json` or through environment variables supported by the CLI.

```json
{
  "provider": "openrouter",
  "openrouter": {
    "apiKey": "sk-or-...",
    "model": "openrouter/auto"
  }
}
```

## Runtime Options

Common options:

- `Model`: model override.
- `Temperature`: sampling temperature.
- `MaxIterations`: loop limit.
- `MaxRuntimeMinutes`: wall-clock limit.
- `MaxCost`: cost budget.
- `ContextCompact`: context compaction.
- `AdditionalDirectories`: extra workspace roots.
- `Skills`: skills available to the agent.
- `Environment`: environment variables for the CLI subprocess.

## System Prompts

Use `AgentOptions.Instructions` or `AppendSystemPrompt` for normal integrations. Replacing `SystemPrompt` means your host owns the full agent contract.

## Permissions

Use `Unrestricted` only for trusted automation. For most applications, keep the default interactive behavior and respond to `PermissionRequestEvent`.

```csharp
await sdk.SetPermissionModeAsync("interactive");
```

## Plan Mode

Plan mode is a runtime control:

```csharp
await sdk.SetPlanModeAsync(true);
```

See [Plan Mode](./plan-mode.md).
