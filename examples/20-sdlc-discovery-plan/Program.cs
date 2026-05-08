using Autohand.CodeAgentSdk;
using Autohand.CodeAgentSdk.Examples;

await using var agent = await Agent.CreateAsync(ExampleSupport.BaseOptions());
await agent.SetPlanModeAsync(true);

await foreach (var item in agent.StreamAsync("Create a discovery plan for adding a new SDK language binding."))
{
    await ExampleSupport.HandleEventAsync(null, item);
}
