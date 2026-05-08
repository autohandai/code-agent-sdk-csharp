using Autohand.CodeAgentSdk;
using Autohand.CodeAgentSdk.Examples;

var options = ExampleSupport.BaseOptions() with
{
    MaxIterations = 4,
    MaxRuntimeMinutes = 5,
};

await using var agent = await Agent.CreateAsync(options);
var result = await agent.RunAsync("Compare short bounded loops with open-ended agent loops for SDK users.");
Console.WriteLine(result.Text);
