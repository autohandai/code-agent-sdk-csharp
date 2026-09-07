using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace Autohand.CodeAgentSdk;

internal interface ITransport : IAsyncDisposable
{
    bool IsStarted { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task<JsonElement> RequestAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default);
    IEventSubscription SubscribeEvents();
    IAsyncEnumerable<SdkEvent> EventsAsync(CancellationToken cancellationToken = default);
}

internal interface IEventSubscription : IAsyncDisposable
{
    IAsyncEnumerable<SdkEvent> ReadAllAsync(CancellationToken cancellationToken = default);
    bool TryRead(out SdkEvent? item);
    int BufferedCount { get; }
}

internal sealed class Transport : ITransport
{
    internal const int EventBacklogCapacity = 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private readonly AutohandOptions _options;
    private readonly ConcurrentDictionary<long, PendingRequest> _pending = new();
    private readonly ConcurrentDictionary<long, EventSubscription> _eventSubscribers = new();
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _stateLock = new();
    private long _nextRequestId;
    private long _nextSubscriberId;
    private long _generation;
    private bool _disposed;
    private bool _usable;
    private Process? _process;
    private CancellationTokenSource? _readerCancellation;
    private Task? _stdoutReader;
    private Task? _stderrReader;

    public Transport(AutohandOptions options)
    {
        _options = options;
    }

    public bool IsStarted
    {
        get
        {
            lock (_stateLock)
            {
                return IsUsableNoLock();
            }
        }
    }

    internal int PendingRequestCount => _pending.Count;

    internal bool IsWriteLocked => _writeLock.CurrentCount == 0;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (IsStarted)
            {
                return;
            }

            await StopCoreAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var startInfo = new ProcessStartInfo
            {
                FileName = _options.CliPath ?? "autohand",
                WorkingDirectory = _options.WorkingDirectory ?? Environment.CurrentDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var argument in BuildArguments())
            {
                startInfo.ArgumentList.Add(argument);
            }

            foreach (var (key, value) in BuildEnvironmentOverrides())
            {
                if (value is not null)
                {
                    startInfo.Environment[key] = value;
                }
            }

            var process = Process.Start(startInfo)
                ?? throw new AutohandSdkException("Failed to start the Autohand CLI process.");
            var readerCancellation = new CancellationTokenSource();
            long generation;
            lock (_stateLock)
            {
                generation = ++_generation;
                _process = process;
                _readerCancellation = readerCancellation;
                _usable = true;
            }

            var readerToken = readerCancellation.Token;
            _stdoutReader = Task.Run(
                () => ReadStdoutAsync(process, generation, readerToken),
                CancellationToken.None);
            _stderrReader = Task.Run(
                () => ReadStderrAsync(process, readerToken),
                CancellationToken.None);

            if (cancellationToken.IsCancellationRequested)
            {
                await StopCoreAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task<JsonElement> RequestAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActiveGeneration(out var process, out var generation))
        {
            throw new TransportNotStartedException();
        }

