using System.Text;
using System.Threading.Channels;

namespace Autohand.CodeAgentSdk;

public sealed class Run
{
    private readonly Channel<SdkEvent> _stream = Channel.CreateUnbounded<SdkEvent>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });
    private readonly List<SdkEvent> _events = new();
    private readonly StringBuilder _text = new();
    private readonly TaskCompletionSource<RunResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _runCancellation = new();
    private readonly object _sync = new();
    private readonly List<AgentStep> _steps = new();
    private string _status = "completed";

    internal Run(AutohandSdk sdk, string prompt, PromptOptions? options)
    {
        Id = $"run_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}_{Guid.NewGuid():N}"[..28];
        _ = Task.Run(() => PumpAsync(sdk, prompt, options, _runCancellation.Token));
    }

    public string Id { get; }

    public async IAsyncEnumerable<SdkEvent> StreamAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _stream.Reader.ReadAllAsync(cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return item;
        }
    }

    public async Task<RunResult> WaitAsync(CancellationToken cancellationToken = default)
    {
        return await _completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<T> JsonAsync<T>(
        JsonRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var result = await WaitAsync(cancellationToken).ConfigureAwait(false);
        return JsonOutput.Parse<T>(result.Text, options);
    }

    public async Task AbortAsync(CancellationToken cancellationToken = default)
    {
        Cancel();
        await _completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal void Cancel() => _runCancellation.Cancel();

    private async Task PumpAsync(
        AutohandSdk sdk,
        string prompt,
        PromptOptions? options,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in sdk.StreamPromptAsync(prompt, options, cancellationToken)
                               .ConfigureAwait(false))
            {
                Record(item);
                await _stream.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
            }

            Complete(_status);
        }
        catch (OperationCanceledException)
        {
            Complete("aborted");
        }
        catch (Exception exception)
        {
            _completion.TrySetException(exception);
            _stream.Writer.TryComplete(exception);
        }
    }

    private void Record(SdkEvent item)
    {
        lock (_sync)
        {
            _events.Add(item);
            switch (item)
            {
                case StepEndEvent step:
                    _steps.Add(step.Step);
                    break;
                case TurnEndEvent end:
                    _status = StatusFromReason(end.Reason);
                    break;
                case AgentEndEvent end when _status == "completed":
                    _status = StatusFromReason(end.Reason);
                    break;
                case MessageUpdateEvent { Delta: { } delta }:
                    _text.Append(delta);
                    break;
                case MessageEndEvent { Content: { } content }:
                    _text.Clear();
                    _text.Append(content);
                    break;
            }
        }
    }

    private void Complete(string status)
    {
        RunResult result;
        lock (_sync)
        {
            result = new RunResult(Id, status, _text.ToString(), _events.ToArray()) { Steps = _steps.AsReadOnly() };
        }

        _completion.TrySetResult(result);
        _stream.Writer.TryComplete();
    }

    private static string StatusFromReason(string? reason) => reason switch
    {
        "stop_condition" => "stopped",
        "aborted" => "aborted",
        _ => "completed",
    };
}

public sealed record RunResult(
    string Id,
    string Status,
    string Text,
    IReadOnlyList<SdkEvent> Events)
{
    public IReadOnlyList<AgentStep> Steps { get; init; } = Array.Empty<AgentStep>();
}
