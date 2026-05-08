# Contributing to the Autohand Code Agent SDK for C#

Thanks for helping improve the C# SDK. This repository is open source and sits beside the public Autohand Code CLI and the other Agent SDK language packages.

## Before You Start

- Read the Agent SDK docs: https://autohand.ai/docs/agent-sdk/
- Search existing issues before opening a new one.
- Keep public API changes small, typed, and ergonomic for .NET hosts.
- Do not commit secrets, provider keys, private logs, or local machine paths.

## Development Setup

```bash
git clone https://github.com/autohandai/code-agent-sdk-csharp.git
cd code-agent-sdk-csharp
dotnet restore
dotnet build Autohand.CodeAgentSdk.sln
dotnet test tests/Autohand.CodeAgentSdk.Tests/Autohand.CodeAgentSdk.Tests.csproj
```

Live examples require an authenticated Autohand CLI. Set `AUTOHAND_CLI_PATH` if you want to test against a local CLI build:

```bash
export AUTOHAND_CLI_PATH=/path/to/autohand
dotnet run --project examples/01-hello-agent/Autohand.Examples.HelloAgent.csproj
```

## Validation

Run the full local gate before opening a pull request:

```bash
dotnet format --verify-no-changes
dotnet build Autohand.CodeAgentSdk.sln
dotnet test tests/Autohand.CodeAgentSdk.Tests/Autohand.CodeAgentSdk.Tests.csproj
./scripts/validate-examples.sh
```

## Pull Requests

Good SDK pull requests usually include:

- A focused API or behavior change.
- Tests for transport, JSON parsing, or configuration behavior.
- Updated examples when public APIs change.
- Updated docs when behavior, setup, or workflows change.

## Commit Style

Use Conventional Commits, following the same style as Autohand Code CLI:

```text
feat: add cancellation helper
fix: preserve final stream events
docs: document plan mode workflow
test: cover permission response flow
```

## Review Principles

We optimize for humans using the API:

- Prefer clear .NET naming and async conventions.
- Keep direct JSON-RPC escape hatches for advanced users.
- Make permissions visible to host applications.
- Keep examples copy-pasteable.
- Keep docs honest about beta status and runtime requirements.

## Community

By participating, you agree to follow the repository [Code of Conduct](./CODE_OF_CONDUCT.md).
