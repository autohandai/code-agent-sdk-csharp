# Getting Started

The Autohand Code Agent SDK for C# spawns the Autohand CLI in JSON-RPC mode and exposes .NET APIs for prompts, streaming events, permissions, and structured output.

## Prerequisites

1. Install and authenticate the Autohand CLI.
2. Configure a provider in `~/.autohand/config.json`.
3. Install .NET 8 or later.

Set a custom CLI path when developing locally:

```bash
export AUTOHAND_CLI_PATH=/path/to/autohand
```

## Installation

The NuGet package name is planned as `Autohand.CodeAgentSdk`:

```bash
dotnet add package Autohand.CodeAgentSdk
```

Until the package is published, reference the project or repository directly from your solution.

## Your First Agent

```csharp
using Autohand.CodeAgentSdk;

await using var agent = await Agent.CreateAsync(new AgentOptions
{
    WorkingDirectory = ".",
});

var result = await agent.RunAsync("Summarize this repository.");
Console.WriteLine(result.Text);
```

## Streaming

```csharp
await using var sdk = new AutohandSdk(new AutohandOptions
{
    WorkingDirectory = ".",
});

await sdk.StartAsync();

await foreach (var item in sdk.StreamPromptAsync("Explain the SDK in one paragraph."))
{
    if (item is MessageUpdateEvent message)
    {
        Console.Write(message.Delta);
    }
}
```

## Next Steps

- Read [Configuration](./configuration.md).
- Try [Event Streaming](./event-streaming.md).
- Learn [Permissions](./permissions.md).
- Use [SDLC Workflows](./sdlc-workflows.md) for production changes.
