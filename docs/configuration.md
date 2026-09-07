# Configuration

## Process provider selection

Set `AutohandOptions.Provider` to the canonical provider name (for example, `autohandai`).
The SDK forwards it as `AUTOHAND_PROVIDER` after other environment overrides.
With [CLI provider startup support](https://github.com/autohandai/code-cli/commit/240f071013316ebed4fcffb5af68f98cf2f8b2ff), this selects the provider ahead of global and workspace settings.
When no provider is configured or inferred by the SDK, normal CLI environment
and saved configuration selection apply. Older CLIs may ignore the override;
use a CLI containing the linked change.

Autohand AI inference credentials use `AUTOHAND_AI_API_KEY`,
`AUTOHAND_AI_BASE_URL`, and `AUTOHAND_AI_PLAN`. Account authentication is
separate, and configured feature gates still apply. The CLI retains saved
provider settings and credentials when other settings are saved during the run.

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
- `Bare`, `IdleLogout`, `ForkSession`: current long-running runtime controls.
- session, AGENTS.md, token/compaction, skill-source, prompt-file, MCP, agents,
  plugin, and display-language options map to their exact CLI flags.
- `Features`: typed feature settings applied through RPC after startup.

For Autohand AI, set `Provider = "autohandai"`, `ApiKey`, optional `BaseUrl`,
and `AutohandAiPlan`. The SDK maps them to the CLI's `AUTOHAND_AI_*`
environment variables without placing credentials on the process command line.

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
