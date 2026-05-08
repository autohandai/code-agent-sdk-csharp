using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace Autohand.CodeAgentSdk;

/// <summary>
/// Low-level SDK wrapper around the Autohand CLI JSON-RPC mode.
/// </summary>
public sealed class AutohandSdk : IAsyncDisposable
{
    private readonly Transport _transport;

    public AutohandSdk(AutohandOptions? options = null)
    {
        Options = options ?? new AutohandOptions();
        _transport = new Transport(Options);
    }

    public AutohandOptions Options { get; }

    public bool IsStarted => _transport.IsStarted;

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _transport.StartAsync(cancellationToken);

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
        var requestTask = PromptAsync(message, options, cancellationToken);
        var enumerator = _transport.EventsAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);

        try
        {
            while (!requestTask.IsCompleted)
            {
                var moveTask = enumerator.MoveNextAsync().AsTask();
                var completed = await Task.WhenAny(moveTask, requestTask).ConfigureAwait(false);
                if (completed == requestTask)
                {
                    break;
                }

                if (await moveTask.ConfigureAwait(false))
                {
                    yield return enumerator.Current;
                }
            }

            await requestTask.ConfigureAwait(false);
            while (_transport.TryReadEvent(out var buffered))
            {
                yield return buffered;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
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

    public Task<JsonElement> PermissionResponseAsync(
        string requestId,
        string decision,
        string? alternative = null,
        CancellationToken cancellationToken = default) =>
        RequestAsync("autohand.permissionResponse", new { requestId, decision, alternative }, cancellationToken);

    public ValueTask DisposeAsync() => _transport.DisposeAsync();

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
}
