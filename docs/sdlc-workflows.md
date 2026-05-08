# SDLC Workflows With The C# SDK

These workflows mirror the TypeScript SDK and use the C# SDK as an inspectable orchestration layer around the Autohand CLI.

## Discovery And Planning

Use `examples/20-sdlc-discovery-plan` for ambiguous work. It starts from plan mode and asks the agent to produce a plan without editing files.

```csharp
await sdk.SetPlanModeAsync(true);
```

Ask for:

- scope
- risks
- test strategy
- rollout steps
- explicit non-goals

## Gated Implementation

Use `examples/21-sdlc-gated-implementation` as the model:

1. Generate a plan.
2. Review it outside the agent loop.
3. Disable plan mode.
4. Execute with permission handling.

## Release Readiness

Use `examples/22-sdlc-release-readiness` to ask the agent to run or explain the release gate.

Recommended C# gate:

```bash
dotnet format --verify-no-changes
dotnet build Autohand.CodeAgentSdk.sln
dotnet test tests/Autohand.CodeAgentSdk.Tests/Autohand.CodeAgentSdk.Tests.csproj
./scripts/validate-examples.sh
```

## Host Responsibilities

The host application should:

- keep approval gates outside the model response;
- surface permission requests clearly;
- record what commands were run;
- summarize residual risk;
- keep generated changes reviewable by humans.
