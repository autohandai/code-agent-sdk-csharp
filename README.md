# Autohand Code Agent SDK for C#

.NET SDK for building applications that control Autohand code agents through the Autohand CLI JSON-RPC mode.

**Documentation:** https://autohand.ai/docs/agent-sdk/

**Beta:** this SDK is actively evolving while the Agent SDK APIs stabilize. Pin versions in production and review release notes before upgrading.

## What It Does

The C# SDK wraps the existing Autohand CLI process and gives .NET applications an async API for agent runs:

```text
.NET app -> Autohand.CodeAgentSdk -> Autohand CLI subprocess -> provider -> model
```

Use it when you want Autohand inside developer tools, build systems, web services, desktop apps, or internal automation without reimplementing the CLI agent protocol.

## Features

- `Agent` and `Run` for high-level application workflows
- `AutohandSdk` for direct low-level RPC access
- `IAsyncEnumerable<SdkEvent>` streaming for tokens, tools, permissions, and errors
- `CancellationToken` support where long-running work can block
- `await using` cleanup for subprocess lifecycle
- `System.Text.Json` for structured output and low-level JSON-RPC escape hatches
- Typed slash commands, persistent goals, and the complete replayable autoresearch ledger
- Typed community skill installation and MCP server/tool/configuration discovery
- Example parity with the TypeScript SDK examples

## Requirements

- .NET 8 or later
- Autohand CLI installed and authenticated
- A configured provider in `~/.autohand/config.json`, or environment variables accepted by the CLI

Set `AUTOHAND_CLI_PATH` when you want to force a local CLI binary:

```bash
export AUTOHAND_CLI_PATH=/path/to/autohand
```

## Installation

The NuGet package name is planned as `Autohand.CodeAgentSdk`:

```bash
dotnet add package Autohand.CodeAgentSdk
```

Until the package is published, reference the project or source repository directly from your solution.

## Quick Start

```csharp
using Autohand.CodeAgentSdk;

await using var agent = await Agent.CreateAsync(new AgentOptions
{
    WorkingDirectory = ".",
    Instructions = "Review code with staff-level C# judgement.",
});

var run = agent.Send("Review this repository for release readiness.");

await foreach (var item in run.StreamAsync())
{
    switch (item)
    {
        case MessageUpdateEvent message:
            Console.Write(message.Delta);
            break;
        case PermissionRequestEvent permission:
            Console.Error.WriteLine($"permission requested: {permission.Description}");
            break;
    }
}

var result = await run.WaitAsync();
Console.WriteLine($"\nRun {result.Id} finished with {result.Status}");
```

## Structured JSON

```csharp
using Autohand.CodeAgentSdk;

await using var agent = await Agent.CreateAsync(new AgentOptions
{
    WorkingDirectory = ".",
    Instructions = "Prefer concise release-readiness analysis.",
});

var risk = await agent.RunJsonAsync<ReleaseRisk>(
    "Assess this SDK repository for publish readiness. Do not execute commands.",
    new JsonRunOptions
    {
        SchemaName = "ReleaseRisk",
        Schema = new
        {
            summary = "string",
            risks = new[]
            {
                new { title = "string", severity = "low | medium | high", mitigation = "string" },
            },
        },
    });

Console.WriteLine(risk.Summary);

public sealed record ReleaseRisk(string Summary, Risk[] Risks);
public sealed record Risk(string Title, string Severity, string Mitigation);
```

## Low-Level Control

Use `AutohandSdk` when your host needs direct access to the JSON-RPC control surface:

```csharp
using Autohand.CodeAgentSdk;

await using var sdk = new AutohandSdk(new AutohandOptions
{
    WorkingDirectory = ".",
    Debug = true,
    RequestTimeout = TimeSpan.FromMinutes(5),
});

await sdk.StartAsync();
await sdk.SetPlanModeAsync(true);

await foreach (var item in sdk.StreamPromptAsync("Create a discovery plan for this SDK change."))
{
    Console.WriteLine(item.Type);
}
```

## Replayable Autoresearch

The .NET API mirrors the TypeScript v1.0.3 autoresearch contract with typed
parameters and top-level results:

```csharp
var started = await agent.StartAutoresearchAsync(new AutoresearchStartParams(
    "Reduce test runtime without regressions")
{
    MetricName = "total_ms",
    MetricUnit = "ms",
    Direction = "lower",
    MeasureCommand = "dotnet test",
    MaxIterations = 12,
    Sampling = new AutoresearchSamplingOptions { MinSamples = 3, MaxSamples = 7 },
});

var history = await agent.GetAutoresearchHistoryAsync();
var pareto = await agent.GetAutoresearchParetoAsync();
var preview = await agent.PruneAutoresearchAsync(new AutoresearchPruneParams { DryRun = true });
await agent.StopAutoresearchAsync();
```

