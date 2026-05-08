using Autohand.CodeAgentSdk;
using Autohand.CodeAgentSdk.Examples;

var options = ExampleSupport.BaseOptions() with
{
    AppendSystemPrompt = "When you need shell context, ask for the smallest command that answers the question.",
};

await using var sdk = new AutohandSdk(options);
await sdk.StartAsync();

await foreach (var item in sdk.StreamPromptAsync("Check the current directory name and explain what it tells you."))
{
    await ExampleSupport.HandleEventAsync(sdk, item);
}
