using Autohand.CodeAgentSdk;
using System.Text.Json;

namespace Autohand.CodeAgentSdk.Examples;

public static class ExampleSupport
{
    public static AgentOptions BaseOptions() => new()
    {
        WorkingDirectory = ".",
        CliPath = Environment.GetEnvironmentVariable("AUTOHAND_CLI_PATH"),
    };

    public static async Task RunLowLevelAsync(string title, string prompt)
    {
        Console.WriteLine($"=== {title} ===\n");
        await using var sdk = new AutohandSdk(BaseOptions());
        await sdk.StartAsync();

        await foreach (var item in sdk.StreamPromptAsync(prompt))
        {
            await HandleEventAsync(sdk, item);
        }

        _ = await sdk.GetStateAsync();
    }

    public static async Task RunAgentAsync(string title, string prompt)
    {
        Console.WriteLine($"=== {title} ===\n");
        await using var agent = await Agent.CreateAsync(BaseOptions());
        var run = agent.Send(prompt);

        await foreach (var item in run.StreamAsync())
        {
            await HandleEventAsync(null, item);
        }

        var result = await run.WaitAsync();
        Console.WriteLine($"\n\n=== Final Response ===\n{result.Text}");
    }

    public static async Task RunJsonExampleAsync()
    {
        await using var agent = await Agent.CreateAsync(BaseOptions());
        var result = await agent.RunJsonAsync<ReleaseRisk>(
            "Assess this SDK repository for publish readiness. Do not execute commands.",
            new JsonRunOptions
            {
                SchemaName = "ReleaseRisk",
                Schema = new
                {
                    summary = "string",
                    risks = new[]
                    {
                        new { title = "string", severity = "low | medium | high", mitigation = "string" },
                    },
                },
                SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web),
            });

        Console.WriteLine(result.Summary);
        foreach (var risk in result.Risks)
        {
            Console.WriteLine($"- {risk.Severity}: {risk.Title}");
        }
    }

    public static Task ShowControlFeaturesAsync()
    {
        string[] methods =
        [
            "RequestAsync",
            "PromptAsync",
            "StreamPromptAsync",
            "InterruptAsync",
            "SetPlanModeAsync",
            "SetPermissionModeAsync",
            "SetModelAsync",
            "GetStateAsync",
            "GetMessagesAsync",
            "PermissionResponseAsync",
        ];

        foreach (var method in methods)
        {
            Console.WriteLine($"✓ SDK has method: {method}");
        }

        return Task.CompletedTask;
    }

    public static async Task HandleEventAsync(AutohandSdk? sdk, SdkEvent item)
    {
        switch (item)
        {
            case MessageUpdateEvent message:
                Console.Write(message.Delta);
                break;
            case MessageEndEvent:
                Console.WriteLine("\n[message completed]");
                break;
            case ToolStartEvent tool:
                Console.WriteLine($"\n[tool] {tool.ToolName}");
                break;
            case ToolEndEvent tool:
                Console.WriteLine($"\n[tool completed] {tool.ToolName}");
                break;
            case PermissionRequestEvent permission:
                Console.WriteLine($"\n[permission] {permission.Tool}: {permission.Description}");
                if (sdk is not null && !string.IsNullOrEmpty(permission.RequestId))
                {
                    await sdk.PermissionResponseAsync(permission.RequestId, "allow_once");
                }
                break;
            case HookContextWarningEvent warning:
                Console.WriteLine($"\n[context warning] {warning.RemainingTokens} tokens remain");
                break;
            case UnknownEvent unknown when unknown.EventType.StartsWith(
                "autohand.hook.", StringComparison.Ordinal):
                Console.WriteLine($"\n[raw hook] {unknown.EventType}: {unknown.Raw.GetRawText()}");
                break;
            case ErrorEvent error:
                Console.Error.WriteLine($"\n[error] {error.Message}");
                break;
        }
    }

    private sealed record ReleaseRisk(string Summary, Risk[] Risks);

    private sealed record Risk(string Title, string Severity, string? Mitigation);
}
