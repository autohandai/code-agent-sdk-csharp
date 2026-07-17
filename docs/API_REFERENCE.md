# API Reference

## `AutohandOptions`

Configuration used to start the Autohand CLI subprocess.

Common properties:

- `WorkingDirectory`: working directory for the agent.
- `CliPath`: optional Autohand CLI binary path.
- `Debug`: print CLI diagnostic output.
- `RequestTimeout`: JSON-RPC request timeout.
- `Model`: model override passed to the CLI.
- `Skills`: skills passed to the CLI.
- `AppendSystemPrompt`: additional system instructions.
- `Unrestricted`, `AutoMode`, `AutoSkill`, `AutoCommit`: execution mode flags.
- `ContextCompact`: enable or disable context compaction.
- `Yolo`, `YoloTimeoutSeconds`: unattended permission policy.
- current session, AGENTS.md, token/compaction, prompt-file, MCP, agents, plugin,
  bare, idle-logout, fork, and display-language controls.
- `Features`: settings applied through RPC immediately after startup.
- `Provider = "autohandai"` plus API key, base URL, and plan environment wiring.

## `AutohandSdk`

Low-level JSON-RPC wrapper.

Important methods:

- `StartAsync()` / `StopAsync()`
- `RequestAsync(method, parameters)`
- `PromptAsync(message, options)`
- `StreamPromptAsync(message, options)`
- `InterruptAsync()`
- `SetPlanModeAsync(enabled)`
- `SetPermissionModeAsync(mode)`
- `SetModelAsync(model)`
- `GetStateAsync()`
- `GetMessagesAsync()`
- `GetSupportedCommandsAsync()` / `SupportsCommandAsync(command)`
- `StreamCommandAsync(command, arguments, options)`
- `ApplyFlagSettingsAsync(settings)`
- `PermissionResponseAsync(requestId, decision)`

### Persistent goals

- `GetGoalAsync()`
- `CreateGoalAsync(parameters)`
- `UpdateGoalAsync(parameters)`
- `ClearGoalAsync()`
- `QueueGoalAsync(parameters)`
- `StartQueuedGoalAsync()`
- `ListGoalTemplatesAsync()`

`GoalParams` uses the CLI's exact snake-case budget keys.

### Replayable autoresearch

- `StartAutoresearchAsync(parameters)`
- `GetAutoresearchStatusAsync()`
- `StopAutoresearchAsync()`
- `GetAutoresearchHistoryAsync()`
- `ReplayAutoresearchAsync(parameters)`
- `RescoreAutoresearchAsync(parameters)`
- `CompareAutoresearchAsync(parameters)`
- `GetAutoresearchParetoAsync()`
- `PinAutoresearchAsync(parameters)`
- `PruneAutoresearchAsync(parameters)`

See [Replayable Autoresearch](autoresearch.md) for typed parameter records,
evaluation/decision JSON, and safety behavior.

## `Agent`

High-level API for application code.

```csharp
await using var agent = await Agent.CreateAsync(new AgentOptions
{
    WorkingDirectory = ".",
});

var run = agent.Send("Review the public API.");
var result = await run.WaitAsync();
```

Methods:

- `Agent.CreateAsync(options)`
- `Agent.FromSdk(sdk)`
- `Send(prompt, options)`
- `Command(command, arguments, options)`
- `DeepResearch(topic, options)`
- `Autoresearch(objective, options)`
- `RunAsync(prompt, options)`
- `RunJsonAsync<T>(prompt, jsonOptions, promptOptions)`
- `AllowPermissionAsync(requestId)`
- `DenyPermissionAsync(requestId)`
- `SuggestPermissionAlternativeAsync(requestId, alternative)`
- `SetPlanModeAsync(enabled)`
- persistent-goal and typed autoresearch methods matching `AutohandSdk`

## `Run`

Represents a single agent run.

- `StreamAsync()`: stream events.
- `WaitAsync()`: wait until the run finishes and collect text/events.
- `JsonAsync<T>()`: parse final output as JSON.
- `AbortAsync()`: interrupt the current run.

## `SdkEvent`

Typed event records with raw `JsonElement` access.

Common event records:

- `AgentStartEvent`
- `TurnStartEvent`
- `TurnEndEvent` with token, usage-status, duration, and context fields
- `MessageUpdateEvent`
- `MessageEndEvent`
- `ToolStartEvent`
- `ToolUpdateEvent`
- `ToolEndEvent`
- `PermissionRequestEvent`
- `ErrorEvent`
- `AutoresearchEvent`
- `UnknownEvent`

## Structured JSON

```csharp
var risk = await agent.RunJsonAsync<ReleaseRisk>(
    "Assess release readiness.",
    new JsonRunOptions { SchemaName = "ReleaseRisk" });
```
