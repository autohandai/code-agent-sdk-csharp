using Autohand.CodeAgentSdk;

await using var agent = await Agent.CreateAsync(new AgentOptions
{
    WorkingDirectory = ".",
    CliPath = Environment.GetEnvironmentVariable("AUTOHAND_CLI_PATH"),
});

var result = await agent.RunJsonAsync<ReleaseRisk>(
    "Assess this SDK repo for release readiness.",
    new JsonRunOptions
    {
        SchemaName = "ReleaseRisk",
        Schema = new
        {
            summary = "string",
            risks = new[] { new { title = "string", severity = "low | medium | high" } },
        },
    });

Console.WriteLine(result.Summary);
foreach (var risk in result.Risks)
{
    Console.WriteLine($"- {risk.Severity}: {risk.Title}");
}

public sealed record ReleaseRisk(string Summary, Risk[] Risks);

public sealed record Risk(string Title, string Severity);

