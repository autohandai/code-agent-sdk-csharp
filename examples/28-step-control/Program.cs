using Autohand.CodeAgentSdk;

await using var agent = await Agent.CreateAsync(new AgentOptions
{
    WorkingDirectory = ".",
    Provider = "autohandai",
    Model = "fantail",
});

var result = await agent.RunAsync("Read README.md using read_file", new PromptOptions
{
    StopWhen = [StopConditions.IsStepCount(1)],
});

Console.WriteLine($"{result.Status}: {result.Steps.Count} completed steps");
foreach (var output in result.Steps.SelectMany(step => step.ToolResults))
{
    Console.WriteLine(output.Output ?? output.Error);
}

var continued = await agent.RunAsync("Summarize the saved result.");
Console.WriteLine(continued.Text);