See [Replayable Autoresearch](./docs/autoresearch.md) for replay, rescoring,
comparison, Pareto analysis, pinning, and retention safety.

## Community Skills and MCP

```csharp
var skills = await agent.GetSkillsRegistryAsync();
await agent.InstallSkillAsync(
    new InstallSkillParams("csharp-quality", SkillInstallScope.Project));

var servers = await agent.ListMcpServersAsync();
var tools = await agent.ListMcpToolsAsync(new McpListToolsParams("github"));
var configs = await agent.GetMcpServerConfigsAsync();
```

See [Community Skills and MCP Discovery](./docs/skills-and-mcp.md) for the full
typed contracts.

## Session and Autonomous Control

- `ResetAsync()` clears the conversation and returns the new session ID.
- `CreateBrowserHandoffAsync()` creates a typed one-time browser handoff.
- `AttachBrowserHandoffAsync()` consumes a handoff token and attaches its session.
- `AttachLatestBrowserHandoffAsync()` attaches the newest unexpired handoff.
- `StartAutoModeAsync()` starts a typed autonomous run and returns on acceptance.
- `GetAutoModeStatusAsync()` reports runtime flags and typed persisted state.
- `PauseAutoModeAsync()` pauses the active autonomous run.
- `ResumeAutoModeAsync()` resumes a paused autonomous run.
- `CancelAutoModeAsync()` cancels the active run with an optional reason.
- `GetAutoModeLogAsync()` returns typed iteration entries with an optional limit.

## Examples

The `examples/` directory mirrors the TypeScript SDK example inventory:

- `01-hello-agent`
- `02-streaming-query`
- `03-code-reviewer`
- `04-bash-command`
- `05-file-editor`
- `06-prompt-skills`
- `07-direct-skills`
- `08-memory-management`
- `10-multi-tool-reasoning`
- `13-permissions`
- `20-sdlc-discovery-plan`
- `21-sdlc-gated-implementation`
- `22-sdlc-release-readiness`
- `23-system-prompts`
- `24-high-level-agent`
- `25-structured-json`
- `27-autoresearch-ledger`
- `basic-agent`
- `basic-usage`
- `loop-strategies`
- `permission-handling`
- `sdk-control-features`
- `streaming`

Run an example with:

```bash
dotnet run --project examples/01-hello-agent/Autohand.Examples.HelloAgent.csproj
```

Live examples require an authenticated Autohand CLI and may ask for tool permissions depending on your CLI configuration.

## Documentation

- [Getting Started](./docs/getting-started.md)
- [API Reference](./docs/API_REFERENCE.md)
- [Configuration](./docs/configuration.md)
- [Event Streaming](./docs/event-streaming.md)
- [Permissions](./docs/permissions.md)
- [Plan Mode](./docs/plan-mode.md)
- [SDLC Workflows](./docs/sdlc-workflows.md)
- [Error Handling](./docs/error-handling.md)
- [Examples](./docs/examples.md)
- [Replayable Autoresearch](./docs/autoresearch.md)
- [Community Skills and MCP Discovery](./docs/skills-and-mcp.md)
- [Startup Performance](./docs/performance.md)
- [Contributing](./CONTRIBUTING.md)
- [Security](./SECURITY.md)

## Development

```bash
dotnet restore
dotnet format --verify-no-changes
dotnet build Autohand.CodeAgentSdk.sln
dotnet test tests/Autohand.CodeAgentSdk.Tests/Autohand.CodeAgentSdk.Tests.csproj
./scripts/validate-examples.sh
dotnet run --project benchmarks/Autohand.CodeAgentSdk.StartupBenchmark/Autohand.CodeAgentSdk.StartupBenchmark.csproj --configuration Release
```

The test suite includes structured-output parsing tests and example inventory checks. The example validator builds every mirrored example project when `dotnet` is available.

## Other SDKs

- [TypeScript](https://github.com/autohandai/code-agent-sdk-typescript)
- [Python](https://github.com/autohandai/code-agent-sdk-python)
- [Go](https://github.com/autohandai/code-agent-sdk-go)
- [Java](https://github.com/autohandai/code-agent-sdk-java)
- [Swift](https://github.com/autohandai/code-agent-sdk-swift)
- [Rust](https://github.com/autohandai/code-agent-sdk-rust)
- [C++](https://github.com/autohandai/code-agent-sdk-cpp)

## Support

- SDK docs: https://autohand.ai/docs/agent-sdk/
- Issues: https://github.com/autohandai/code-agent-sdk-csharp/issues
- Security reports: security@autohand.ai
