using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autohand.CodeAgentSdk;
using Xunit;

namespace Autohand.CodeAgentSdk.Tests;

public sealed class ParityTests
{
    [Fact]
    public void CurrentRuntimeOptionsMapToExactCliFlags()
    {
        var transport = new Transport(new AutohandOptions
        {
            Bare = true,
            IdleLogout = false,
            ForkSession = "session-1",
            DisplayLanguage = "en-NZ",
            SystemPromptFile = "SYSTEM.md",
            AppendSystemPromptFile = "EXTRA.md",
            McpConfig = "mcp.json",
            Agents = "agents.json",
            PluginDirectory = ".autohand/plugins",
            PersistSession = true,
            SessionId = "session-2",
            AgentsMdEnable = true,
            AgentsMdCreate = true,
            AgentsMdPath = "AGENTS.md",
            MaxTokens = 40_000,
            SkillSources = ["team"],
            InstallMissingSkills = true,
            Provider = "autohandai",
            ApiKey = "test-key",
            BaseUrl = "https://example.test",
            AutohandAiPlan = "cloud",
        });

        var args = transport.BuildArguments();

        Assert.Contains("--bare", args);
        Assert.Contains("--no-idle-logout", args);
        Assert.Equal("session-1", ValueAfter(args, "--fork"));
        Assert.Equal("en-NZ", ValueAfter(args, "--display-language"));
        Assert.Equal("SYSTEM.md", ValueAfter(args, "--system-prompt-file"));
        Assert.Equal("mcp.json", ValueAfter(args, "--mcp-config"));
        Assert.Equal(".autohand/plugins", ValueAfter(args, "--plugin-dir"));
        Assert.Contains("--persist-session", args);
        Assert.Contains("--agents-md", args);
        Assert.Contains("--agents-md-create", args);
        Assert.Equal("AGENTS.md", ValueAfter(args, "--agents-md-path"));
        Assert.Equal("40000", ValueAfter(args, "--max-tokens"));
        Assert.Equal("team", ValueAfter(args, "--skill-sources"));
        Assert.Contains("--install-missing-skills", args);
        var environment = transport.BuildEnvironmentOverrides();
        Assert.Equal("test-key", environment["AUTOHAND_AI_API_KEY"]);
        Assert.Equal("https://example.test", environment["AUTOHAND_AI_BASE_URL"]);
    }

    [Fact]
    public async Task RoutesGoalAndReplayableAutoresearchMethodsThroughExactRpcContract()
    {
        var transport = new FakeTransport();
        await using var sdk = new AutohandSdk(new AutohandOptions
        {
            Features = new FeatureFlagSettings { SlashGoal = true },
        }, transport);
        await sdk.StartAsync();

        await sdk.CreateGoalAsync(new GoalParams { Objective = "Finish parity", TokenBudget = 20_000 });
        await sdk.GetGoalAsync();
        await sdk.UpdateGoalAsync(new GoalParams { Status = "paused" });
        await sdk.ClearGoalAsync();
        await sdk.QueueGoalAsync(new GoalParams { Objective = "Next goal" });
        await sdk.StartQueuedGoalAsync();
        await sdk.ListGoalTemplatesAsync();

        var started = await sdk.StartAutoresearchAsync(new AutoresearchStartParams("Reduce test runtime")
        {
            MetricName = "total_ms",
            MaxIterations = 12,
            Subagents = new AutoresearchSubagentOptions { IdeaGeneration = true },
            SecondaryObjectives =
            [
                new AutoresearchSecondaryObjective("memory", "mb", "lower"),
            ],
        });
        await sdk.GetAutoresearchStatusAsync();
        await sdk.GetAutoresearchHistoryAsync();
        await sdk.ReplayAutoresearchAsync(new AutoresearchReplayParams("attempt-1", "current"));
        await sdk.RescoreAutoresearchAsync(new AutoresearchRescoreParams { All = true });
        await sdk.CompareAutoresearchAsync(new AutoresearchCompareParams("attempt-1", "attempt-2"));
        await sdk.GetAutoresearchParetoAsync();
        await sdk.PinAutoresearchAsync(new AutoresearchPinParams("attempt-1", true));
        await sdk.PruneAutoresearchAsync(new AutoresearchPruneParams { DryRun = true });
        await sdk.StopAutoresearchAsync();

        Assert.True(started.Success);
        Assert.Equal(
            new[]
            {
                "autohand.applyFlagSettings",
                "autohand.goal.create",
                "autohand.goal.get",
                "autohand.goal.update",
                "autohand.goal.clear",
                "autohand.goal.queue",
                "autohand.goal.startQueued",
                "autohand.goal.listTemplates",
                "autohand.autoresearch.start",
                "autohand.autoresearch.status",
                "autohand.autoresearch.history",
                "autohand.autoresearch.replay",
                "autohand.autoresearch.rescore",
                "autohand.autoresearch.compare",
                "autohand.autoresearch.pareto",
                "autohand.autoresearch.pin",
                "autohand.autoresearch.prune",
                "autohand.autoresearch.stop",
            },
            transport.Calls.Select(call => call.Method));

        var goal = transport.Call("autohand.goal.create").Parameters;
        Assert.Equal(20_000, goal.GetProperty("token_budget").GetInt64());
        var start = transport.Call("autohand.autoresearch.start").Parameters;
        Assert.Equal("total_ms", start.GetProperty("metricName").GetString());
        Assert.True(start.GetProperty("subagents").GetProperty("ideaGeneration").GetBoolean());
        Assert.Equal("memory", start.GetProperty("secondaryObjectives")[0].GetProperty("name").GetString());
    }

