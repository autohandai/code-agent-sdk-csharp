using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace Autohand.CodeAgentSdk;

/// <summary>
/// Low-level SDK wrapper around the Autohand CLI JSON-RPC mode.
/// </summary>
public sealed class AutohandSdk : IAsyncDisposable
{
    private static readonly JsonSerializerOptions RpcJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
    private readonly ITransport _transport;
    private readonly SemaphoreSlim _promptLock = new(1, 1);

    public AutohandSdk(AutohandOptions? options = null)
    {
        Options = options ?? new AutohandOptions();
        _transport = new Transport(Options);
    }

    internal AutohandSdk(AutohandOptions options, ITransport transport)
    {
        Options = options;
        _transport = transport;
    }

    public AutohandOptions Options { get; }

    public bool IsStarted => _transport.IsStarted;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _transport.StartAsync(cancellationToken).ConfigureAwait(false);
            if (Options.Features is not null)
            {
                await ApplyFlagSettingsAsync(new { features = Options.Features }, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            try
            {
                await _transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Preserve the startup failure; cleanup is best effort.
            }

            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        _transport.StopAsync(cancellationToken);

    public Task<JsonElement> RequestAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default) =>
        _transport.RequestAsync(method, parameters, cancellationToken);

    public async Task<JsonElement> PromptAsync(
        string message,
        PromptOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        JsonElement response = default;
        await foreach (var _ in StreamPromptCoreAsync(message, options, result => response = result, cancellationToken)
                           .ConfigureAwait(false)) { }
        return response;
    }

    public IAsyncEnumerable<SdkEvent> StreamPromptAsync(
        string message, PromptOptions? options = null, CancellationToken cancellationToken = default) =>
        StreamPromptCoreAsync(message, options, null, cancellationToken);

    private async IAsyncEnumerable<SdkEvent> StreamPromptCoreAsync(
        string message, PromptOptions? options, Action<JsonElement>? onResponse,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var conditions = options?.StopWhen.ToArray() ?? Array.Empty<StopCondition>();
        if (conditions.Any(condition => condition is null)) throw new ArgumentException("Stop conditions must not be null", nameof(options));
        await _promptLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var subscription = _transport.SubscribeEvents();
            using var promptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var eventCancellation = new CancellationTokenSource();
            var enumerator = subscription.ReadAllAsync(eventCancellation.Token).GetAsyncEnumerator();
            var requestTask = RequestAsync("autohand.prompt", BuildPromptParameters(message, options), promptCancellation.Token);
            var steps = new List<AgentStep>();
            Task<bool>? moveTask = null;
            Task<StepDecision>? decisionTask = null;
            Exception? conditionFailure = null;
            var terminalSeen = false;
            var requestAcknowledged = false;
            try
            {
                while (!terminalSeen)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (requestTask.IsFaulted || requestTask.IsCanceled) await requestTask.ConfigureAwait(false);
                    moveTask ??= enumerator.MoveNextAsync().AsTask();
                    var pending = new List<Task> { moveTask };
                    if (!requestAcknowledged) pending.Add(requestTask);
                    if (decisionTask is not null) pending.Add(decisionTask);
                    await Task.WhenAny(pending).WaitAsync(Options.RequestTimeout, cancellationToken).ConfigureAwait(false);
                    if (moveTask.IsCompleted)
                    {
                        if (!await moveTask.ConfigureAwait(false))
                            throw new AutohandSdkException("CLI event stream closed before prompt completion");
                        var item = enumerator.Current;
                        moveTask = null;
                        if (item is UnknownEvent { EventType: "autohand.stepEnd" })
                            throw new AutohandSdkException("Malformed autohand.stepEnd notification");
                        if (item is StepEndEvent step)
                        {
                            steps.Add(step.Step);
                            decisionTask = StepControl.EvaluateAsync(step.StepId, conditions,
                                new StopConditionContext(steps), promptCancellation.Token);
                        }
                        terminalSeen = StepControl.IsTerminal(item);
                        yield return item;
                    }
                    else if (decisionTask is { IsCompleted: true })
                    {
                        var decision = await decisionTask.ConfigureAwait(false);
                        decisionTask = null;
                        var result = await RequestAsync("autohand.stepDecision",
                            new { stepId = decision.StepId, stop = decision.Stop }, promptCancellation.Token).ConfigureAwait(false);
                        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("success", out var success)
                            || success.ValueKind != JsonValueKind.True)
                            throw new AutohandSdkException("Invalid or rejected autohand.stepDecision result");
                        conditionFailure = decision.Failure;
                    }
                    else
                    {
                        await requestTask.ConfigureAwait(false);
                        requestAcknowledged = true;
                    }
                }
                var response = await requestTask.ConfigureAwait(false);
                onResponse?.Invoke(response);
            }
            finally
            {
                promptCancellation.Cancel();
                if (!terminalSeen && _transport.IsStarted)
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try
                    {
                        await InterruptAsync(cleanup.Token).ConfigureAwait(false);
                        while (true)
                        {
                            moveTask ??= enumerator.MoveNextAsync().AsTask();
                            if (!await moveTask.WaitAsync(cleanup.Token).ConfigureAwait(false))
                                throw new AutohandSdkException("CLI event stream closed during abort cleanup");
                            moveTask = null;
                            if (StepControl.IsTerminal(enumerator.Current)) break;
                        }
                    }
                    catch (Exception)
                    {
                        await _transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                eventCancellation.Cancel();
                if (moveTask is not null) await ObserveAsync(moveTask).ConfigureAwait(false);
                await ObserveAsync(enumerator.DisposeAsync().AsTask()).ConfigureAwait(false);
                await ObserveAsync(requestTask).ConfigureAwait(false);
                if (decisionTask is not null) await ObserveAsync(decisionTask).ConfigureAwait(false);
            }
            if (conditionFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(conditionFailure).Throw();
        }
        finally { _promptLock.Release(); }
    }

