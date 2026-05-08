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

    public IAsyncEnumerable<SdkEvent> StreamAsync(
        string prompt,
        PromptOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return Send(prompt, options).StreamAsync(cancellationToken);
    }

    public async Task<RunResult> RunAsync(
        string prompt,
        PromptOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return await Send(prompt, options).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<T> RunJsonAsync<T>(
        string prompt,
        JsonRunOptions? jsonOptions = null,
        PromptOptions? promptOptions = null,
        CancellationToken cancellationToken = default)
    {
        var run = Send(JsonOutput.WithJsonInstruction(prompt, jsonOptions), promptOptions);
        var result = await run.WaitAsync(cancellationToken).ConfigureAwait(false);
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
        _sdk.PermissionResponseAsync(requestId, "deny_once", alternative, cancellationToken);

    public Task<JsonElement> SetPlanModeAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        _sdk.SetPlanModeAsync(enabled, cancellationToken);

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

