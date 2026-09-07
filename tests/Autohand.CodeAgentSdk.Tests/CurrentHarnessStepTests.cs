using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Autohand.CodeAgentSdk;
using Xunit;

namespace Autohand.CodeAgentSdk.Tests;

public sealed class CurrentHarnessStepTests
{
    [Fact]
    public async Task ActualCliUsesAutohandAiDiscoversExtensionsAndResumesPersistedSteps()
    {
        var cli = Environment.GetEnvironmentVariable("AUTOHAND_TEST_CLI_PATH");
        if (string.IsNullOrWhiteSpace(cli)) return;
        var directory = Path.Combine(Path.GetTempPath(), "autohand-csharp-harness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        var port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();
        var baseUrl = $"http://127.0.0.1:{port}";
        using var listener = new HttpListener();
        listener.Prefixes.Add(baseUrl + "/");
        listener.Start();
        using var shutdown = new CancellationTokenSource();
        var requests = new ConcurrentQueue<JsonElement>();
        Exception? serverFailure = null;
        var server = Task.Run(async () =>
        {
            while (!shutdown.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync().WaitAsync(shutdown.Token); }
                catch (OperationCanceledException) { break; }
                try
                {
                    object response;
                    if (context.Request.Url!.AbsolutePath == "/auth/me")
                    {
                        Assert.Equal("Bearer session-token", context.Request.Headers["Authorization"]);
                        response = new { user = new { id = "csharp-sdk", email = "sdk@example.test" } };
                    }
                    else
                    {
                        Assert.Equal("/v1/chat/completions", context.Request.Url.AbsolutePath);
                        Assert.Equal("Bearer inference-key", context.Request.Headers["Authorization"]);
                        using var body = await JsonDocument.ParseAsync(context.Request.InputStream);
                        var request = body.RootElement.Clone();
                        Assert.Equal("fantail", request.GetProperty("model").GetString());
                        requests.Enqueue(request);
                        var first = requests.Count == 1;
                        object message = first ? new
                        {
                            role = "assistant",
                            content = "Read evidence",
                            tool_calls = new[]
                            {
                                new { id = "read-evidence", type = "function", function = new { name = "read_file", arguments = "{\"path\":\"evidence.txt\"}" } },
                            },
                        } : new { role = "assistant", content = "continued from persisted evidence" };
                        response = new
                        {
                            id = "csharp-completion",
                            choices = new[] { new { message, finish_reason = first ? "tool_calls" : "stop" } },
                            usage = new { prompt_tokens = 10, completion_tokens = 5, total_tokens = 15 },
                        };
                    }
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(response);
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                }
                catch (Exception exception)
                {
                    serverFailure = exception;
                    context.Response.StatusCode = 500;
                }
                finally { context.Response.Close(); }
            }
        });
        try
        {
            var workspace = Path.Combine(directory, "workspace");
            var home = Path.Combine(directory, "home");
            Directory.CreateDirectory(workspace);
            Directory.CreateDirectory(home);
            await File.WriteAllTextAsync(Path.Combine(workspace, "evidence.txt"), "csharp-persisted-marker");
            var config = Path.Combine(home, "config.json");
            var saved = new
            {
                provider = "openai",
                auth = new { token = "session-token" },
                openai = new { apiKey = "saved-key", model = "saved-model", baseUrl = "http://127.0.0.1:1/unused" },
                features = new { automaticSpecialists = false },
                agent = new { autoMemory = false },
                telemetry = new { enabled = false },
            };
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(saved));
            var extension = Path.Combine(workspace, ".autohand", "extensions", "sdk.csharp");
            Directory.CreateDirectory(Path.Combine(extension, "agents"));
            await File.WriteAllTextAsync(Path.Combine(extension, "autohand.extension.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                extensionApi = 1,
                id = "sdk.csharp",
                name = "C# helper",
                version = "1.0.0",
                description = "Local SDK integration",
                contributes = new { agents = new[] { "agents/helper.md" } },
            }));
            await File.WriteAllTextAsync(Path.Combine(extension, "agents", "helper.md"), "---\ndescription: C# helper\ntools: read_file\n---\nPrivate helper instructions.\n");
            await using var agent = await Agent.CreateAsync(new AgentOptions
            {
                WorkingDirectory = workspace,
                CliPath = cli,
                Provider = "autohandai",
                Model = "fantail",
                ApiKey = "inference-key",
                BaseUrl = baseUrl + "/v1",
                Unrestricted = true,
                RequestTimeout = TimeSpan.FromSeconds(30),
                Agents = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["inline-helper"] = new { description = "Inline helper", prompt = "Private inline instructions.", tools = new[] { "read_file" } },
                }),
                Environment = new Dictionary<string, string?>
                {
                    ["AUTOHAND_HOME"] = home,
                    ["AUTOHAND_CONFIG"] = config,
                    ["AUTOHAND_AUTH_API_URL"] = baseUrl + "/auth",
                    ["AUTOHAND_SKIP_PING"] = "1",
                    ["AUTOHAND_SKIP_UPDATE_CHECK"] = "1",
                    ["AUTOHAND_NO_IDLE_LOGOUT"] = "1",
                    ["AUTOHAND_DISABLE_AUTO_REPORT"] = "1",
                },
            });
            var agents = await agent.GetSupportedAgentsAsync();
            Assert.Contains(agents, item => item.Name == "reviewer" && item.Source == "builtin");
            Assert.Contains(agents, item => item.Name == "inline-helper" && item.Source == "session");
            Assert.Contains(agents, item => item.Name == "helper" && item.ExtensionId == "sdk.csharp");
            Assert.DoesNotContain("Private", JsonSerializer.Serialize(agents));
            var disabled = await agent.RunAsync("/extensions disable sdk.csharp --scope project");
            Assert.Contains("Disabled sdk.csharp", disabled.Text);
            Assert.DoesNotContain(await agent.GetSupportedAgentsAsync(), item => item.ExtensionId == "sdk.csharp");
            var enabled = await agent.RunAsync("/extensions enable sdk.csharp --scope project");
            Assert.Contains("Enabled sdk.csharp", enabled.Text);
            Assert.Contains(await agent.GetSupportedAgentsAsync(), item => item.ExtensionId == "sdk.csharp");
            Assert.Empty(requests);
            var result = await agent.RunAsync("Read evidence.txt using read_file", new PromptOptions { StopWhen = [StopConditions.IsStepCount(1)] });
            Assert.Equal("stopped", result.Status);
            Assert.Single(result.Steps);
            Assert.Single(requests);
            Assert.Contains("csharp-persisted-marker", result.Steps[0].ToolResults[0].Output);
            var continued = await agent.RunAsync("Continue using the saved tool result");
            Assert.Equal("completed", continued.Status);
            Assert.Equal("continued from persisted evidence", continued.Text);
            Assert.Equal(2, requests.Count);
            Assert.Contains("csharp-persisted-marker", requests.ToArray()[1].GetProperty("messages").ToString());
            using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(config));
            Assert.Equal("openai", persisted.RootElement.GetProperty("provider").GetString());
            var provider = persisted.RootElement.GetProperty("openai");
            Assert.Equal(saved.openai.apiKey, provider.GetProperty("apiKey").GetString());
            Assert.Equal(saved.openai.model, provider.GetProperty("model").GetString());
            Assert.Equal(saved.openai.baseUrl, provider.GetProperty("baseUrl").GetString());
            Assert.False(persisted.RootElement.TryGetProperty("autohandai", out _));
            Assert.Null(serverFailure);
        }
        finally
        {
            shutdown.Cancel();
            await server;
            Directory.Delete(directory, recursive: true);
        }
    }
}
