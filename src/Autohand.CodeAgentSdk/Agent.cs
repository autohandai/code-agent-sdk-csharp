namespace Autohand.CodeAgentSdk;

/// <summary>
/// High-level agent session for application code.
/// </summary>
public sealed class Agent : IAsyncDisposable
{
    private readonly AutohandSdk _sdk;

    private Agent(AutohandSdk sdk)
    {
        _sdk = sdk;
    }

    public static async Task<Agent> CreateAsync(
        AgentOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeOptions(options ?? new AgentOptions());
        var sdk = new AutohandSdk(normalized);
        await sdk.StartAsync(cancellationToken).ConfigureAwait(false);
        return new Agent(sdk);
    }

    public static Agent FromSdk(AutohandSdk sdk) => new(sdk);

    public Run Send(string prompt, PromptOptions? options = null)
    {
        return new Run(_sdk, prompt, options);
    }

    public Run Command(string command, string? arguments = null, PromptOptions? options = null) =>
        Send(AutohandSdk.FormatSlashCommand(command, arguments), options);

    public Run DeepResearch(string topic, PromptOptions? options = null) =>
        Command("/deep-research", topic, options);

    public Run Autoresearch(string objective, PromptOptions? options = null) =>
        Command("/autoresearch", objective, options);

    public async IAsyncEnumerable<SdkEvent> StreamAsync(
        string prompt,
        PromptOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var run = Send(prompt, options);
        using var cancellation = cancellationToken.Register(run.Cancel);
        var completed = false;
        try
        {
            await foreach (var item in run.StreamAsync(cancellationToken).ConfigureAwait(false)) yield return item;
            completed = true;
        }
        finally
        {
            if (!completed) await run.AbortAsync().ConfigureAwait(false);
        }
    }

