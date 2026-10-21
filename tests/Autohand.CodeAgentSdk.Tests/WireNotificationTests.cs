using System.Text.Json;
using Autohand.CodeAgentSdk;
using Xunit;

namespace Autohand.CodeAgentSdk.Tests;

public sealed class WireNotificationTests
{
    [Fact]
    public void EphemeralModeIsExplicit()
    {
        Assert.DoesNotContain("--ephemeral",new Transport(new AutohandOptions()).BuildArguments());
        Assert.Contains("--ephemeral",new Transport(new AutohandOptions {Ephemeral=true}).BuildArguments());
    }

    [Fact]
    public async Task RejectsObsoleteOptionsBeforeLaunching()
    {
        await using var transport=new Transport(new AutohandOptions {CliPath="/does-not-exist",AutoSaveInterval=30});
        var error=await Assert.ThrowsAsync<AutohandSdkException>(()=>transport.StartAsync());
        Assert.Contains("unsupported by the current CLI",error.Message);
    }

    [Theory]
    [InlineData("autohand.messageUpdate", "message_update", "{\"type\":\"error\",\"delta\":\"kept\",\"future\":false}")]
    [InlineData("autohand.pipe.output", "pipe_output", "{\"type\":\"progress\",\"content\":\"kept\"}")]
    [InlineData("autohand.peerUpdate", "peer_update", "{\"event\":{\"type\":\"peer_joined\"}}")]
    [InlineData("autohand.future", "future", "[1,null,\"value\"]")]
    [InlineData("autohand.future", "future", "null")]
    [InlineData("autohand.mcp.invokeRequest", "autohand.mcp.invokeRequest", "{\"future\":false}")]
    public void MethodIsAuthoritativeAndPayloadIsLossless(string method, string type, string json)
    {
        using var document = JsonDocument.Parse(json);
        var packet = SdkEventParser.Parse(method, document.RootElement);
        Assert.Equal(type, packet.Type);
        Assert.Equal(json, packet.Raw.GetRawText());
        var property = typeof(SdkEvent).GetProperty("Method");
        Assert.NotNull(property);
        Assert.Equal(method, property.GetValue(packet));
    }
}