    private static async Task ObserveAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception) { /* The main loop already reports failure; cleanup observes remaining work. */ }
    }

    public async IAsyncEnumerable<SdkEvent> EventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _transport.EventsAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    public Task<JsonElement> InterruptAsync(CancellationToken cancellationToken = default) =>
        RequestAsync("autohand.abort", new { }, cancellationToken);

    public Task<JsonElement> SetPermissionModeAsync(
        string mode,
        CancellationToken cancellationToken = default) =>
        RequestAsync("autohand.permissionModeSet", new { mode }, cancellationToken);

    public Task<JsonElement> SetPlanModeAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        RequestAsync("autohand.planModeSet", new { enabled }, cancellationToken);

    public Task<JsonElement> SetModelAsync(
        string? model,
        CancellationToken cancellationToken = default) =>
        RequestAsync("autohand.modelSet", new { model }, cancellationToken);

    public Task<JsonElement> GetStateAsync(CancellationToken cancellationToken = default) =>
        RequestAsync("autohand.getState", new { }, cancellationToken);

    public Task<JsonElement> GetMessagesAsync(
        int? limit = null,
        string? before = null,
        CancellationToken cancellationToken = default) =>
        RequestAsync("autohand.getMessages", new { limit, before }, cancellationToken);

    public Task<ResetResult> ResetAsync(CancellationToken cancellationToken = default) =>
        RequestTypedAsync<ResetResult>("autohand.reset", new { }, cancellationToken);

    public Task<BrowserHandoffCreateResult> CreateBrowserHandoffAsync(
        BrowserHandoffCreateParams? parameters = null,
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<BrowserHandoffCreateResult>(
            "autohand.browserHandoff.create",
            parameters ?? new BrowserHandoffCreateParams(),
            cancellationToken);

    public Task<BrowserHandoffAttachResult> AttachBrowserHandoffAsync(
        BrowserHandoffAttachParams parameters,
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<BrowserHandoffAttachResult>(
            "autohand.browserHandoff.attach",
            parameters,
            cancellationToken);

    public Task<BrowserHandoffAttachResult> AttachLatestBrowserHandoffAsync(
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<BrowserHandoffAttachResult>(
            "autohand.browserHandoff.attachLatest",
            new { },
            cancellationToken);

    public Task<AutoModeStartResult> StartAutoModeAsync(
        AutoModeStartParams parameters,
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<AutoModeStartResult>(
            "autohand.automode.start",
            parameters,
            cancellationToken);

    public Task<AutoModeStatusResult> GetAutoModeStatusAsync(
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<AutoModeStatusResult>(
            "autohand.automode.status",
            new { },
            cancellationToken);

    public Task<AutoModeOperationResult> PauseAutoModeAsync(
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<AutoModeOperationResult>(
            "autohand.automode.pause",
            new { },
            cancellationToken);

    public Task<AutoModeOperationResult> ResumeAutoModeAsync(
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<AutoModeOperationResult>(
            "autohand.automode.resume",
            new { },
            cancellationToken);

    public Task<AutoModeOperationResult> CancelAutoModeAsync(
        AutoModeCancelParams? parameters = null,
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<AutoModeOperationResult>(
            "autohand.automode.cancel",
            parameters ?? new AutoModeCancelParams(),
            cancellationToken);

    public Task<AutoModeGetLogResult> GetAutoModeLogAsync(
        AutoModeGetLogParams? parameters = null,
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<AutoModeGetLogResult>(
            "autohand.automode.getLog",
            parameters ?? new AutoModeGetLogParams(),
            cancellationToken);

    /// <summary>Return effective subagents, including inline and enabled extension agents.</summary>
    public async Task<IReadOnlyList<AgentInfo>> GetSupportedAgentsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await RequestTypedAsync<SupportedAgentsResult>(
            "autohand.getSupportedAgents", new { }, cancellationToken).ConfigureAwait(false);
        if (result.Agents is null || result.Agents.Any(agent =>
                agent is null || agent.Id is null || agent.Name is null || agent.Description is null ||
                agent.Tools is null || agent.Tools.Any(tool => tool is null) ||
                agent.ExtensionScope is not (null or "user" or "project")))
        {
            throw new JsonException("Invalid agent discovery result.");
        }
        return result.Agents;
    }

    public async Task<IReadOnlyList<string>> GetSupportedCommandsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await RequestAsync("autohand.getSupportedCommands", new { }, cancellationToken)
            .ConfigureAwait(false);
        if (!result.TryGetProperty("commands", out var commands) ||
            commands.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return commands.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .Select(command => command.StartsWith("/", StringComparison.Ordinal) ? command : $"/{command}")
            .ToArray();
    }

    public async Task<bool> SupportsCommandAsync(
        string command,
        CancellationToken cancellationToken = default)
    {
        var normalized = FormatSlashCommand(command);
        var supported = await GetSupportedCommandsAsync(cancellationToken).ConfigureAwait(false);
        return supported.Contains(normalized, StringComparer.Ordinal);
    }

    public IAsyncEnumerable<SdkEvent> StreamCommandAsync(
        string command,
        string? arguments = null,
        PromptOptions? options = null,
        CancellationToken cancellationToken = default) =>
        StreamPromptAsync(FormatSlashCommand(command, arguments), options, cancellationToken);

    public Task<JsonElement> ApplyFlagSettingsAsync(
        object settings,
        CancellationToken cancellationToken = default) =>
        RequestAsync("autohand.applyFlagSettings", new { settings }, cancellationToken);

    public Task<JsonElement> GetGoalAsync(CancellationToken cancellationToken = default) =>
        RequestAsync("autohand.goal.get", new { }, cancellationToken);

    public Task<JsonElement> CreateGoalAsync(
        GoalParams parameters,
        CancellationToken cancellationToken = default) =>
        RequestAsync("autohand.goal.create", parameters, cancellationToken);

    public Task<JsonElement> UpdateGoalAsync(
        GoalParams parameters,
        CancellationToken cancellationToken = default) =>
        RequestAsync("autohand.goal.update", parameters, cancellationToken);

    public Task<JsonElement> UpdateGoalAsync(
        GoalUpdateParams parameters,
        CancellationToken cancellationToken = default) =>
        RequestAsync("autohand.goal.update", parameters, cancellationToken);

    public Task<JsonElement> ClearGoalAsync(CancellationToken cancellationToken = default) =>
        RequestAsync("autohand.goal.clear", new { }, cancellationToken);

    public Task<JsonElement> QueueGoalAsync(
        GoalParams parameters,
        CancellationToken cancellationToken = default) =>
        RequestAsync("autohand.goal.queue", parameters, cancellationToken);

    public Task<JsonElement> StartQueuedGoalAsync(CancellationToken cancellationToken = default) =>
        RequestAsync("autohand.goal.startQueued", new { }, cancellationToken);

    public Task<JsonElement> ListGoalTemplatesAsync(CancellationToken cancellationToken = default) =>
        RequestAsync("autohand.goal.listTemplates", new { }, cancellationToken);

    public Task<AutoresearchStartResult> StartAutoresearchAsync(
        AutoresearchStartParams parameters,
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<AutoresearchStartResult>("autohand.autoresearch.start", parameters, cancellationToken);

    public Task<AutoresearchStatusResult> GetAutoresearchStatusAsync(
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<AutoresearchStatusResult>("autohand.autoresearch.status", new { }, cancellationToken);

    public Task<AutoresearchStopResult> StopAutoresearchAsync(
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<AutoresearchStopResult>("autohand.autoresearch.stop", new { }, cancellationToken);

    public Task<AutoresearchHistoryResult> GetAutoresearchHistoryAsync(
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<AutoresearchHistoryResult>("autohand.autoresearch.history", new { }, cancellationToken);

    public Task<AutoresearchReplayResult> ReplayAutoresearchAsync(
        AutoresearchReplayParams parameters,
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<AutoresearchReplayResult>("autohand.autoresearch.replay", parameters, cancellationToken);

    public Task<AutoresearchRescoreResult> RescoreAutoresearchAsync(
        AutoresearchRescoreParams parameters,
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<AutoresearchRescoreResult>("autohand.autoresearch.rescore", parameters, cancellationToken);

    public Task<AutoresearchCompareResult> CompareAutoresearchAsync(
        AutoresearchCompareParams parameters,
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<AutoresearchCompareResult>("autohand.autoresearch.compare", parameters, cancellationToken);

    public Task<AutoresearchParetoResult> GetAutoresearchParetoAsync(
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<AutoresearchParetoResult>("autohand.autoresearch.pareto", new { }, cancellationToken);

    public Task<AutoresearchPinResult> PinAutoresearchAsync(
        AutoresearchPinParams parameters,
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<AutoresearchPinResult>("autohand.autoresearch.pin", parameters, cancellationToken);

    public Task<AutoresearchPruneResult> PruneAutoresearchAsync(
        AutoresearchPruneParams? parameters = null,
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<AutoresearchPruneResult>(
            "autohand.autoresearch.prune",
            parameters ?? new AutoresearchPruneParams(),
            cancellationToken);

    public Task<GetSkillsRegistryResult> GetSkillsRegistryAsync(
        GetSkillsRegistryParams? parameters = null,
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<GetSkillsRegistryResult>(
            "autohand.getSkillsRegistry",
            parameters ?? new GetSkillsRegistryParams(),
            cancellationToken);

    public Task<InstallSkillResult> InstallSkillAsync(
        InstallSkillParams parameters,
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<InstallSkillResult>("autohand.installSkill", parameters, cancellationToken);

    public Task<McpListServersResult> ListMcpServersAsync(
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<McpListServersResult>("autohand.mcp.listServers", new { }, cancellationToken);

    public Task<McpListToolsResult> ListMcpToolsAsync(
        McpListToolsParams? parameters = null,
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<McpListToolsResult>(
            "autohand.mcp.listTools",
            parameters ?? new McpListToolsParams(),
            cancellationToken);

    public Task<McpGetServerConfigsResult> GetMcpServerConfigsAsync(
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<McpGetServerConfigsResult>(
            "autohand.mcp.getServerConfigs",
            new { },
            cancellationToken);

    public Task<JsonElement> PermissionResponseAsync(
        string requestId,
        string decision,
        string? alternative = null,
        CancellationToken cancellationToken = default) =>
        RequestAsync("autohand.permissionResponse", new { requestId, decision, alternative }, cancellationToken);

    public Task<PermissionAcknowledgementResult> AcknowledgePermissionAsync(
        string requestId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        return RequestTypedAsync<PermissionAcknowledgementResult>(
            "autohand.permissionAcknowledged", new { requestId }, cancellationToken);
    }

    public Task<DirectoryAccessResponseResult> RespondDirectoryAccessAsync(
        string requestId,
        bool granted,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        return RequestTypedAsync<DirectoryAccessResponseResult>(
            "autohand.directoryAccessResponse", new { requestId, granted }, cancellationToken);
    }

    public Task<DirectoryAccessAcknowledgementResult> AcknowledgeDirectoryAccessAsync(
        string requestId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        return RequestTypedAsync<DirectoryAccessAcknowledgementResult>(
            "autohand.directoryAccessAcknowledged", new { requestId }, cancellationToken);
    }

    public Task<ChangesDecisionResult> DecideChangesAsync(
        ChangesDecisionParams parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.BatchId);
        if (parameters.Action == ChangesDecisionAction.AcceptSelected &&
            parameters.SelectedChangeIds is not { Count: > 0 })
        {
            throw new ArgumentException("AcceptSelected requires at least one change ID.", nameof(parameters));
        }

        return RequestTypedAsync<ChangesDecisionResult>(
            "autohand.changesDecision", parameters, cancellationToken);
    }

    public Task<SessionHistoryResult> GetHistoryAsync(
        SessionHistoryParams? parameters = null,
        CancellationToken cancellationToken = default)
    {
        parameters ??= new SessionHistoryParams();
        if (parameters.Page is < 1 || parameters.PageSize is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), "Page values must be positive.");
        }

        return RequestTypedAsync<SessionHistoryResult>(
            "autohand.getHistory", parameters, cancellationToken);
    }

    public async Task<SessionDetailsResult> GetSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var result = await RequestAsync("autohand.getSession", new { sessionId }, cancellationToken)
            .ConfigureAwait(false);
        if (!result.TryGetProperty("success", out var success) || !success.GetBoolean())
        {
            var error = result.TryGetProperty("error", out var errorElement)
                ? errorElement.GetString()
                : null;
            return new SessionDetailsFailure(
                string.IsNullOrWhiteSpace(error) ? "Session could not be loaded." : error);
        }

        var payload = result.Deserialize<SessionDetailsPayload>(RpcJsonOptions)
            ?? throw new AutohandSdkException("autohand.getSession returned an empty successful result.");
        if (string.IsNullOrWhiteSpace(payload.SessionId) ||
            string.IsNullOrWhiteSpace(payload.ProjectName) ||
            string.IsNullOrWhiteSpace(payload.Model) ||
            string.IsNullOrWhiteSpace(payload.Status) ||
            string.IsNullOrWhiteSpace(payload.CreatedAt) ||
            string.IsNullOrWhiteSpace(payload.LastActiveAt) ||
            payload.Messages is null ||
            string.IsNullOrWhiteSpace(payload.WorkspaceRoot))
        {
            throw new AutohandSdkException("autohand.getSession returned an incomplete successful result.");
        }
        return new SessionDetailsSuccess(
            payload.SessionId,
            payload.ProjectName,
            payload.Model,
            payload.MessageCount,
            payload.Status,
            payload.CreatedAt,
            payload.LastActiveAt,
            payload.Summary,
            payload.Messages,
            payload.WorkspaceRoot);
    }

    public Task<SessionAttachmentResult> AttachSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return RequestTypedAsync<SessionAttachmentResult>(
            "autohand.session.attach", new { sessionId }, cancellationToken);
    }

    public Task<YoloModeResult> SetYoloModeAsync(
        YoloModeParams parameters,
        CancellationToken cancellationToken = default) =>
        SetYoloModeCoreAsync("autohand.yoloSet", parameters, cancellationToken);

    /// <summary>Uses the dotted compatibility alias exposed by some CLI versions.</summary>
    public Task<YoloModeResult> SetYoloModeAliasAsync(
        YoloModeParams parameters,
        CancellationToken cancellationToken = default) =>
        SetYoloModeCoreAsync("autohand.yolo.set", parameters, cancellationToken);

    private Task<YoloModeResult> SetYoloModeCoreAsync(
        string method,
        YoloModeParams parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(parameters.Pattern);
        if (parameters.TimeoutSeconds is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), "YOLO timeout must be positive.");
        }

        return RequestTypedAsync<YoloModeResult>(method, parameters, cancellationToken);
    }

    public Task<VscodeMcpToolsResult> SetVscodeMcpToolsAsync(
        VscodeMcpToolsParams parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(parameters.Tools);
        if (parameters.Tools.Any(tool =>
                string.IsNullOrWhiteSpace(tool.Name) || string.IsNullOrWhiteSpace(tool.ServerName)))
        {
            throw new ArgumentException("MCP tool and server names must be non-empty.", nameof(parameters));
        }

        return RequestTypedAsync<VscodeMcpToolsResult>(
            "autohand.mcp.setVscodeTools", parameters, cancellationToken);
    }

    public Task<McpInvocationResponseResult> RespondMcpInvocationAsync(
        McpInvocationResponseParams parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.RequestId);
        if (parameters.Success && parameters.Error is not null)
        {
            throw new ArgumentException("A successful invocation response cannot contain an error.", nameof(parameters));
        }
        if (!parameters.Success && string.IsNullOrWhiteSpace(parameters.Error))
        {
            throw new ArgumentException("A failed invocation response requires an error.", nameof(parameters));
        }

        return RequestTypedAsync<McpInvocationResponseResult>(
            "autohand.mcp.invokeResponse", parameters, cancellationToken);
    }

    public Task<LearnRecommendationResult> RecommendLearnAsync(
        LearnRecommendationParams? parameters = null,
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<LearnRecommendationResult>(
            "autohand.learn.recommend", parameters ?? new LearnRecommendationParams(), cancellationToken);

    public Task<LearnUpdateResult> UpdateLearnAsync(CancellationToken cancellationToken = default) =>
        RequestTypedAsync<LearnUpdateResult>("autohand.learn.update", new { }, cancellationToken);

    public Task<LearnGenerationResult> GenerateLearnAsync(
        LearnGenerationScope scope,
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<LearnGenerationResult>(
            "autohand.learn.generate", new LearnGenerationParams(scope), cancellationToken);

    public Task<ToolsRegistryResult> GetToolsRegistryAsync(
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<ToolsRegistryResult>("autohand.getToolsRegistry", new { }, cancellationToken);

    public Task<ContextCompactionResult> SetContextCompactAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        RequestTypedAsync<ContextCompactionResult>(
            "autohand.setContextCompact", new { enabled }, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _transport.DisposeAsync().ConfigureAwait(false);
        _promptLock.Dispose();
    }

    public static string FormatSlashCommand(string command, string? arguments = null)
    {
        var normalized = command.Trim();
        if (!normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException($"Invalid slash command: {command}", nameof(command));
        }

        var normalizedArguments = arguments?.Trim();
        return string.IsNullOrEmpty(normalizedArguments)
            ? normalized
            : $"{normalized} {normalizedArguments}";
    }

    internal static JsonObject BuildPromptParameters(string message, PromptOptions? options)
    {
        var payload = new JsonObject
        {
            ["message"] = message,
        };

        if (options?.Context is not null)
        {
            payload["context"] = options.Context.DeepClone();
        }

        if (options?.ThinkingLevel is not null)
        {
            payload["thinkingLevel"] = options.ThinkingLevel;
        }

        if (options?.Images.Count > 0)
        {
            var images = new JsonArray();
            foreach (var image in options.Images)
            {
                images.Add(new JsonObject
                {
                    ["data"] = image.Data,
                    ["mimeType"] = image.MimeType,
                });
            }

            payload["images"] = images;
        }

        if (options?.Extra is not null)
        {
            foreach (var (key, value) in options.Extra)
            {
                payload[key] = value?.DeepClone();
            }
        }

        if (options?.StopWhen.Count > 0) payload["stopWhen"] = new JsonObject { ["mode"] = "host" };
        return payload;
    }

    private async Task<T> RequestTypedAsync<T>(
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        var result = await RequestAsync(method, parameters, cancellationToken).ConfigureAwait(false);
        return result.Deserialize<T>(RpcJsonOptions)
            ?? throw new AutohandSdkException($"RPC method {method} returned an empty result.");
    }
}
