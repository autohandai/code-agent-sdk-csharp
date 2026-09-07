using System.Text.Json;
using Autohand.CodeAgentSdk;
using Xunit;

namespace Autohand.CodeAgentSdk.Tests;

public sealed class StepControlTests
{
    [Fact]
    public async Task PlainPromptWaitsForTheTerminalEvent()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new StepFixture();
        await using var sdk = new AutohandSdk(fixture.Options);
        await sdk.StartAsync();
        await sdk.PromptAsync("Read evidence");
        Assert.True(File.Exists(Path.Combine(fixture.Directory, "completed")));
    }

    [Fact]
    public async Task StopsAfterPersistedStepsAndContinuesTheSameAgent()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new StepFixture();
        await using var agent = await Agent.CreateAsync(fixture.Options);
        var result = await agent.RunAsync("Read evidence", new PromptOptions
        {
            StopWhen = [StopConditions.IsStepCount(2),
                StopConditions.HasToolCall("never"),
                (context, _) =>
            {
                var step = context.Steps[^1];
                Assert.Equal("evidence-" + step.StepNumber, step.ToolResults[0].Output);
                Assert.Throws<NotSupportedException>(() => ((IList<AgentStep>)context.Steps).Clear());
                return ValueTask.FromResult(false);
            }
            ],
        });
        Assert.Equal("stopped", result.Status);
        Assert.Equal(2, result.Steps.Count);
        Assert.Equal("read_file", result.Steps[0].ToolCalls[0].Tool);
        var next = await agent.RunAsync("Continue");
        Assert.Equal("completed", next.Status);
        Assert.Equal("continued", next.Text);
        Assert.Empty(next.Steps);
        var prompt = fixture.Requests.First(r => r.GetProperty("method").GetString() == "autohand.prompt");
        Assert.Equal("host", prompt.GetProperty("params").GetProperty("stopWhen").GetProperty("mode").GetString());
    }

    [Fact]
    public async Task HasToolCallUsesTheLatestStepAndTrimsTheName()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new StepFixture();
        await using var agent = await Agent.CreateAsync(fixture.Options);
        var result = await agent.RunAsync("Read evidence", new PromptOptions { StopWhen = [StopConditions.HasToolCall(" read_file ")] });
        Assert.Equal("stopped", result.Status);
        Assert.Single(result.Steps);
    }

    [Fact]
    public async Task AbortsANonCooperativeConditionWithoutALateDecision()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new StepFixture();
        await using var agent = await Agent.CreateAsync(fixture.Options);
        var entered = Signal();
        var verdict = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = agent.Send("Read evidence", new PromptOptions
        {
            StopWhen = [(context, _) =>
        {
            entered.SetResult();
            return new ValueTask<bool>(verdict.Task);
        }
            ]
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await run.AbortAsync();
        Assert.Equal("aborted", (await run.WaitAsync()).Status);
        Assert.Equal("continued", (await agent.RunAsync("Continue")).Text);
        verdict.SetResult(true);
        Assert.DoesNotContain(fixture.Requests, r => r.GetProperty("method").GetString() == "autohand.stepDecision");
    }

    [Fact]
    public async Task CancellingAQueuedRunDoesNotAbortTheActiveRun()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new StepFixture();
        await using var agent = await Agent.CreateAsync(fixture.Options);
        var entered = Signal();
        var verdict = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = agent.Send("Read evidence", new PromptOptions
        {
            StopWhen = [(context, _) =>
        {
            entered.SetResult();
            return new ValueTask<bool>(verdict.Task);
        }
            ]
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var queued = agent.Send("Queued prompt");
        await queued.AbortAsync();
        Assert.Equal("aborted", (await queued.WaitAsync()).Status);
        Assert.DoesNotContain(fixture.Requests, r => r.GetProperty("method").GetString() == "autohand.abort");
        verdict.SetResult(true);
        Assert.Equal("stopped", (await first.WaitAsync()).Status);
        Assert.Single(fixture.Requests, r => r.GetProperty("method").GetString() == "autohand.prompt");
    }

    [Fact]
    public async Task FailingConditionStopsWithoutWaitingForOtherConditions()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new StepFixture();
        await using var agent = await Agent.CreateAsync(fixture.Options);
        var failure = new InvalidOperationException("condition failed");
        var run = agent.Send("Read evidence", new PromptOptions
        {
            StopWhen = [(_, _) => new ValueTask<bool>(new TaskCompletionSource<bool>().Task),
                (_, _) => ValueTask.FromException<bool>(failure)],
        });
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync().WaitAsync(TimeSpan.FromSeconds(3))));
        Assert.Contains(fixture.Requests, r => r.GetProperty("method").GetString() == "autohand.stepDecision"
            && r.GetProperty("params").GetProperty("stop").GetBoolean());
        Assert.Equal("continued", (await agent.RunAsync("Continue")).Text);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"success\":false}")]
    [InlineData("{\"success\":\"true\"}")]
    [InlineData("rpc-error")]
    public async Task InvalidStepDecisionAbortsAndRecovers(string response)
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new StepFixture(new Dictionary<string, string?> { ["DECISION_RESULT"] = response });
        await using var agent = await Agent.CreateAsync(fixture.Options);
        var error = await Assert.ThrowsAnyAsync<AutohandSdkException>(() => agent.RunAsync("Read evidence",
            new PromptOptions { StopWhen = [StopConditions.IsStepCount(1)] }));
        Assert.Contains("stepDecision", error.Message);
        Assert.Equal("continued", (await agent.RunAsync("Continue")).Text);
        Assert.Contains(fixture.Requests, r => r.GetProperty("method").GetString() == "autohand.abort");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"stepId\":\"one\",\"timestamp\":\"now\",\"step\":{\"stepNumber\":0,\"toolCalls\":[],\"toolResults\":[]}}")]
    [InlineData("{\"stepId\":\"one\",\"timestamp\":\"now\",\"step\":{\"stepNumber\":1,\"toolCalls\":[{\"tool\":\"read_file\",\"args\":[]}],\"toolResults\":[]}}")]
    public async Task MalformedStepAbortsAndRecovers(string step)
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new StepFixture(new Dictionary<string, string?> { ["STEP_RESULT"] = step });
        await using var agent = await Agent.CreateAsync(fixture.Options);
        var error = await Assert.ThrowsAnyAsync<AutohandSdkException>(() => agent.RunAsync("Read evidence",
            new PromptOptions { StopWhen = [StopConditions.IsStepCount(1)] }));
        Assert.Contains("stepEnd", error.Message);
        Assert.Equal("continued", (await agent.RunAsync("Continue")).Text);
    }

    [Fact]
    public async Task ExitingCliInterruptsAnUnresolvedCondition()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new StepFixture(new Dictionary<string, string?> { ["EXIT_AT_STEP"] = "1" });
        await using var agent = await Agent.CreateAsync(fixture.Options);
        await Assert.ThrowsAnyAsync<AutohandSdkException>(() => agent.RunAsync("Read evidence", new PromptOptions
        {
            StopWhen = [(_, _) => new ValueTask<bool>(new TaskCompletionSource<bool>().Task)],
        }).WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task AbandonedStreamAbortsAfterAcknowledgementAndDrainsTheTerminal()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new StepFixture();
        await using var sdk = new AutohandSdk(fixture.Options);
        await sdk.StartAsync();
        await foreach (var _ in sdk.StreamPromptAsync("Read evidence", new PromptOptions
        {
            StopWhen = [(_, _) => new ValueTask<bool>(new TaskCompletionSource<bool>().Task)],
        })) break;
        Assert.Contains(fixture.Requests, r => r.GetProperty("method").GetString() == "autohand.abort");
        Assert.Equal("continued", (await Agent.FromSdk(sdk).RunAsync("Continue")).Text);
    }

    [Fact]
    public async Task AgentRunCancellationStopsItsUnderlyingPrompt()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new StepFixture();
        await using var agent = await Agent.CreateAsync(fixture.Options);
        var entered = Signal();
        using var cancellation = new CancellationTokenSource();
        var result = agent.RunAsync("Read evidence", new PromptOptions
        {
            StopWhen = [(_, _) =>
        {
            entered.SetResult();
            return new ValueTask<bool>(new TaskCompletionSource<bool>().Task);
        }
            ]
        }, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result);
        Assert.Equal("continued", (await agent.RunAsync("Continue")).Text);
    }

    [Fact]
    public void InvalidConditionsFailBeforeAnyRpc()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StopConditions.IsStepCount(0));
        Assert.Throws<ArgumentException>(() => StopConditions.HasToolCall(" "));
    }

    [Fact]
    public async Task TerminalEventDoesNotHideARejectedPromptResponse()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new StepFixture(new Dictionary<string, string?> { ["REJECT_AFTER_TERMINAL"] = "1" });
        await using var agent = await Agent.CreateAsync(fixture.Options);
        await Assert.ThrowsAnyAsync<AutohandSdkException>(() => agent.RunAsync("Read evidence"));
    }

    [Fact]
    public async Task DisposingTheAgentStreamStopsItsUnderlyingRun()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new StepFixture();
        await using var agent = await Agent.CreateAsync(fixture.Options);
        await foreach (var _ in agent.StreamAsync("Read evidence", new PromptOptions
        {
            StopWhen = [(_, _) => new ValueTask<bool>(new TaskCompletionSource<bool>().Task)],
        })) break;
        Assert.Contains(fixture.Requests, r => r.GetProperty("method").GetString() == "autohand.abort");
        Assert.Equal("continued", (await agent.RunAsync("Continue")).Text);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class StepFixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "autohand-csharp-steps-" + Guid.NewGuid().ToString("N"));
        public AgentOptions Options { get; }
        public IReadOnlyList<JsonElement> Requests => File.ReadAllLines(Path.Combine(Directory, "requests"))
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();

        public StepFixture(IReadOnlyDictionary<string, string?>? environment = null)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var executable = Path.Combine(Directory, "step-cli");
            File.WriteAllText(executable, """
                #!/usr/bin/env python3
                import json, os, sys, threading
                write_lock = threading.Lock()
                prompt = 0
                step = 0
                generation = 0
                def write(message):
                    with write_lock:
                        print(json.dumps(message), flush=True)
                def reply(id, result=None):
                    write({'jsonrpc':'2.0', 'id':id, 'result':{'success':True} if result is None else result})
                def event(method, params):
                    write({'jsonrpc':'2.0', 'method':method, 'params':params})
                def end(reason):
                    event('autohand.turnEnd', {'turnId':str(prompt), 'reason':reason, 'timestamp':'now'})
                def next_step():
                    global step
                    step += 1
                    payload = json.loads(os.environ['STEP_RESULT']) if 'STEP_RESULT' in os.environ else {
                        'stepId':'step-'+str(step), 'timestamp':'now', 'step':{
                            'stepNumber':step, 'thought':'Read evidence',
                            'toolCalls':[{'id':'read-'+str(step), 'tool':'read_file', 'args':{'path':'evidence.txt'}}],
                            'toolResults':[{'tool':'read_file', 'success':True, 'output':'evidence-'+str(step)}]}}
                    event('autohand.stepEnd', payload)
                    if 'EXIT_AT_STEP' in os.environ:
                        threading.Timer(0.05, lambda: os._exit(1)).start()
                def complete(expected):
                    if expected != generation: return
                    with open('completed', 'w') as out: out.write('yes')
                    event('autohand.messageUpdate', {'messageId':'answer', 'delta':'continued', 'timestamp':'now'})
                    end('completed')
                for line in sys.stdin:
                    with open('requests', 'a') as out: out.write(line)
                    request = json.loads(line)
                    id, method, params = request['id'], request['method'], request.get('params', {})
                    if method == 'autohand.prompt':
                        prompt += 1
                        generation += 1
                        if 'REJECT_AFTER_TERMINAL' in os.environ:
                            end('completed')
                            write({'jsonrpc':'2.0', 'id':id, 'error':{'code':-32602, 'message':'prompt rejected'}})
                            continue
                        reply(id)
                        if prompt == 1 and params.get('stopWhen'):
                            assert params['stopWhen'] == {'mode':'host'}
                            next_step()
                        else: threading.Timer(0.08, complete, args=(generation,)).start()
                    elif method == 'autohand.stepDecision':
                        result = os.environ.get('DECISION_RESULT')
                        if result == 'rpc-error':
                            write({'jsonrpc':'2.0', 'id':id, 'error':{'code':-32602, 'message':'stepDecision rejected'}})
                        elif result: reply(id, json.loads(result))
                        else:
                            reply(id)
                            if params['stop']: end('stop_condition')
                            else: next_step()
                    elif method == 'autohand.abort':
                        generation += 1
                        reply(id)
                        end('aborted')
                    else: reply(id)
                """);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(executable,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Options = new AgentOptions
            {
                WorkingDirectory = Directory,
                CliPath = executable,
                RequestTimeout = TimeSpan.FromSeconds(3),
                Environment = environment ?? new Dictionary<string, string?>(),
            };
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
