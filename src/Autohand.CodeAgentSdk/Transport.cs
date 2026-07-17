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
    IAsyncEnumerable<SdkEvent> EventsAsync(CancellationToken cancellationToken = default);
    bool TryReadEvent(out SdkEvent? item);
}

internal sealed class Transport : ITransport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AutohandOptions _options;
    private readonly ConcurrentDictionary<long, PendingRequest> _pending = new();
    private readonly Channel<SdkEvent> _events = Channel.CreateUnbounded<SdkEvent>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private long _nextRequestId;
    private Process? _process;
    private CancellationTokenSource? _readerCancellation;
    private Task? _stdoutReader;
    private Task? _stderrReader;

    public Transport(AutohandOptions options)
    {
        _options = options;
    }

    public bool IsStarted => _process is { HasExited: false };

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsStarted)
        {
            return;
        }

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

        _process = Process.Start(startInfo)
            ?? throw new AutohandSdkException("Failed to start the Autohand CLI process.");

        _readerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _stdoutReader = Task.Run(() => ReadStdoutAsync(_readerCancellation.Token), CancellationToken.None);
        _stderrReader = Task.Run(() => ReadStderrAsync(_readerCancellation.Token), CancellationToken.None);

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _readerCancellation?.Cancel();

        if (_process is { HasExited: false } process)
        {
            process.StandardInput.Close();
            if (!process.WaitForExit(1000))
            {
                process.Kill(entireProcessTree: true);
            }
        }

        if (_stdoutReader is not null)
        {
            await ObserveShutdownAsync(_stdoutReader, cancellationToken).ConfigureAwait(false);
        }

        if (_stderrReader is not null)
        {
            await ObserveShutdownAsync(_stderrReader, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<JsonElement> RequestAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (_process is not { HasExited: false } process)
        {
            throw new TransportNotStartedException();
        }

        var id = Interlocked.Increment(ref _nextRequestId);
        var pending = new PendingRequest(method, _options.RequestTimeout);
        if (!_pending.TryAdd(id, pending))
        {
            throw new AutohandSdkException($"Failed to register RPC request {id}.");
        }

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

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
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

    public async IAsyncEnumerable<SdkEvent> EventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _events.Reader.ReadAllAsync(cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return item;
        }
    }

    public bool TryReadEvent([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SdkEvent? item) =>
        _events.Reader.TryRead(out item);

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _writeLock.Dispose();
        _readerCancellation?.Dispose();
        _process?.Dispose();
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

    private async Task ReadStdoutAsync(CancellationToken cancellationToken)
    {
        if (_process is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await _process.StandardOutput.ReadLineAsync(cancellationToken)
                .ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            HandleLine(line);
        }
    }

    private async Task ReadStderrAsync(CancellationToken cancellationToken)
    {
        if (_process is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await _process.StandardError.ReadLineAsync(cancellationToken)
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

    private void HandleLine(string line)
    {
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
            _events.Writer.TryWrite(SdkEventParser.Parse(method, parameters));
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

    private static async Task ObserveShutdownAsync(Task task, CancellationToken cancellationToken)
    {
        try
        {
            await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
    }

    private sealed class PendingRequest
    {
        private readonly TaskCompletionSource<JsonElement> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PendingRequest(string method, TimeSpan timeout)
        {
            Method = method;
            Timeout = timeout;
        }

        public string Method { get; }
        public TimeSpan Timeout { get; }
        public Task<JsonElement> Task => _completion.Task;

        public void TrySetResult(JsonElement result) => _completion.TrySetResult(result);

        public void TrySetException(Exception exception) => _completion.TrySetException(exception);

        public void TrySetCanceled(CancellationToken cancellationToken) =>
            _completion.TrySetCanceled(cancellationToken);
    }
}
