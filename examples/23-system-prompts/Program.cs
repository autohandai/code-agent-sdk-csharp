using Autohand.CodeAgentSdk;
using Autohand.CodeAgentSdk.Examples;

var options = ExampleSupport.BaseOptions() with
{
    Instructions = "You are an SDK maintainer. Prefer small, composable APIs and examples that teach one idea at a time.",
};

await using var agent = await Agent.CreateAsync(options);
var result = await agent.RunAsync("Explain the public C# API in three concise bullets.");
Console.WriteLine(result.Text);
