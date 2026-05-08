using Autohand.CodeAgentSdk;

await using var agent = await Agent.CreateAsync(new AgentOptions
{
    WorkingDirectory = ".",
    CliPath = Environment.GetEnvironmentVariable("AUTOHAND_CLI_PATH"),
});

await foreach (var item in agent.StreamAsync("Explain the shape of this repository."))
{
    switch (item)
    {
        case MessageUpdateEvent message:
            Console.Write(message.Delta);
            break;
        case ToolStartEvent tool:
            Console.WriteLine($"\n[tool] {tool.ToolName}");
            break;
        case PermissionRequestEvent permission:
            Console.WriteLine($"\n[permission] {permission.Tool}: {permission.Description}");
            break;
    }
}

