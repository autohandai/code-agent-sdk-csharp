using Autohand.CodeAgentSdk;
using Autohand.CodeAgentSdk.Examples;

var options = ExampleSupport.BaseOptions() with
{
    Skills = new[] { "csharp", "sdk-development" },
};

await using var sdk = new AutohandSdk(options);
await sdk.StartAsync();

await foreach (var item in sdk.StreamPromptAsync("Use the loaded SDK skills to outline a compact C# API design checklist."))
{
    await ExampleSupport.HandleEventAsync(sdk, item);
}
