# Examples

The C# examples mirror the TypeScript SDK examples and are intended to teach one workflow at a time.

Run one with:

```bash
dotnet run --project examples/01-hello-agent/Autohand.Examples.HelloAgent.csproj
```

Examples:

- `01-hello-agent`: first prompt.
- `02-streaming-query`: stream message events.
- `03-code-reviewer`: inspect repository files.
- `04-bash-command`: command-oriented prompt with permissions.
- `05-file-editor`: file-editing workflow.
- `06-prompt-skills`: skill-aware prompting.
- `07-direct-skills`: preconfigured skills.
- `08-memory-management`: memory save and recall pattern.
- `10-multi-tool-reasoning`: inspect, test, and summarize.
- `13-permissions`: explicit permission mode.
- `20-sdlc-discovery-plan`: plan-only discovery.
- `21-sdlc-gated-implementation`: plan then execute.
- `22-sdlc-release-readiness`: release gate.
- `23-system-prompts`: appended system instructions.
- `24-high-level-agent`: `Agent` and `Run`.
- `25-structured-json`: typed JSON output.
- `28-step-control`: stop after persisted tool results, inspect steps, and continue.

Live examples require an authenticated Autohand CLI.