    [Fact]
    public void ParsesAutoresearchAndTurnUsageNotificationsAsTypedEvents()
    {
        using var statusDocument = JsonDocument.Parse(
            """{"active":true,"runsLogged":3,"statusText":"active","subcommand":"status"}""");
        var lifecycle = Assert.IsType<AutoresearchEvent>(
            SdkEventParser.Parse("autohand.autoresearch.status", statusDocument.RootElement));
        Assert.Equal("status", lifecycle.Phase);

        using var operationDocument = JsonDocument.Parse(
            """{"operation":"replay","phase":"complete","success":true,"attemptId":"attempt-1"}""");
        var operation = Assert.IsType<AutoresearchEvent>(
            SdkEventParser.Parse("autohand.autoresearch.event", operationDocument.RootElement));
        Assert.Equal("replay", operation.Operation);
        Assert.True(operation.Success);

        using var turnDocument = JsonDocument.Parse(
            """{"tokensUsed":42,"tokensUsageStatus":"actual","durationMs":100,"contextPercent":12.5}""");
        var turn = Assert.IsType<TurnEndEvent>(
            SdkEventParser.Parse("autohand.turnEnd", turnDocument.RootElement));
        Assert.Equal(42, turn.TokensUsed);
        Assert.Equal("actual", turn.TokensUsageStatus);
        Assert.Equal(12.5, turn.ContextPercent);
    }

    [Fact]
    public void FormatsOnlyValidSlashCommands()
    {
        Assert.Equal(
            "/deep-research RPC reliability",
            AutohandSdk.FormatSlashCommand(" /deep-research ", " RPC reliability "));
        Assert.Throws<ArgumentException>(() => AutohandSdk.FormatSlashCommand("deep-research"));
        Assert.Throws<ArgumentException>(() => AutohandSdk.FormatSlashCommand("/deep research"));
    }

    private static string ValueAfter(IReadOnlyList<string> args, string flag) =>
        args[Array.IndexOf(args.ToArray(), flag) + 1];

    private sealed class FakeTransport : ITransport
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public List<RpcCall> Calls { get; } = [];
        public bool IsStarted { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            IsStarted = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsStarted = false;
            return Task.CompletedTask;
        }

        public Task<JsonElement> RequestAsync(
            string method,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            var serialized = JsonSerializer.SerializeToElement(parameters ?? new { }, Options);
            Calls.Add(new RpcCall(method, serialized));
            return Task.FromResult(ResultFor(method));
        }

        public async IAsyncEnumerable<SdkEvent> EventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public bool TryReadEvent(out SdkEvent? item)
        {
            item = null;
            return false;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public RpcCall Call(string method) => Calls.Single(call => call.Method == method);

        private static JsonElement ResultFor(string method)
        {
            var json = method switch
            {
                "autohand.autoresearch.status" =>
                    """{"success":true,"active":true,"statusText":"active","runsLogged":1}""",
                "autohand.autoresearch.history" => """{"success":true,"attempts":[]}""",
                "autohand.autoresearch.rescore" => """{"success":true,"decisions":[]}""",
                "autohand.autoresearch.pareto" => """{"success":true,"attemptIds":[]}""",
                "autohand.autoresearch.pin" =>
                    """{"success":true,"attemptId":"attempt-1","pinned":true}""",
                "autohand.autoresearch.prune" =>
                    """{"success":true,"applied":false,"candidates":[],"bytesFreed":0,"remainingBytes":0}""",
                _ => """{"success":true}""",
            };
            return JsonDocument.Parse(json).RootElement.Clone();
        }
    }

    private sealed record RpcCall(string Method, JsonElement Parameters);
}
