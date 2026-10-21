using System.Text.Json.Nodes;
using Autohand.CodeAgentSdk;
using Xunit;
namespace Autohand.CodeAgentSdk.Tests;

public sealed class RuntimeSettingsTests
{
    [Theory]
    [InlineData("anthropic", "apiKey", "baseUrl", null, null)]
    [InlineData("openai", "apiKey", "baseUrl", "authMode", "api-key")]
    [InlineData("xai", "apiKey", "baseUrl", "authMode", "api-key")]
    [InlineData("azure", "apiKey", "baseUrl", "authMethod", "api-key")]
    [InlineData("bedrock", "apiKey", "endpoint", "authMode", "bedrock-api-key")]
    [InlineData("vertexai", "authToken", "endpoint", null, null)]
    public void NativeProviderFields(string provider, string key, string endpoint, string? auth, string? mode)
    {
        var result = RuntimeSettings.Build(new() { Provider = provider, ApiKey = "secret", BaseUrl = "https://fixture.test" });
        var section = result[provider]!;
        Assert.Equal("secret", section[key]!.GetValue<string>());
        Assert.Equal(provider == "vertexai" ? "fixture.test" : "https://fixture.test", section[endpoint]!.GetValue<string>());
        if (auth is not null) Assert.Equal(mode, section[auth]!.GetValue<string>());
    }
    [Fact]
    public void DefaultsAndFalseArePreservedWithoutMutatingInput()
    {
        var original = JsonNode.Parse("{\"features\":{\"slashGoal\":true},\"customProviders\":{\"my.provider\":{\"models\":[null,\"one\"]}}}")!.AsObject();
        var result = RuntimeSettings.Build(new() { Provider = "autohandai", Features = new() { SlashGoal = false }, RunSettings = original });
        Assert.Null(result["autohandai"]);
        Assert.False(result["features"]!["slashGoal"]!.GetValue<bool>());
        Assert.True(original["features"]!["slashGoal"]!.GetValue<bool>());
        var transport = new Transport(new() { Provider = "autohandai" });
        Assert.False(transport.BuildEnvironmentOverrides().ContainsKey("AUTOHAND_AI_PLAN"));
    }
    [Fact]
    public void RejectsUnsafeSettingsAndAmbiguousCredentials()
    {
        Assert.Throws<AutohandSdkException>(() => RuntimeSettings.Build(new() { ApiKey = "secret" }));
        Assert.Throws<AutohandSdkException>(() => RuntimeSettings.Build(new() { Provider = "vertexai", BaseUrl = "https://user:secret@fixture.test/v1" }));
        Assert.Throws<AutohandSdkException>(() => RuntimeSettings.Build(new() { RunSettings = JsonNode.Parse("{\"features\":{\"constructor\":true}}")!.AsObject() }));
    }
    [Fact]
    public void TranslatesCurrentSessionStartupArguments()
    {
        Assert.Equal(new[] {"--mode","rpc","--sdk-session-reference","saved"}, new Transport(new() { SessionId="saved", PersistSession=true }).BuildArguments());
        Assert.Equal(new[] {"--mode","rpc","--sdk-resume-last"}, new Transport(new() { ContinueSession=true }).BuildArguments());
        Assert.Equal(new[] {"--mode","rpc","--fork","saved"}, new Transport(new() { ForkSession="saved",SessionId="saved" }).BuildArguments());
    }

}
