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
- `PermissionResponseAsync(requestId, decision)`

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
- `RunAsync(prompt, options)`
- `RunJsonAsync<T>(prompt, jsonOptions, promptOptions)`
- `AllowPermissionAsync(requestId)`
- `DenyPermissionAsync(requestId)`
- `SuggestPermissionAlternativeAsync(requestId, alternative)`
- `SetPlanModeAsync(enabled)`

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
- `MessageUpdateEvent`
- `MessageEndEvent`
- `ToolStartEvent`
- `ToolUpdateEvent`
- `ToolEndEvent`
- `PermissionRequestEvent`
- `ErrorEvent`
- `UnknownEvent`

## Structured JSON

```csharp
var risk = await agent.RunJsonAsync<ReleaseRisk>(
    "Assess release readiness.",
    new JsonRunOptions { SchemaName = "ReleaseRisk" });
```
