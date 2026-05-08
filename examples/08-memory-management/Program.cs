using Autohand.CodeAgentSdk;
using Autohand.CodeAgentSdk.Examples;

var options = ExampleSupport.BaseOptions() with
{
    AppendSystemPrompt = "Keep project context compact. Prefer facts that affect the next implementation decision.",
    ContextCompact = true,
};

await using var agent = await Agent.CreateAsync(options);
var result = await agent.RunAsync("Summarize what should be remembered between SDK example runs.");
Console.WriteLine(result.Text);
