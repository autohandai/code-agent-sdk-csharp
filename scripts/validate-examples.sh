#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
examples_dir="$repo_root/examples"

expected=(
  "01-hello-agent"
  "02-streaming-query"
  "03-code-reviewer"
  "04-bash-command"
  "05-file-editor"
  "06-prompt-skills"
  "07-direct-skills"
  "08-memory-management"
  "10-multi-tool-reasoning"
  "13-permissions"
  "20-sdlc-discovery-plan"
  "21-sdlc-gated-implementation"
  "22-sdlc-release-readiness"
  "23-system-prompts"
  "24-high-level-agent"
  "25-structured-json"
  "27-autoresearch-ledger"
  "28-step-control"
  "basic-agent"
  "basic-usage"
  "loop-strategies"
  "permission-handling"
  "sdk-control-features"
  "streaming"
)

for example in "${expected[@]}"; do
  dir="$examples_dir/$example"
  test -d "$dir" || { echo "missing example directory: $example" >&2; exit 1; }
  test -f "$dir/Program.cs" || { echo "missing Program.cs: $example" >&2; exit 1; }
  project_count="$(find "$dir" -maxdepth 1 -name '*.csproj' | wc -l | tr -d ' ')"
  test "$project_count" = "1" || { echo "expected one csproj in $example, found $project_count" >&2; exit 1; }
  ! grep -Rqi 'TODO' "$dir" || { echo "placeholder TODO left in $example" >&2; exit 1; }
  grep -Eq 'ExampleSupport|AutohandSdk|Agent' "$dir/Program.cs" || {
    echo "example does not exercise SDK API: $example" >&2
    exit 1
  }
done

echo "Validated ${#expected[@]} C# examples."

if command -v dotnet >/dev/null 2>&1; then
  dotnet test "$repo_root/tests/Autohand.CodeAgentSdk.Tests/Autohand.CodeAgentSdk.Tests.csproj"
  for example in "${expected[@]}"; do
    project="$(find "$examples_dir/$example" -maxdepth 1 -name '*.csproj' -print -quit)"
    dotnet build "$project"
  done
else
  echo "dotnet was not found; skipped compile/test execution."
fi
