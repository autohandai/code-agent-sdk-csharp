# Startup Performance

The startup gate records three wrapper-controlled metrics:

- `publicImportMs`: a fresh .NET child process times loading the public SDK
  assembly from inside the process, excluding .NET runtime boot.
- `sdkStartReturnMs`: a ready benchmark runtime times the public `StartAsync`
  call against a deterministic native fixture.
- `fixtureSpawnToFirstRpcMs`: the same runtime times fixture spawn through a
  successful `GetStateAsync` response.

Every metric uses 5 warmups and 50 measured samples. The benchmark exits
nonzero if any p95 is not below the 50 ms budget.

```bash
dotnet run --project benchmarks/Autohand.CodeAgentSdk.StartupBenchmark/Autohand.CodeAgentSdk.StartupBenchmark.csproj --configuration Release
```

Measured on 2026-07-20 on the development macOS host:

```json
{
  "language": "csharp",
  "budgetMs": 50,
  "metrics": {
    "publicImportMs": { "samples": 50, "medianMs": 0.996, "p95Ms": 1.309, "maxMs": 1.703, "passed": true },
    "sdkStartReturnMs": { "samples": 50, "medianMs": 10.133, "p95Ms": 12.92, "maxMs": 18.512, "passed": true },
    "fixtureSpawnToFirstRpcMs": { "samples": 50, "medianMs": 11.462, "p95Ms": 17.45, "maxMs": 28.253, "passed": true }
  },
  "passed": true
}
```

The native fixture excludes network and provider latency. A real Autohand CLI
session still depends on local CLI initialization, authentication, provider
availability, and network conditions; this benchmark does not claim those
environment-dependent paths are below 50 ms.
