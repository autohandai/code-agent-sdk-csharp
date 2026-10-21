using Xunit;

namespace Autohand.CodeAgentSdk.Tests;

public sealed class ExampleInventoryTests
{
    private static readonly string[] ExpectedExamples =
    [
        "01-hello-agent",
        "02-streaming-query",
        "03-code-reviewer",
        "04-bash-command",
        "05-file-editor",
        "06-prompt-skills",
        "07-direct-skills",
        "08-memory-management",
        "10-multi-tool-reasoning",
        "13-permissions",
        "20-sdlc-discovery-plan",
        "21-sdlc-gated-implementation",
        "22-sdlc-release-readiness",
        "23-system-prompts",
        "24-high-level-agent",
        "25-structured-json",
        "27-autoresearch-ledger",
        "28-step-control",
        "basic-agent",
        "basic-usage",
        "loop-strategies",
        "permission-handling",
        "sdk-control-features",
        "streaming",
    ];

    [Fact]
    public void CSharpExamplesMirrorTypeScriptInventory()
    {
        var root = FindRepoRoot();

        foreach (var example in ExpectedExamples)
        {
            var directory = Path.Combine(root, "examples", example);
            Assert.True(Directory.Exists(directory), $"Missing example directory: {example}");
            Assert.Single(Directory.GetFiles(directory, "*.csproj"));
            Assert.True(File.Exists(Path.Combine(directory, "Program.cs")), $"Missing Program.cs for {example}");
        }
    }

    [Fact]
    public void CSharpExamplesAreRealSdkPrograms()
    {
        var root = FindRepoRoot();

        foreach (var example in ExpectedExamples)
        {
            var program = File.ReadAllText(Path.Combine(root, "examples", example, "Program.cs"));
            Assert.DoesNotContain("TODO", program, StringComparison.OrdinalIgnoreCase);
            Assert.True(
                program.Contains("ExampleSupport", StringComparison.Ordinal) ||
                program.Contains("AutohandSdk", StringComparison.Ordinal) ||
                program.Contains("Agent", StringComparison.Ordinal),
                $"Example {example} should exercise the SDK API surface.");
        }
    }

    [Fact]
    public void CSharpExampleProjectsShareTheSdkReference()
    {
        var root = FindRepoRoot();

        foreach (var example in ExpectedExamples)
        {
            var project = Directory.GetFiles(Path.Combine(root, "examples", example), "*.csproj").Single();
            var text = File.ReadAllText(project);

            Assert.Contains("Examples.Shared.props", text, StringComparison.Ordinal);
        }
    }

    private static string FindRepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "Autohand.CodeAgentSdk.sln")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Could not locate the C# SDK repository root.");
    }
}
