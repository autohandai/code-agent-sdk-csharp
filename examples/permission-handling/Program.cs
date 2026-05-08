using Autohand.CodeAgentSdk;
using Autohand.CodeAgentSdk.Examples;

await using var sdk = new AutohandSdk(ExampleSupport.BaseOptions());
await sdk.StartAsync();
await sdk.SetPermissionModeAsync("ask");

await foreach (var item in sdk.StreamPromptAsync("If you need to inspect a file, request permission and keep the action narrow."))
{
    await ExampleSupport.HandleEventAsync(sdk, item);
}
