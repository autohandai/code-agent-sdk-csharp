# Code Agent SDK for C#

.NET SDK for controlling Autohand code agents through the CLI JSON-RPC mode.

**Beta:** this SDK is actively evolving while the Agent SDK APIs stabilize. Pin versions in production and review release notes before upgrading.

## Overview

This SDK provides a C# wrapper around the Autohand CLI binary, enabling programmatic access to Autohand's autonomous coding agent capabilities via JSON-RPC 2.0 over stdin/stdout.

```
User -> C# SDK (thin wrapper) -> CLI Subprocess (existing binary) -> Provider -> HTTP
```

The API is designed for .NET application code:

- `Agent` and `Run` for the high-level workflow
- `IAsyncEnumerable<SdkEvent>` for streaming tokens, tools, and permissions
- `CancellationToken` everywhere long-running work can block
- `await using` cleanup for subprocess lifecycle
- `System.Text.Json` for structured output and low-level RPC escape hatches

## Other Programming Languages (Beta)

The Agent SDK is available in multiple beta language packages. Use the same CLI-backed SDK model from another programming language:

- [TypeScript](https://github.com/autohandai/code-agent-sdk-typescript) - `Agent`, `Run`, streaming, and JSON helpers for Node and Bun hosts.
- [Go](https://github.com/autohandai/code-agent-sdk-go) - idiomatic Go package with `context.Context`, typed events, and channel-based streaming.
- [Python](https://github.com/autohandai/code-agent-sdk-python) - async Python package with `async for` event streams and typed Pydantic models.
- [Java](https://github.com/autohandai/code-agent-sdk-java) - Java 21 records, sealed events, and virtual-thread-ready APIs.
- [Swift](https://github.com/autohandai/code-agent-sdk-swift) - SwiftPM package with `Agent`, `Runner`, async streams, tools, hooks, and permissions.
- [Rust](https://github.com/autohandai/code-agent-sdk-rust) - async Rust crate with Tokio, typed events, and stream-based runs.
- [C++](https://github.com/autohandai/code-agent-sdk-cpp) - modern C++20 package with CMake targets and typed event callbacks.
- [C#](https://github.com/autohandai/code-agent-sdk-csharp) - this package, with `IAsyncEnumerable`, `CancellationToken`, and `System.Text.Json`.

## Requirements

- .NET 8 or later
- Autohand CLI installed and authenticated
- A configured provider in `~/.autohand/config.json`, or environment variables accepted by the CLI

## Installation

The NuGet package name is planned as `Autohand.CodeAgentSdk`:

```bash
dotnet add package Autohand.CodeAgentSdk
```

For local development:

```bash
dotnet restore
dotnet build src/Autohand.CodeAgentSdk/Autohand.CodeAgentSdk.csproj
```

## Quick Start

### High-Level API

Use `Agent` for application code. It gives you an explicit run lifecycle while keeping CLI subprocess and JSON-RPC details out of your app.

```csharp
using Autohand.CodeAgentSdk;

await using var agent = await Agent.CreateAsync(new AgentOptions
{
    WorkingDirectory = ".",
    Instructions = "Review code with staff-level C# judgement.",
});

var run = agent.Send("Review this repository for release readiness");

await foreach (var item in run.StreamAsync())
{
    if (item is MessageUpdateEvent message)
    {
        Console.Write(message.Delta);
    }
}

var result = await run.WaitAsync();
Console.WriteLine(result.Text);
```

For simple one-shot tasks:

```csharp
var result = await agent.RunAsync("Summarize the API surface");
Console.WriteLine(result.Text);
```

For JSON output:

```csharp
public sealed record ReleaseRisk(string Summary, Risk[] Risks);
public sealed record Risk(string Title, string Severity);

var risk = await agent.RunJsonAsync<ReleaseRisk>(
    "Assess publish readiness",
    new JsonRunOptions
    {
        SchemaName = "ReleaseRisk",
        Schema = new
        {
            summary = "string",
            risks = new[] { new { title = "string", severity = "low | medium | high" } },
        },
    });
```

### Low-Level API

Use `AutohandSdk` when you need direct control over the JSON-RPC surface.

```csharp
await using var sdk = new AutohandSdk(new AutohandOptions
{
    WorkingDirectory = ".",
    Debug = true,
    RequestTimeout = TimeSpan.FromMinutes(5),
});

await sdk.StartAsync();

await foreach (var item in sdk.StreamPromptAsync("Analyze the codebase"))
{
    Console.WriteLine(item.Type);
}

var state = await sdk.GetStateAsync();
Console.WriteLine(state);
```

## Development

```bash
dotnet restore
dotnet format --verify-no-changes
dotnet build src/Autohand.CodeAgentSdk/Autohand.CodeAgentSdk.csproj
dotnet test tests/Autohand.CodeAgentSdk.Tests/Autohand.CodeAgentSdk.Tests.csproj
```

## Examples

The `examples/` directory mirrors the TypeScript SDK examples with C# projects for streaming, permissions, structured JSON, plan mode, high-level agents, and SDK control methods.

```bash
./scripts/validate-examples.sh

for project in examples/*/*.csproj; do
  dotnet build "$project"
done
```

Live examples require an authenticated CLI. Set `AUTOHAND_CLI_PATH` only when you want to force a local CLI binary.

## Repository

This repo is intended to live at:

```text
https://github.com/autohandai/code-agent-sdk-csharp
```
