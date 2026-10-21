using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Autohand.CodeAgentSdk;
using Xunit;

namespace Autohand.CodeAgentSdk.Tests;

public sealed class TransportLifecycleTests
{
    [Fact]
    public async Task CancelingTheStartupTokenAfterReturnDoesNotStopReaders()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = RpcFixture.Create();
        using var startup = new CancellationTokenSource();
        await using var transport = CreateTransport(fixture);
        await transport.StartAsync(startup.Token);
        startup.Cancel();

        var state = await transport.RequestAsync("autohand.getState");

        Assert.Equal("idle", state.GetProperty("status").GetString());
    }

    [Fact]
    public async Task StopFailsAnInflightRequestAndWakesEventSubscribers()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = RpcFixture.Create();
        await using var transport = CreateTransport(fixture);
        await transport.StartAsync();
        var request = transport.RequestAsync("autohand.test.hang");
        await using var events = transport.EventsAsync().GetAsyncEnumerator();
        var nextEvent = events.MoveNextAsync().AsTask();
        await Task.Delay(50);

        await transport.StopAsync();

        await Assert.ThrowsAnyAsync<AutohandSdkException>(
            async () => await request.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(await nextEvent.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task MalformedStdoutInvalidatesAndTerminatesGenerationThenCanRestart()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = RpcFixture.Create();
        await using var transport = CreateTransport(fixture);
        await transport.StartAsync();
        var original = await transport.RequestAsync("autohand.getState");
        var originalPid = original.GetProperty("pid").GetInt32();

        var exception = await Assert.ThrowsAsync<AutohandSdkException>(
            () => transport.RequestAsync("autohand.test.malformed"));

        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
        Assert.False(transport.IsStarted);
        await WaitUntilAsync(() => !IsProcessAlive(originalPid));
        await Assert.ThrowsAsync<TransportNotStartedException>(
            () => transport.RequestAsync("autohand.getState"));

        await transport.StartAsync();
        var restarted = await transport.RequestAsync("autohand.getState");

        Assert.Equal("idle", restarted.GetProperty("status").GetString());
        Assert.NotEqual(originalPid, restarted.GetProperty("pid").GetInt32());
    }

    [Fact]
    public async Task StdoutClosureWhileChildIsAliveInvalidatesAndTerminatesGeneration()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = RpcFixture.Create();
        await using var transport = CreateTransport(fixture);
        await transport.StartAsync();
        var state = await transport.RequestAsync("autohand.getState");
        var pid = state.GetProperty("pid").GetInt32();

        var exception = await Assert.ThrowsAsync<AutohandSdkException>(
            () => transport.RequestAsync("autohand.test.closeStdout"));

        Assert.Contains("stdout stream closed", exception.Message, StringComparison.Ordinal);
        Assert.False(transport.IsStarted);
        await WaitUntilAsync(() => !IsProcessAlive(pid));
    }

    [Fact]
    public async Task RestartAfterSpontaneousExitUsesOnlyTheNewProcessReaders()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = RpcFixture.Create();
        await using var transport = CreateTransport(fixture);
        await transport.StartAsync();
        await Assert.ThrowsAsync<AutohandSdkException>(
            () => transport.RequestAsync("autohand.test.exit"));

        await transport.StartAsync();
        var state = await transport.RequestAsync("autohand.getState");

        Assert.Equal("idle", state.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CancellationWhileWaitingForWriteLockRemovesPendingRequest()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = RpcFixture.Create();
        await using var transport = CreateTransport(fixture);
        await transport.StartAsync();
        await transport.RequestAsync("autohand.test.pauseReads");
        var blockedWrite = transport.RequestAsync(
            "autohand.test.hang",
            new { payload = new string('x', 4 * 1024 * 1024) });
        await WaitUntilAsync(() => transport.IsWriteLocked);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.RequestAsync("autohand.getState", cancellationToken: cancellation.Token));

        Assert.Equal(1, transport.PendingRequestCount);
        await transport.StopAsync();
        await Assert.ThrowsAnyAsync<Exception>(
            async () => await blockedWrite.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(0, transport.PendingRequestCount);
    }

    [Fact]
    public async Task PublicObserversAndPromptStreamEachReceiveTheSameEvent()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = RpcFixture.Create();
        await using var sdk = new AutohandSdk(new AutohandOptions
        {
            CliPath = fixture.Path,
            RequestTimeout = TimeSpan.FromSeconds(5),
        });
        await sdk.StartAsync();
        var firstObserver = ReadFirstMessageAsync(sdk.EventsAsync());
        var secondObserver = ReadFirstMessageAsync(sdk.EventsAsync());
        await Task.Delay(25);

        var promptText = await CollectTextAsync(sdk.StreamPromptAsync("broadcast"));

        Assert.Equal("broadcast", promptText);
        Assert.Equal("broadcast", await firstObserver.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal("broadcast", await secondObserver.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task DisposingPromptStreamAbortsRealCliAndLateEventsDoNotContaminateNextStream()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = RpcFixture.Create();
        await using var sdk = new AutohandSdk(new AutohandOptions
        {
            CliPath = fixture.Path,
            RequestTimeout = TimeSpan.FromSeconds(5),
        });
        await sdk.StartAsync();

        await foreach (var item in sdk.StreamPromptAsync("abandoned"))
        {
            Assert.Equal("abandoned", Assert.IsType<MessageUpdateEvent>(item).Delta);
            break;
        }

        var state = await sdk.GetStateAsync();
        var next = await CollectTextAsync(sdk.StreamPromptAsync("next"));

        Assert.Equal(1, state.GetProperty("abortCount").GetInt32());
        Assert.Equal("next", next);
        Assert.DoesNotContain("late-from-abandoned", next, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EventBacklogIsBoundedAndDropsOldestItems()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        const int eventCount = 1200;
        using var fixture = RpcFixture.Create();
        await using var transport = CreateTransport(fixture);
        await transport.StartAsync();
        await using var subscription = transport.SubscribeEvents();

        await transport.RequestAsync("autohand.test.burst", new { count = eventCount });

        Assert.Equal(Transport.EventBacklogCapacity, subscription.BufferedCount);
        var deltas = new List<int>();
        while (subscription.TryRead(out var item))
        {
            var update = Assert.IsType<MessageUpdateEvent>(item);
            deltas.Add(int.Parse(update.Delta!, System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.Equal(Transport.EventBacklogCapacity, deltas.Count);
        Assert.Equal(eventCount - Transport.EventBacklogCapacity, deltas[0]);
        Assert.Equal(eventCount - 1, deltas[^1]);
    }

    [Fact]
    public async Task FailedGenerationCompletesOldSubscriptionAndRestartGetsFreshEvents()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = RpcFixture.Create();
        await using var transport = CreateTransport(fixture);
        await transport.StartAsync();
        await using var oldSubscription = transport.SubscribeEvents();
        var oldRead = ReadFirstMessageAsync(oldSubscription.ReadAllAsync());

        await Assert.ThrowsAsync<AutohandSdkException>(
            () => transport.RequestAsync("autohand.test.exit"));
        await Assert.ThrowsAnyAsync<Exception>(
            async () => await oldRead.WaitAsync(TimeSpan.FromSeconds(2)));

        await transport.StartAsync();
        await using var newSubscription = transport.SubscribeEvents();
        var newRead = ReadFirstMessageAsync(newSubscription.ReadAllAsync());
        await transport.RequestAsync("autohand.test.event", new { delta = "fresh" });

        Assert.Equal("fresh", await newRead.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task ConcurrentStartsSpawnOnceAndConcurrentStopsLeaveNoOrphan()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = RpcFixture.Create();
        await using var transport = CreateTransport(fixture, fixture.StartLogPath);

        await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => transport.StartAsync()));
        var firstState = await transport.RequestAsync("autohand.getState");
        var firstPid = firstState.GetProperty("pid").GetInt32();
        Assert.Single(fixture.ReadStartLog());

        await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => transport.StopAsync()));
        await WaitUntilAsync(() => !IsProcessAlive(firstPid));
        Assert.False(transport.IsStarted);

        await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => transport.StartAsync()));
        var secondState = await transport.RequestAsync("autohand.getState");
        var secondPid = secondState.GetProperty("pid").GetInt32();

        Assert.Equal(2, fixture.ReadStartLog().Count);
        Assert.NotEqual(firstPid, secondPid);
        await transport.StopAsync();
        await WaitUntilAsync(() => !IsProcessAlive(secondPid));
    }

    private static Transport CreateTransport(RpcFixture fixture, string? startLog = null) =>
        new(new AutohandOptions
        {
            CliPath = fixture.Path,
            RequestTimeout = TimeSpan.FromSeconds(5),
            Environment = startLog is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?> { ["AUTOHAND_TEST_START_LOG"] = startLog },
        });

    private static async Task<string> CollectTextAsync(IAsyncEnumerable<SdkEvent> events)
    {
        var text = new StringBuilder();
        await foreach (var item in events)
        {
            if (item is MessageUpdateEvent { Delta: { } delta })
            {
                text.Append(delta);
            }
        }

        return text.ToString();
    }

    private static async Task<string> ReadFirstMessageAsync(IAsyncEnumerable<SdkEvent> events)
    {
        await foreach (var item in events)
        {
            if (item is MessageUpdateEvent { Delta: { } delta })
            {
                return delta;
            }
        }

        throw new InvalidOperationException("The event stream completed without a message update.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(5))
            {
                throw new TimeoutException("The expected subprocess state was not reached.");
            }

            await Task.Delay(20);
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private sealed class RpcFixture : IDisposable
    {
        private RpcFixture(string directory, string path, string startLogPath)
        {
            Directory = directory;
            Path = path;
            StartLogPath = startLogPath;
        }

        public string Directory { get; }

        public string Path { get; }

        public string StartLogPath { get; }

        public static RpcFixture Create()
        {
            if (OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("The Python fixture requires a Unix host.");
            }

            var directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"autohand-csharp-tests-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, "fake-rpc-cli");
            var startLogPath = System.IO.Path.Combine(directory, "starts.log");
            File.WriteAllText(path, """
                #!/usr/bin/env python3
                import json
                import os
                import sys
                import threading
                import time

                output_lock = threading.Lock()
                prompt_lock = threading.Lock()
                active_prompt = None
                abort_count = 0

                start_log = os.environ.get("AUTOHAND_TEST_START_LOG")
                if start_log:
                    with open(start_log, "a", encoding="utf-8") as log:
                        log.write(f"{os.getpid()}\n")

                def emit(value):
                    with output_lock:
                        sys.stdout.write(json.dumps(value, separators=(",", ":")) + "\n")
                        sys.stdout.flush()

                def reply(request_id, result):
                    emit({"jsonrpc": "2.0", "id": request_id, "result": result})

                def notify(delta):
                    emit({
                        "jsonrpc": "2.0",
                        "method": "autohand.messageUpdate",
                        "params": {"delta": delta},
                    })

                def run_prompt(request_id, message, state):
                    global active_prompt
                    try:
                        notify(message)
                        if message == "abandoned":
                            state["stop"].wait()
                            notify("late-from-abandoned")
                        emit({"jsonrpc": "2.0", "method": "autohand.turnEnd", "params": {
                            "turnId": str(request_id), "reason": "aborted" if state["stop"].is_set() else "completed"}})
                        reply(request_id, {"success": True})
                    finally:
                        state["done"].set()
                        with prompt_lock:
                            if active_prompt is state:
                                active_prompt = None

                for raw_line in sys.stdin:
                    request = json.loads(raw_line)
                    request_id = request.get("id")
                    method = request.get("method")
                    parameters = request.get("params") or {}

                    if method == "autohand.getState":
                        with prompt_lock:
                            active = active_prompt is not None
                        reply(request_id, {
                            "status": "running" if active else "idle",
                            "abortCount": abort_count,
                            "pid": os.getpid(),
                        })
                    elif method == "autohand.prompt":
                        state = {"stop": threading.Event(), "done": threading.Event()}
                        with prompt_lock:
                            active_prompt = state
                        threading.Thread(
                            target=run_prompt,
                            args=(request_id, parameters.get("message", ""), state),
                            daemon=True,
                        ).start()
                    elif method == "autohand.abort":
                        with prompt_lock:
                            state = active_prompt
                        if state is not None:
                            state["stop"].set()
                            state["done"].wait(timeout=3)
                        abort_count += 1
                        reply(request_id, {"success": True})
                    elif method == "autohand.test.event":
                        notify(parameters.get("delta", "event"))
                        reply(request_id, {"success": True})
                    elif method == "autohand.test.burst":
                        for index in range(int(parameters.get("count", 0))):
                            notify(str(index))
                        reply(request_id, {"success": True})
                    elif method == "autohand.test.malformed":
                        with output_lock:
                            sys.stdout.write("not-json\n")
                            sys.stdout.flush()
                    elif method == "autohand.test.closeStdout":
                        os.close(sys.stdout.fileno())
                        time.sleep(30)
                    elif method == "autohand.test.exit":
                        sys.exit(0)
                    elif method == "autohand.test.pauseReads":
                        reply(request_id, {"success": True})
                        time.sleep(3)
                    elif method == "autohand.test.hang":
                        pass
                    else:
                        reply(request_id, {"success": True})
                """);
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return new RpcFixture(directory, path, startLogPath);
        }

        public IReadOnlyList<string> ReadStartLog() =>
            File.Exists(StartLogPath) ? File.ReadAllLines(StartLogPath) : [];

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