    public async Task<RunResult> RunAsync(
        string prompt,
        PromptOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var run = Send(prompt, options);
        using var cancellation = cancellationToken.Register(run.Cancel);
        return await run.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<T> RunJsonAsync<T>(
        string prompt,
        JsonRunOptions? jsonOptions = null,
        PromptOptions? promptOptions = null,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(JsonOutput.WithJsonInstruction(prompt, jsonOptions), promptOptions, cancellationToken)
            .ConfigureAwait(false);
        return JsonOutput.Parse<T>(result.Text, jsonOptions);
    }

    public Task<JsonElement> AllowPermissionAsync(
        string requestId,
        CancellationToken cancellationToken = default) =>
        _sdk.PermissionResponseAsync(requestId, "allow_once", cancellationToken: cancellationToken);

    public Task<JsonElement> DenyPermissionAsync(
        string requestId,
        CancellationToken cancellationToken = default) =>
        _sdk.PermissionResponseAsync(requestId, "deny_once", cancellationToken: cancellationToken);

    public Task<JsonElement> SuggestPermissionAlternativeAsync(
        string requestId,
        string alternative,
        CancellationToken cancellationToken = default) =>
        _sdk.PermissionResponseAsync(requestId, "alternative", alternative, cancellationToken);

    public Task<JsonElement> SetPlanModeAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        _sdk.SetPlanModeAsync(enabled, cancellationToken);

    public Task<IReadOnlyList<AgentInfo>> GetSupportedAgentsAsync(
        CancellationToken cancellationToken = default) =>
        _sdk.GetSupportedAgentsAsync(cancellationToken);

    public Task<IReadOnlyList<string>> GetSupportedCommandsAsync(
        CancellationToken cancellationToken = default) =>
        _sdk.GetSupportedCommandsAsync(cancellationToken);

    public Task<bool> SupportsCommandAsync(
        string command,
        CancellationToken cancellationToken = default) =>
        _sdk.SupportsCommandAsync(command, cancellationToken);

    public Task<JsonElement> GetGoalAsync(CancellationToken cancellationToken = default) =>
        _sdk.GetGoalAsync(cancellationToken);

    public Task<JsonElement> CreateGoalAsync(
        GoalParams parameters,
        CancellationToken cancellationToken = default) =>
        _sdk.CreateGoalAsync(parameters, cancellationToken);

    public Task<JsonElement> UpdateGoalAsync(
        GoalParams parameters,
        CancellationToken cancellationToken = default) =>
        _sdk.UpdateGoalAsync(parameters, cancellationToken);

    public Task<JsonElement> UpdateGoalAsync(
        GoalUpdateParams parameters,
        CancellationToken cancellationToken = default) =>
        _sdk.UpdateGoalAsync(parameters, cancellationToken);

    public Task<JsonElement> ClearGoalAsync(CancellationToken cancellationToken = default) =>
        _sdk.ClearGoalAsync(cancellationToken);

    public Task<JsonElement> QueueGoalAsync(
        GoalParams parameters,
        CancellationToken cancellationToken = default) =>
        _sdk.QueueGoalAsync(parameters, cancellationToken);

    public Task<JsonElement> StartQueuedGoalAsync(CancellationToken cancellationToken = default) =>
        _sdk.StartQueuedGoalAsync(cancellationToken);

    public Task<JsonElement> ListGoalTemplatesAsync(CancellationToken cancellationToken = default) =>
        _sdk.ListGoalTemplatesAsync(cancellationToken);

    public Task<ResetResult> ResetAsync(CancellationToken cancellationToken = default) =>
        _sdk.ResetAsync(cancellationToken);

    public Task<BrowserHandoffCreateResult> CreateBrowserHandoffAsync(
        BrowserHandoffCreateParams? parameters = null,
        CancellationToken cancellationToken = default) =>
        _sdk.CreateBrowserHandoffAsync(parameters, cancellationToken);

    public Task<BrowserHandoffAttachResult> AttachBrowserHandoffAsync(
        BrowserHandoffAttachParams parameters,
        CancellationToken cancellationToken = default) =>
        _sdk.AttachBrowserHandoffAsync(parameters, cancellationToken);

    public Task<BrowserHandoffAttachResult> AttachLatestBrowserHandoffAsync(
        CancellationToken cancellationToken = default) =>
        _sdk.AttachLatestBrowserHandoffAsync(cancellationToken);

    public Task<AutoModeStartResult> StartAutoModeAsync(
        AutoModeStartParams parameters,
        CancellationToken cancellationToken = default) =>
        _sdk.StartAutoModeAsync(parameters, cancellationToken);

    public Task<AutoModeStatusResult> GetAutoModeStatusAsync(
        CancellationToken cancellationToken = default) =>
        _sdk.GetAutoModeStatusAsync(cancellationToken);

    public Task<AutoModeOperationResult> PauseAutoModeAsync(
        CancellationToken cancellationToken = default) =>
        _sdk.PauseAutoModeAsync(cancellationToken);

    public Task<AutoModeOperationResult> ResumeAutoModeAsync(
        CancellationToken cancellationToken = default) =>
        _sdk.ResumeAutoModeAsync(cancellationToken);

    public Task<AutoModeOperationResult> CancelAutoModeAsync(
        AutoModeCancelParams? parameters = null,
        CancellationToken cancellationToken = default) =>
        _sdk.CancelAutoModeAsync(parameters, cancellationToken);

    public Task<AutoModeGetLogResult> GetAutoModeLogAsync(
        AutoModeGetLogParams? parameters = null,
        CancellationToken cancellationToken = default) =>
        _sdk.GetAutoModeLogAsync(parameters, cancellationToken);

    public Task<AutoresearchStartResult> StartAutoresearchAsync(
        AutoresearchStartParams parameters,
        CancellationToken cancellationToken = default) =>
        _sdk.StartAutoresearchAsync(parameters, cancellationToken);

    public Task<AutoresearchStatusResult> GetAutoresearchStatusAsync(
        CancellationToken cancellationToken = default) =>
        _sdk.GetAutoresearchStatusAsync(cancellationToken);

    public Task<AutoresearchStopResult> StopAutoresearchAsync(
        CancellationToken cancellationToken = default) =>
        _sdk.StopAutoresearchAsync(cancellationToken);

    public Task<AutoresearchHistoryResult> GetAutoresearchHistoryAsync(
        CancellationToken cancellationToken = default) =>
        _sdk.GetAutoresearchHistoryAsync(cancellationToken);

    public Task<AutoresearchReplayResult> ReplayAutoresearchAsync(
        AutoresearchReplayParams parameters,
        CancellationToken cancellationToken = default) =>
        _sdk.ReplayAutoresearchAsync(parameters, cancellationToken);

    public Task<AutoresearchRescoreResult> RescoreAutoresearchAsync(
        AutoresearchRescoreParams parameters,
        CancellationToken cancellationToken = default) =>
        _sdk.RescoreAutoresearchAsync(parameters, cancellationToken);

    public Task<AutoresearchCompareResult> CompareAutoresearchAsync(
        AutoresearchCompareParams parameters,
        CancellationToken cancellationToken = default) =>
        _sdk.CompareAutoresearchAsync(parameters, cancellationToken);

    public Task<AutoresearchParetoResult> GetAutoresearchParetoAsync(
        CancellationToken cancellationToken = default) =>
        _sdk.GetAutoresearchParetoAsync(cancellationToken);

    public Task<AutoresearchPinResult> PinAutoresearchAsync(
        AutoresearchPinParams parameters,
        CancellationToken cancellationToken = default) =>
        _sdk.PinAutoresearchAsync(parameters, cancellationToken);

    public Task<AutoresearchPruneResult> PruneAutoresearchAsync(
        AutoresearchPruneParams? parameters = null,
        CancellationToken cancellationToken = default) =>
        _sdk.PruneAutoresearchAsync(parameters, cancellationToken);

    public Task<GetSkillsRegistryResult> GetSkillsRegistryAsync(
        GetSkillsRegistryParams? parameters = null,
        CancellationToken cancellationToken = default) =>
        _sdk.GetSkillsRegistryAsync(parameters, cancellationToken);

    public Task<InstallSkillResult> InstallSkillAsync(
        InstallSkillParams parameters,
        CancellationToken cancellationToken = default) =>
        _sdk.InstallSkillAsync(parameters, cancellationToken);

    public Task<McpListServersResult> ListMcpServersAsync(
        CancellationToken cancellationToken = default) =>
        _sdk.ListMcpServersAsync(cancellationToken);

    public Task<McpListToolsResult> ListMcpToolsAsync(
        McpListToolsParams? parameters = null,
        CancellationToken cancellationToken = default) =>
        _sdk.ListMcpToolsAsync(parameters, cancellationToken);

    public Task<McpGetServerConfigsResult> GetMcpServerConfigsAsync(
        CancellationToken cancellationToken = default) =>
        _sdk.GetMcpServerConfigsAsync(cancellationToken);

    public ValueTask DisposeAsync() => _sdk.DisposeAsync();

    private static AgentOptions NormalizeOptions(AgentOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Instructions))
        {
            return options;
        }

        return options with
        {
            AppendSystemPrompt = AppendPrompt(options.AppendSystemPrompt, options.Instructions),
        };
    }

    private static string AppendPrompt(string? existing, string next)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return next;
        }

        return $"{existing}\n\n{next}";
    }
}