        var id = Interlocked.Increment(ref _nextRequestId);
        var payload = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
        };

        if (parameters is not null)
        {
            payload["params"] = parameters;
        }

        var line = JsonSerializer.Serialize(payload, JsonOptions);
        var pending = new PendingRequest(method, _options.RequestTimeout, generation);
        if (!_pending.TryAdd(id, pending))
        {
            throw new AutohandSdkException($"Failed to register RPC request {id}.");
        }

        var lockTaken = false;
        try
        {
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            lockTaken = true;
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsGenerationUsable(process, generation))
            {
                throw new AutohandSdkException(
                    "The Autohand transport generation ended before the RPC request could be written.");
            }

            await process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(id, out _);
            if (pending.Task.IsFaulted)
            {
                _ = pending.Task.Exception;
            }

            throw;
        }
        finally
        {
            if (lockTaken)
            {
                _writeLock.Release();
            }
        }

        using var timeout = new CancellationTokenSource(_options.RequestTimeout);
        using var timeoutRegistration = timeout.Token.Register(() =>
        {
            if (_pending.TryRemove(id, out var request))
            {
                request.TrySetException(new RequestTimeoutException(method, request.Timeout));
            }
        });
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            if (_pending.TryRemove(id, out var request))
            {
                request.TrySetCanceled(cancellationToken);
            }
        });

        return await pending.Task.ConfigureAwait(false);
    }

    public IEventSubscription SubscribeEvents()
    {
        lock (_stateLock)
        {
            ThrowIfDisposed();
            if (!IsUsableNoLock())
            {
                throw new TransportNotStartedException();
            }

            var id = ++_nextSubscriberId;
            var subscription = new EventSubscription(
                id,
                _generation,
                RemoveSubscriber);
            if (!_eventSubscribers.TryAdd(id, subscription))
            {
                throw new AutohandSdkException("Failed to subscribe to SDK events.");
            }

            return subscription;
        }
    }

    public async IAsyncEnumerable<SdkEvent> EventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var subscription = SubscribeEvents();
        await foreach (var item in subscription.ReadAllAsync(cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return item;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            await StopCoreAsync().ConfigureAwait(false);
            _disposed = true;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    internal IReadOnlyList<string> BuildArguments()
    {
        var args = new List<string> { "--mode", "rpc" };

        AddFlag(args, _options.Bare, "--bare");
        AddFlag(args, _options.IdleLogout == false, "--no-idle-logout");
        AddFlag(args, _options.Unrestricted, "--unrestricted");
        AddFlag(args, _options.AutoMode, "--auto-mode");
        AddFlag(args, _options.AutoSkill, "--auto-skill");
        AddFlag(args, _options.AutoCommit, "-c");
        AddFlag(args, _options.PersistSession, "--persist-session");
        AddFlag(args, _options.Resume, "--resume");
        AddFlag(args, _options.ContinueSession, "--continue");
        AddFlag(args, _options.AgentsMdCreate, "--agents-md-create");
        AddFlag(args, _options.AgentsMdAutoUpdate, "--agents-md-auto-update");

        if (_options.AgentsMdEnable == true)
        {
            args.Add("--agents-md");
        }
        else if (_options.AgentsMdEnable == false)
        {
            args.Add("--no-agents-md");
        }

        if (_options.ContextCompact == true)
        {
            args.Add("--context-compact");
        }
        else if (_options.ContextCompact == false)
        {
            args.Add("--no-context-compact");
        }

        AddValue(args, "--max-iterations", _options.MaxIterations);
        AddValue(args, "--max-runtime", _options.MaxRuntimeMinutes);
        AddValue(args, "--max-cost", _options.MaxCost);
        AddValue(args, "--session-id", _options.SessionId);
        AddValue(args, "--session-path", _options.SessionPath);
        AddValue(args, "--auto-save-interval", _options.AutoSaveInterval);
        AddValue(args, "--max-tokens", _options.MaxTokens);
        AddValue(args, "--compression-threshold", _options.CompressionThreshold);
        AddValue(args, "--summarization-threshold", _options.SummarizationThreshold);
        AddValue(args, "--agents-md-path", _options.AgentsMdPath);
        AddValue(args, "--model", _options.Model);
        AddValue(args, "--temperature", _options.Temperature);
        AddValue(args, "--sys-prompt", _options.SystemPrompt);
        AddValue(args, "--append-sys-prompt", _options.AppendSystemPrompt);
        AddValue(args, "--fork", _options.ForkSession);
        AddValue(args, "--display-language", _options.DisplayLanguage);
        AddValue(args, "--system-prompt-file", _options.SystemPromptFile);
        AddValue(args, "--append-system-prompt-file", _options.AppendSystemPromptFile);
        AddValue(args, "--mcp-config", _options.McpConfig);
        AddValue(args, "--agents", _options.Agents);
        AddValue(args, "--plugin-dir", _options.PluginDirectory);
        AddValue(args, "--yolo", _options.Yolo);
        AddValue(args, "--yolo-timeout", _options.YoloTimeoutSeconds);

        if (_options.Skills.Count > 0)
        {
            args.Add("--skills");
            args.Add(string.Join(",", _options.Skills));
        }

        if (_options.SkillSources.Count > 0)
        {
            args.Add("--skill-sources");
            args.Add(string.Join(",", _options.SkillSources));
        }

        AddFlag(args, _options.InstallMissingSkills, "--install-missing-skills");

        foreach (var directory in _options.AdditionalDirectories)
        {
            args.Add("--add-dir");
            args.Add(directory);
        }

        args.AddRange(_options.ExtraArgs);
        return args;
    }

    internal IReadOnlyDictionary<string, string?> BuildEnvironmentOverrides()
    {
        var environment = new Dictionary<string, string?>(_options.Environment, StringComparer.Ordinal)
        {
            ["AUTOHAND_STREAM_TOOL_OUTPUT"] = "1",
        };
        if (string.Equals(_options.Provider, "autohandai", StringComparison.OrdinalIgnoreCase))
        {
            environment["AUTOHAND_AI_PLAN"] = _options.AutohandAiPlan ?? "cloud";
            environment["AUTOHAND_AI_API_KEY"] = _options.ApiKey;
            environment["AUTOHAND_AI_BASE_URL"] = _options.BaseUrl;
        }

        if (_options.Provider is not null)
        {
            environment["AUTOHAND_PROVIDER"] = _options.Provider;
        }

        return environment;
    }

    private static void AddFlag(List<string> args, bool enabled, string flag)
    {
        if (enabled)
        {
            args.Add(flag);
        }
    }

    private static void AddValue<T>(List<string> args, string flag, T? value)
    {
        if (value is null)
        {
            return;
        }

        args.Add(flag);
        args.Add(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
    }

    private async Task ReadStdoutAsync(
        Process process,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                HandleLine(line, process, generation);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            FailGeneration(
                process,
                generation,
                new AutohandSdkException("Failed while reading Autohand CLI stdout.", exception));
            return;
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            FailGeneration(
                process,
                generation,
                new AutohandSdkException(
                    "The Autohand CLI stdout stream closed before the RPC response arrived."));
        }
    }

    private async Task ReadStderrAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (_options.Debug)
                {
                    Console.Error.WriteLine($"[autohand] {line}");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException exception)
        {
            if (_options.Debug)
            {
                Console.Error.WriteLine($"[autohand] stderr reader failed: {exception.Message}");
            }
        }
    }

    private void HandleLine(string line, Process process, long generation)
    {
        if (!IsGenerationUsable(process, generation))
        {
            return;
        }

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;

        if (root.TryGetProperty("id", out var idElement) &&
            TryGetRequestId(idElement, out var id) &&
            _pending.TryRemove(id, out var pending))
        {
            if (root.TryGetProperty("error", out var error))
            {
                pending.TrySetException(CreateRpcException(error));
            }
            else if (root.TryGetProperty("result", out var result))
            {
                pending.TrySetResult(result.Clone());
            }
            else
            {
                pending.TrySetResult(default);
            }

            return;
        }

        if (root.TryGetProperty("method", out var methodElement) &&
            methodElement.GetString() is { } method)
        {
            var parameters = root.TryGetProperty("params", out var paramsElement)
                ? paramsElement
                : default;
            var sdkEvent = SdkEventParser.Parse(method, parameters);
            foreach (var (_, subscriber) in _eventSubscribers)
            {
                if (subscriber.Generation == generation)
                {
                    subscriber.TryWrite(sdkEvent);
                }
            }
        }
    }

    private static bool TryGetRequestId(JsonElement element, out long id)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out id))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String &&
            long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
        {
            return true;
        }

        id = 0;
        return false;
    }

    private static RpcException CreateRpcException(JsonElement error)
    {
        var code = error.TryGetProperty("code", out var codeElement) &&
            codeElement.TryGetInt32(out var parsedCode)
                ? parsedCode
                : 0;
        var message = error.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString() ?? "Unknown RPC error"
            : "Unknown RPC error";
        var data = error.TryGetProperty("data", out var dataElement)
            ? dataElement.Clone()
            : (JsonElement?)null;
        return new RpcException(code, message, data);
    }

    private static async Task ObserveShutdownAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
    }

    private void FailPending(Exception exception, long? generation = null)
    {
        foreach (var (id, candidate) in _pending)
        {
            if ((generation is null || candidate.Generation == generation) &&
                _pending.TryRemove(id, out var pending))
            {
                pending.TrySetException(exception);
            }
        }
    }

    private async Task StopCoreAsync()
    {
        Process? process;
        CancellationTokenSource? readerCancellation;
        Task? stdoutReader;
        Task? stderrReader;
        long generation;
        lock (_stateLock)
        {
            generation = _generation;
            process = _process;
            readerCancellation = _readerCancellation;
            stdoutReader = _stdoutReader;
            stderrReader = _stderrReader;
            _usable = false;
            _process = null;
            _readerCancellation = null;
            _stdoutReader = null;
            _stderrReader = null;
        }

        var stopped = new AutohandSdkException(
            "The Autohand transport stopped before the RPC response arrived.");
        FailPending(stopped, generation);
        CompleteSubscribers(generation);
        readerCancellation?.Cancel();

        try
        {
            if (process is not null && !HasExited(process))
            {
                if (_writeLock.CurrentCount == 0)
                {
                    process.Kill(entireProcessTree: true);
                }
                else
                {
                    try
                    {
                        process.StandardInput.Close();
                    }
                    catch (Exception exception) when (
                        exception is IOException or InvalidOperationException or ObjectDisposedException)
                    {
                    }
                }

                if (!process.WaitForExit(1000))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(1000);
                }
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        await ObserveShutdownAsync(stdoutReader).ConfigureAwait(false);
        await ObserveShutdownAsync(stderrReader).ConfigureAwait(false);
        readerCancellation?.Dispose();
        process?.Dispose();
    }

    private bool TryGetActiveGeneration(
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Process? process,
        out long generation)
    {
        lock (_stateLock)
        {
            if (IsUsableNoLock())
            {
                process = _process!;
                generation = _generation;
                return true;
            }

            process = null;
            generation = 0;
            return false;
        }
    }

    private bool IsGenerationUsable(Process process, long generation)
    {
        lock (_stateLock)
        {
            return _usable &&
                _generation == generation &&
                ReferenceEquals(_process, process) &&
                !HasExited(process);
        }
    }

    private bool IsUsableNoLock() =>
        _usable && _process is not null && !HasExited(_process);

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private void FailGeneration(Process process, long generation, AutohandSdkException exception)
    {
        lock (_stateLock)
        {
            if (_generation != generation || !ReferenceEquals(_process, process))
            {
                return;
            }

            _usable = false;
        }

        FailPending(exception, generation);
        CompleteSubscribers(generation, exception);
        TerminateProcess(process);
    }

    private static void TerminateProcess(Process process)
    {
        try
        {
            if (!HasExited(process))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(1000);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private void CompleteSubscribers(long generation, Exception? exception = null)
    {
        foreach (var (id, candidate) in _eventSubscribers)
        {
            if (candidate.Generation == generation &&
                _eventSubscribers.TryRemove(id, out var subscriber))
            {
                subscriber.Complete(exception);
            }
        }
    }

    private void RemoveSubscriber(long id)
    {
        if (_eventSubscribers.TryRemove(id, out var subscriber))
        {
            subscriber.Complete();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class EventSubscription : IEventSubscription
    {
        private readonly Channel<SdkEvent> _channel = Channel.CreateBounded<SdkEvent>(
            new BoundedChannelOptions(EventBacklogCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest,
            });
        private readonly Action<long> _onDispose;
        private int _disposed;

        public EventSubscription(long id, long generation, Action<long> onDispose)
        {
            Id = id;
            Generation = generation;
            _onDispose = onDispose;
        }

        public long Id { get; }

        public long Generation { get; }

        public int BufferedCount => _channel.Reader.CanCount ? _channel.Reader.Count : 0;

        public async IAsyncEnumerable<SdkEvent> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var item in _channel.Reader.ReadAllAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                yield return item;
            }
        }

        public bool TryRead(
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SdkEvent? item) =>
            _channel.Reader.TryRead(out item);

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _onDispose(Id);
            }

            return ValueTask.CompletedTask;
        }

        internal bool TryWrite(SdkEvent item) => _channel.Writer.TryWrite(item);

        internal void Complete(Exception? exception = null) =>
            _channel.Writer.TryComplete(exception);
    }

    private sealed class PendingRequest
    {
        private readonly TaskCompletionSource<JsonElement> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PendingRequest(string method, TimeSpan timeout, long generation)
        {
            Method = method;
            Timeout = timeout;
            Generation = generation;
        }

        public string Method { get; }
        public TimeSpan Timeout { get; }
        public long Generation { get; }
        public Task<JsonElement> Task => _completion.Task;

        public void TrySetResult(JsonElement result) => _completion.TrySetResult(result);

        public void TrySetException(Exception exception) => _completion.TrySetException(exception);

        public void TrySetCanceled(CancellationToken cancellationToken) =>
            _completion.TrySetCanceled(cancellationToken);
    }
}
