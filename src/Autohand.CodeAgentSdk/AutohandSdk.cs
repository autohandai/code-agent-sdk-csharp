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
        return await RequestAsync("autohand.prompt", BuildPromptParameters(message, options), cancellationToken)
            .ConfigureAwait(false);
    }

    public async IAsyncEnumerable<SdkEvent> StreamPromptAsync(
        string message,
        PromptOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await _promptLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var subscription = _transport.SubscribeEvents();
            using var promptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var eventCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var enumerator = subscription.ReadAllAsync(eventCancellation.Token)
                .GetAsyncEnumerator(eventCancellation.Token);
            var requestTask = PromptAsync(message, options, promptCancellation.Token);
            Task<bool>? moveTask = null;

            try
            {
                while (true)
                {
                    moveTask ??= enumerator.MoveNextAsync().AsTask();
                    var completed = await Task.WhenAny(moveTask, requestTask).ConfigureAwait(false);
                    if (completed == moveTask)
                    {
                        if (await moveTask.ConfigureAwait(false))
                        {
                            var item = enumerator.Current;
                            moveTask = null;
                            yield return item;
                            continue;
                        }

                        await requestTask.ConfigureAwait(false);
                        break;
                    }

                    await requestTask.ConfigureAwait(false);
                    eventCancellation.Cancel();
                    if (await AwaitMoveNextAsync(moveTask, eventCancellation.Token).ConfigureAwait(false))
                    {
                        var item = enumerator.Current;
                        moveTask = null;
                        yield return item;
                    }

                    while (subscription.TryRead(out var buffered) && buffered is not null)
                    {
                        yield return buffered;
                    }

                    break;
                }
            }
            finally
            {
                var canceledForDisposal = !requestTask.IsCompleted || requestTask.IsCanceled;
                eventCancellation.Cancel();
                if (canceledForDisposal)
                {
                    promptCancellation.Cancel();
                }

                if (moveTask is not null)
                {
                    try
                    {
                        await moveTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (eventCancellation.IsCancellationRequested)
                    {
                    }
                }

                try
                {
                    await enumerator.DisposeAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (eventCancellation.IsCancellationRequested)
                {
                }

                try
                {
                    await requestTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (canceledForDisposal)
                {
                }

                if (canceledForDisposal && _transport.IsStarted)
                {
                    using var abortTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try
                    {
                        await InterruptAsync(abortTimeout.Token).ConfigureAwait(false);
                        while (subscription.TryRead(out _))
                        {
                        }
                    }
                    catch (Exception exception) when (
                        exception is OperationCanceledException or AutohandSdkException or IOException)
                    {
                        await _transport.StopAsync(CancellationToken.None).ConfigureAwait(false);
                        throw new AutohandSdkException(
                            "Failed to abort an abandoned prompt; the transport was stopped to prevent event contamination.",
                            exception);
                    }
                }
            }
        }
        finally
        {
            _promptLock.Release();
        }
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

    private static async Task<bool> AwaitMoveNextAsync(
        Task<bool> moveTask,
        CancellationToken cancellationToken)
    {
        try
        {
            return await moveTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
