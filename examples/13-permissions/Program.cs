using Autohand.CodeAgentSdk;
using Autohand.CodeAgentSdk.Examples;

await using var sdk = new AutohandSdk(ExampleSupport.BaseOptions());
await sdk.StartAsync();
await sdk.SetPermissionModeAsync("ask");

await foreach (var item in sdk.StreamPromptAsync("List the files in the current directory if permission is requested."))
{
    await ExampleSupport.HandleEventAsync(sdk, item);
}
