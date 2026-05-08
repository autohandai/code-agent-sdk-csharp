using Autohand.CodeAgentSdk;
using Xunit;

namespace Autohand.CodeAgentSdk.Tests;

public sealed class JsonOutputTests
{
    [Fact]
    public void ParseJsonTextAcceptsDirectJson()
    {
        var json = JsonOutput.ParseJsonText("""{"ok":true}""");

        Assert.Equal("""{"ok":true}""", json);
    }

    [Fact]
    public void ParseJsonTextAcceptsFencedJson()
    {
        var json = JsonOutput.ParseJsonText("""
            Here is the answer:

            ```json
            {"ok":true}
            ```
            """);

        Assert.Equal("""{"ok":true}""", json);
    }

    [Fact]
    public void ParseJsonTextAcceptsEmbeddedJson()
    {
        var json = JsonOutput.ParseJsonText("""Result: {"ok":true} done.""");

        Assert.Equal("""{"ok":true}""", json);
    }

    [Fact]
    public void ParseJsonTextRejectsNonJson()
    {
        Assert.Throws<StructuredOutputException>(() => JsonOutput.ParseJsonText("not json"));
    }
}

