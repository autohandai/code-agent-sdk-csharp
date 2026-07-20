using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
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
        await sdk.UpdateGoalAsync(new GoalUpdateParams
        {
            Status = "paused",
            TokenBudget = NullableUpdate<long>.Clear(),
            MinTokensBeforeWrapUp = NullableUpdate<long>.Set(500),
        });
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
        var update = transport.Call("autohand.goal.update").Parameters;
        Assert.Equal(JsonValueKind.Null, update.GetProperty("token_budget").ValueKind);
        Assert.Equal(500, update.GetProperty("min_tokens_before_wrap_up").GetInt64());
        Assert.False(update.TryGetProperty("time_budget_seconds", out _));
        var start = transport.Call("autohand.autoresearch.start").Parameters;
        Assert.Equal("total_ms", start.GetProperty("metricName").GetString());
        Assert.True(start.GetProperty("subagents").GetProperty("ideaGeneration").GetBoolean());
        Assert.Equal("memory", start.GetProperty("secondaryObjectives")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task RoutesTypedCommunitySkillsAndMcpDiscoveryMethods()
    {
        var transport = new FakeTransport();
        await using var sdk = new AutohandSdk(new AutohandOptions(), transport);
        await sdk.StartAsync();

        var registry = await sdk.GetSkillsRegistryAsync(new GetSkillsRegistryParams(true));
        var installed = await sdk.InstallSkillAsync(
            new InstallSkillParams("csharp-quality", SkillInstallScope.Project, true));
        var servers = await sdk.ListMcpServersAsync();
        var tools = await sdk.ListMcpToolsAsync(new McpListToolsParams("github"));
        var configs = await sdk.GetMcpServerConfigsAsync();

        Assert.Equal("csharp-quality", registry.Skills.Single().Id);
        Assert.Equal(".agents/skills/csharp-quality", installed.Path);
        Assert.Equal(2, servers.Servers.Single().ToolCount);
        Assert.Equal("github", tools.Tools.Single().ServerName);
        Assert.Equal(McpTransportKind.Stdio, configs.Configs.Single().Transport);
        Assert.Equal("project", transport.Call("autohand.installSkill").Parameters.GetProperty("scope").GetString());
        Assert.True(transport.Call("autohand.getSkillsRegistry").Parameters.GetProperty("forceRefresh").GetBoolean());
        Assert.Equal(
            new[]
            {
                "autohand.getSkillsRegistry",
                "autohand.installSkill",
                "autohand.mcp.listServers",
                "autohand.mcp.listTools",
                "autohand.mcp.getServerConfigs",
            },
            transport.Calls.Select(call => call.Method));
    }

    [Fact]
    public async Task ResetsConversationWithExactEmptyParameters()
    {
        var transport = new FakeTransport();
        await using var sdk = new AutohandSdk(new AutohandOptions(), transport);
        await sdk.StartAsync();
        var agent = Agent.FromSdk(sdk);

        var result = await agent.ResetAsync();

        Assert.Equal("reset-session", result.SessionId);
        Assert.Empty(transport.Call("autohand.reset").Parameters.EnumerateObject());
    }

    [Fact]
    public async Task CreatesBrowserHandoffWithExactCamelCaseParameters()
    {
        var transport = new FakeTransport();
        await using var sdk = new AutohandSdk(new AutohandOptions(), transport);
        await sdk.StartAsync();
        var agent = Agent.FromSdk(sdk);
        var parameters = new BrowserHandoffCreateParams(
            "extension-1",
            "https://example.test/install");

        var result = await agent.CreateBrowserHandoffAsync(parameters);

        Assert.Equal("handoff-token", result.Token);
        Assert.Equal("browser-session", result.SessionId);
        Assert.Equal("https://example.test/handoff", result.Url);
        var call = transport.Call("autohand.browserHandoff.create").Parameters;
        Assert.Equal("extension-1", call.GetProperty("extensionId").GetString());
        Assert.Equal("https://example.test/install", call.GetProperty("installUrl").GetString());
    }

    [Fact]
    public async Task AttachesBrowserHandoffWithExactToken()
    {
        var transport = new FakeTransport();
        await using var sdk = new AutohandSdk(new AutohandOptions(), transport);
        await sdk.StartAsync();
        var agent = Agent.FromSdk(sdk);

        var result = await agent.AttachBrowserHandoffAsync(
            new BrowserHandoffAttachParams("handoff-token"));

        Assert.True(result.Success);
        Assert.Equal("browser-session", result.SessionId);
        Assert.Equal(3, result.MessageCount);
        Assert.Equal(
            "handoff-token",
            transport.Call("autohand.browserHandoff.attach").Parameters.GetProperty("token").GetString());
    }

    [Fact]
    public async Task AttachesLatestBrowserHandoffWithExactEmptyParameters()
    {
        var transport = new FakeTransport();
        await using var sdk = new AutohandSdk(new AutohandOptions(), transport);
        await sdk.StartAsync();
        var agent = Agent.FromSdk(sdk);

        var result = await agent.AttachLatestBrowserHandoffAsync();

        Assert.True(result.Success);
        Assert.Equal("latest-session", result.SessionId);
        Assert.Equal(5, result.MessageCount);
        Assert.Empty(transport.Call("autohand.browserHandoff.attachLatest").Parameters.EnumerateObject());
    }

    [Fact]
    public async Task StartsAutoModeWithCompleteCamelCaseContract()
    {
        var transport = new FakeTransport();
        await using var sdk = new AutohandSdk(new AutohandOptions(), transport);
        await sdk.StartAsync();
        var agent = Agent.FromSdk(sdk);
        var parameters = new AutoModeStartParams("Ship the SDK")
        {
            MaxIterations = 8,
            CompletionPromise = "SHIPPED",
            UseWorktree = false,
            CheckpointInterval = 2,
            MaxRuntime = 45,
            MaxCost = 4.5,
        };

        var result = await agent.StartAutoModeAsync(parameters);

        Assert.True(result.Success);
        Assert.Equal("automode-session", result.SessionId);
        var call = transport.Call("autohand.automode.start").Parameters;
        Assert.Equal("Ship the SDK", call.GetProperty("prompt").GetString());
        Assert.Equal(8, call.GetProperty("maxIterations").GetInt32());
        Assert.Equal("SHIPPED", call.GetProperty("completionPromise").GetString());
        Assert.False(call.GetProperty("useWorktree").GetBoolean());
        Assert.Equal(2, call.GetProperty("checkpointInterval").GetInt32());
        Assert.Equal(45, call.GetProperty("maxRuntime").GetInt32());
        Assert.Equal(4.5, call.GetProperty("maxCost").GetDouble());
    }

    [Fact]
    public async Task GetsTypedAutoModeStatusWithExactEmptyParameters()
    {
        var transport = new FakeTransport();
        await using var sdk = new AutohandSdk(new AutohandOptions(), transport);
        await sdk.StartAsync();
        var agent = Agent.FromSdk(sdk);

        var result = await agent.GetAutoModeStatusAsync();

        Assert.True(result.Active);
        Assert.False(result.Paused);
        Assert.Equal(AutoModeSessionStatus.Running, result.State?.Status);
        Assert.Equal(4, result.State?.CurrentIteration);
        Assert.Equal("checkpoint-1", result.State?.LastCheckpoint?.Commit);
        Assert.Empty(transport.Call("autohand.automode.status").Parameters.EnumerateObject());
    }

    [Fact]
    public async Task PausesAutoModeWithExactEmptyParameters()
    {
        var transport = new FakeTransport();
        await using var sdk = new AutohandSdk(new AutohandOptions(), transport);
        await sdk.StartAsync();
        var agent = Agent.FromSdk(sdk);

        var result = await agent.PauseAutoModeAsync();

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.Empty(transport.Call("autohand.automode.pause").Parameters.EnumerateObject());
    }

    [Fact]
    public async Task ResumesAutoModeWithExactEmptyParameters()
    {
        var transport = new FakeTransport();
        await using var sdk = new AutohandSdk(new AutohandOptions(), transport);
        await sdk.StartAsync();
        var agent = Agent.FromSdk(sdk);

        var result = await agent.ResumeAutoModeAsync();

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.Empty(transport.Call("autohand.automode.resume").Parameters.EnumerateObject());
    }

    [Fact]
    public async Task CancelsAutoModeWithExactOptionalReason()
    {
        var transport = new FakeTransport();
        await using var sdk = new AutohandSdk(new AutohandOptions(), transport);
        await sdk.StartAsync();
        var agent = Agent.FromSdk(sdk);

        var result = await agent.CancelAutoModeAsync(
            new AutoModeCancelParams("release window closed"));

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.Equal(
            "release window closed",
            transport.Call("autohand.automode.cancel").Parameters.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task PermissionAlternativeUsesTheCanonicalDecision()
    {
        var transport = new FakeTransport();
        await using var sdk = new AutohandSdk(new AutohandOptions(), transport);
        await sdk.StartAsync();
        var agent = Agent.FromSdk(sdk);

        await agent.SuggestPermissionAlternativeAsync("permission-1", "Use a read-only command");

        var call = transport.Call("autohand.permissionResponse").Parameters;
        Assert.Equal("alternative", call.GetProperty("decision").GetString());
        Assert.Equal("Use a read-only command", call.GetProperty("alternative").GetString());
    }

    [Fact]
    public void GoalSerializationOmitsUnchangedValuesAndPreservesExplicitClear()
    {
        var legacy = JsonSerializer.SerializeToElement(new GoalParams { Status = "paused" });
        var update = JsonSerializer.SerializeToElement(new GoalUpdateParams
        {
            Status = "paused",
            TokenBudget = NullableUpdate<long>.Clear(),
        });

        Assert.False(legacy.TryGetProperty("token_budget", out _));
        Assert.False(legacy.TryGetProperty("time_budget_seconds", out _));
        Assert.Equal(JsonValueKind.Null, update.GetProperty("token_budget").ValueKind);
        Assert.False(update.TryGetProperty("time_budget_seconds", out _));
    }

    [Fact]
    public async Task StartupFailureStopsThePartiallyStartedTransport()
    {
        var transport = new FakeTransport { ThrowOnMethod = "autohand.applyFlagSettings" };
        await using var sdk = new AutohandSdk(new AutohandOptions
        {
            Features = new FeatureFlagSettings { SlashGoal = true },
        }, transport);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sdk.StartAsync());

        Assert.False(transport.IsStarted);
        Assert.Equal(1, transport.StopCalls);
    }

    [Fact]
    public async Task ConcurrentPromptStreamsAreSerializedAndKeepTheirOwnEvents()
    {
        var transport = new StreamingFakeTransport();
        await using var sdk = new AutohandSdk(new AutohandOptions(), transport);
        await sdk.StartAsync();

        var first = CollectTextAsync(sdk.StreamPromptAsync("first"));
        var second = CollectTextAsync(sdk.StreamPromptAsync("second"));
        var results = await Task.WhenAll(first, second);

        Assert.Equal(new[] { "first", "second" }, results);
        Assert.Equal(1, transport.MaxConcurrentPrompts);
    }

    [Fact]
    public async Task DisposingAStreamEarlyCancelsAndObservesItsPromptRequest()
    {
        var transport = new StreamingFakeTransport();
        await using var sdk = new AutohandSdk(new AutohandOptions(), transport);
        await sdk.StartAsync();

        await foreach (var _ in sdk.StreamPromptAsync("abandoned"))
        {
            break;
        }

        Assert.Equal(1, transport.CanceledPrompts);
        Assert.Equal("next", await CollectTextAsync(sdk.StreamPromptAsync("next")));
        Assert.Equal(1, transport.MaxConcurrentPrompts);
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

    private static async Task<string> CollectTextAsync(IAsyncEnumerable<SdkEvent> events)
    {
        var text = new System.Text.StringBuilder();
        await foreach (var item in events)
        {
            if (item is MessageUpdateEvent { Delta: { } delta })
            {
                text.Append(delta);
            }
        }

        return text.ToString();
    }

    private sealed class FakeTransport : ITransport
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public List<RpcCall> Calls { get; } = [];
        public bool IsStarted { get; private set; }
        public string? ThrowOnMethod { get; init; }
        public int StopCalls { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            IsStarted = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsStarted = false;
            StopCalls++;
            return Task.CompletedTask;
        }

        public Task<JsonElement> RequestAsync(
            string method,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (method == ThrowOnMethod)
            {
                throw new InvalidOperationException("Injected RPC failure.");
            }

            var serialized = JsonSerializer.SerializeToElement(parameters ?? new { }, Options);
            Calls.Add(new RpcCall(method, serialized));
            return Task.FromResult(ResultFor(method));
        }

        public IEventSubscription SubscribeEvents() => TestEventSubscription.Completed();

        public async IAsyncEnumerable<SdkEvent> EventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public RpcCall Call(string method) => Calls.Single(call => call.Method == method);

        private static JsonElement ResultFor(string method)
        {
            var json = method switch
            {
                "autohand.reset" => """{"sessionId":"reset-session"}""",
                "autohand.browserHandoff.create" =>
                    """{"token":"handoff-token","sessionId":"browser-session","workspaceRoot":"/workspace","createdAt":"2026-07-20T00:00:00.000Z","expiresAt":"2026-07-20T00:10:00.000Z","url":"https://example.test/handoff"}""",
                "autohand.browserHandoff.attach" =>
                    """{"success":true,"sessionId":"browser-session","workspaceRoot":"/workspace","messageCount":3}""",
                "autohand.browserHandoff.attachLatest" =>
                    """{"success":true,"sessionId":"latest-session","workspaceRoot":"/workspace","messageCount":5}""",
                "autohand.automode.start" =>
                    """{"success":true,"sessionId":"automode-session"}""",
                "autohand.automode.status" =>
                    """{"active":true,"paused":false,"state":{"sessionId":"automode-session","status":"running","currentIteration":4,"maxIterations":8,"filesCreated":2,"filesModified":7,"branch":"automode/session","lastCheckpoint":{"commit":"checkpoint-1","message":"iteration 3","timestamp":"2026-07-20T00:03:00.000Z"}}}""",
                "autohand.autoresearch.status" =>
                    """{"success":true,"active":true,"statusText":"active","runsLogged":1}""",
                "autohand.autoresearch.history" => """{"success":true,"attempts":[]}""",
                "autohand.autoresearch.rescore" => """{"success":true,"decisions":[]}""",
                "autohand.autoresearch.pareto" => """{"success":true,"attemptIds":[]}""",
                "autohand.autoresearch.pin" =>
                    """{"success":true,"attemptId":"attempt-1","pinned":true}""",
                "autohand.autoresearch.prune" =>
                    """{"success":true,"applied":false,"candidates":[],"bytesFreed":0,"remainingBytes":0}""",
                "autohand.getSkillsRegistry" =>
                    """{"success":true,"skills":[{"id":"csharp-quality","name":"C# Quality","description":"Review C# code","category":"development"}],"categories":[{"name":"development","count":1}]}""",
                "autohand.installSkill" =>
                    """{"success":true,"skillName":"csharp-quality","path":".agents/skills/csharp-quality"}""",
                "autohand.mcp.listServers" =>
                    """{"servers":[{"name":"github","status":"connected","toolCount":2}]}""",
                "autohand.mcp.listTools" =>
                    """{"tools":[{"name":"get_issue","description":"Get an issue","serverName":"github"}]}""",
                "autohand.mcp.getServerConfigs" =>
                    """{"configs":[{"name":"github","transport":"stdio","command":"github-mcp","args":["serve"],"autoConnect":true}]}""",
                _ => """{"success":true}""",
            };
            return JsonDocument.Parse(json).RootElement.Clone();
        }
    }

    private sealed record RpcCall(string Method, JsonElement Parameters);

    private sealed class StreamingFakeTransport : ITransport
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<long, TestEventSubscription>
            _subscribers = new();
        private int _activePrompts;
        private int _canceledPrompts;
        private int _maxConcurrentPrompts;
        private long _nextSubscriberId;

        public bool IsStarted { get; private set; }
        public int MaxConcurrentPrompts => Volatile.Read(ref _maxConcurrentPrompts);
        public int CanceledPrompts => Volatile.Read(ref _canceledPrompts);

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            IsStarted = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsStarted = false;
            foreach (var (_, subscriber) in _subscribers)
            {
                subscriber.Complete();
            }

            _subscribers.Clear();
            return Task.CompletedTask;
        }

        public async Task<JsonElement> RequestAsync(
            string method,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (method != "autohand.prompt")
            {
                return JsonDocument.Parse("""{"success":true}""").RootElement.Clone();
            }

            var active = Interlocked.Increment(ref _activePrompts);
            UpdateMaximum(active);
            try
            {
                var payload = JsonSerializer.SerializeToElement(parameters);
                var message = payload.GetProperty("message").GetString()!;
                foreach (var (_, subscriber) in _subscribers)
                {
                    subscriber.TryWrite(new MessageUpdateEvent(message, default));
                }

                await Task.Delay(25, cancellationToken);
                return JsonDocument.Parse("""{"success":true}""").RootElement.Clone();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Interlocked.Increment(ref _canceledPrompts);
                throw;
            }
            finally
            {
                Interlocked.Decrement(ref _activePrompts);
            }
        }

        public IEventSubscription SubscribeEvents()
        {
            var id = Interlocked.Increment(ref _nextSubscriberId);
            var subscription = new TestEventSubscription(
                () => _subscribers.TryRemove(id, out _));
            _subscribers[id] = subscription;
            return subscription;
        }

        public async IAsyncEnumerable<SdkEvent> EventsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await using var subscription = SubscribeEvents();
            await foreach (var item in subscription.ReadAllAsync(cancellationToken))
            {
                yield return item;
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private void UpdateMaximum(int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maxConcurrentPrompts);
                if (value <= current || Interlocked.CompareExchange(ref _maxConcurrentPrompts, value, current) == current)
                {
                    return;
                }
            }
        }
    }

    private sealed class TestEventSubscription : IEventSubscription
    {
        private readonly Channel<SdkEvent> _events = Channel.CreateUnbounded<SdkEvent>();
        private readonly Action _onDispose;
        private int _disposed;

        public TestEventSubscription(Action onDispose)
        {
            _onDispose = onDispose;
        }

        public int BufferedCount => _events.Reader.Count;

        public static TestEventSubscription Completed()
        {
            var subscription = new TestEventSubscription(static () => { });
            subscription.Complete();
            return subscription;
        }

        public async IAsyncEnumerable<SdkEvent> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var item in _events.Reader.ReadAllAsync(cancellationToken))
            {
                yield return item;
            }
        }

        public bool TryRead(out SdkEvent? item) => _events.Reader.TryRead(out item);

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _onDispose();
                Complete();
            }

            return ValueTask.CompletedTask;
        }

        public bool TryWrite(SdkEvent item) => _events.Writer.TryWrite(item);

        public void Complete() => _events.Writer.TryComplete();
    }
}
