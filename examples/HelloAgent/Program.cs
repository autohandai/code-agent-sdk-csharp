using Autohand.CodeAgentSdk;

await using var agent = await Agent.CreateAsync(new AgentOptions
{
    WorkingDirectory = ".",
    CliPath = Environment.GetEnvironmentVariable("AUTOHAND_CLI_PATH"),
    Instructions = "Be concise and practical for C# developers.",
});

var result = await agent.RunAsync("Hello, Autohand. Summarize what this SDK can do.");
Console.WriteLine(result.Text);

