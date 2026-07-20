using Autohand.CodeAgentSdk;
using Xunit;

namespace Autohand.CodeAgentSdk.Tests;

public sealed class SdkControlE2ETests
{
    [Fact]
    public async Task AcknowledgesPermissionThroughSpawnedCli()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = FeatureRpcFixture.Create();
        await using var sdk = CreateSdk(fixture);
        await sdk.StartAsync();

        var result = await sdk.AcknowledgePermissionAsync("permission-1");

        Assert.True(result.Success);
    }

    [Fact]
    public async Task RespondsToDirectoryAccessThroughSpawnedCli()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = FeatureRpcFixture.Create();
        await using var sdk = CreateSdk(fixture);
        await sdk.StartAsync();

        var result = await sdk.RespondDirectoryAccessAsync("directory-1", granted: true);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task AcknowledgesDirectoryAccessThroughSpawnedCli()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = FeatureRpcFixture.Create();
        await using var sdk = CreateSdk(fixture);
        await sdk.StartAsync();

        var result = await sdk.AcknowledgeDirectoryAccessAsync("directory-1");

        Assert.True(result.Success);
    }

    private static AutohandSdk CreateSdk(FeatureRpcFixture fixture) =>
        new(new AutohandOptions
        {
            CliPath = fixture.Path,
            RequestTimeout = TimeSpan.FromSeconds(5),
        });

    private sealed class FeatureRpcFixture : IDisposable
    {
        private FeatureRpcFixture(string directory, string path)
        {
            Directory = directory;
            Path = path;
        }

        public string Directory { get; }
        public string Path { get; }

        public static FeatureRpcFixture Create()
        {
            var directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"autohand-csharp-features-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, "fake-rpc-cli");
            File.WriteAllText(path, """
                #!/usr/bin/env python3
                import json
                import sys

                def emit(value):
                    sys.stdout.write(json.dumps(value, separators=(",", ":")) + "\n")
                    sys.stdout.flush()

                def reply(request_id, result):
                    emit({"jsonrpc": "2.0", "id": request_id, "result": result})

                def notify(method, params):
                    emit({"jsonrpc": "2.0", "method": method, "params": params})

                def result_for(method, params):
                    if method == "autohand.permissionAcknowledged":
                        return {"success": list(params.keys()) == ["requestId"] and params.get("requestId") == "permission-1"}
                    if method == "autohand.directoryAccessResponse":
                        return {"success": params == {"requestId": "directory-1", "granted": True}}
                    if method == "autohand.directoryAccessAcknowledged":
                        return {"success": params == {"requestId": "directory-1"}}
                    if method == "autohand.changesDecision":
                        exact = params == {"batchId": "batch-1", "action": "accept_selected", "selectedChangeIds": ["change-1"]}
                        return {"success": exact, "appliedCount": 1 if exact else 0, "skippedCount": 1, "errors": []}
                    if method == "autohand.getHistory":
                        return {"sessions": [{"sessionId": "history-1", "createdAt": "2026-07-20T00:00:00Z", "lastActiveAt": "2026-07-20T00:10:00Z", "projectName": "csharp-sdk", "model": "fantail", "messageCount": 7 if params == {"page": 2, "pageSize": 25} else -1, "status": "completed"}], "currentPage": 2, "totalPages": 3, "totalItems": 51}
                    if method == "autohand.getSession":
                        if params.get("sessionId") == "missing-session":
                            return {"success": False, "error": "Session not found"}
                        return {"success": True, "sessionId": params.get("sessionId"), "projectName": "csharp-sdk", "model": "fantail", "messageCount": 1, "status": "completed", "createdAt": "2026-07-20T00:00:00Z", "lastActiveAt": "2026-07-20T00:10:00Z", "summary": "Session summary", "messages": [{"id": "message-1", "role": "assistant", "content": "done", "timestamp": "2026-07-20T00:10:00Z"}], "workspaceRoot": "/workspace"}
                    if method == "autohand.session.attach":
                        exact = params == {"sessionId": "session-attach-1"}
                        return {"success": exact, "sessionId": "session-attach-1", "workspaceRoot": "/workspace", "messageCount": 9}
                    if method == "autohand.yoloSet":
                        exact = params == {"pattern": "*", "timeoutSeconds": 60}
                        return {"success": exact, "expiresIn": 60}
                    if method == "autohand.yolo.set":
                        return {"success": params == {"pattern": ""}}
                    if method == "autohand.mcp.setVscodeTools":
                        tool = params.get("tools", [{}])[0]
                        exact = tool.get("name") == "search" and tool.get("serverName") == "github" and tool.get("inputSchema", {}).get("type") == "object"
                        return {"success": exact}
                    if method == "autohand.mcp.invokeResponse":
                        return {"success": params == {"requestId": "mcp-request-1", "success": True, "result": "issue-42"}}
                    if method == "autohand.learn.recommend":
                        exact = params == {"deep": True}
                        return {"success": exact, "projectSummary": "C# SDK project", "audit": [{"skill": "legacy-dotnet", "status": "outdated", "reason": "Old runtime"}], "recommendations": [{"slug": "dotnet-8", "score": 0.98, "reason": "Uses records"}], "gapAnalysis": "Add async guidance"}
                    if method == "autohand.learn.update":
                        return {"success": params == {}, "updated": 1, "unchanged": 1, "results": [{"name": "dotnet-8", "status": "updated"}, {"name": "testing", "status": "unchanged"}]}
                    if method == "autohand.learn.generate":
                        exact = params == {"scope": "project"}
                        return {"success": exact, "skillName": "csharp-sdk-learning", "skillPath": ".agents/skills/csharp-sdk-learning"}
                    if method == "autohand.getToolsRegistry":
                        return {"tools": [{"name": "read_file", "description": "Read a file", "requiresApproval": False, "source": "builtin", "scope": "project", "disabled": False, "schemaVersion": 1, "reuseHint": "Reuse read results"}], "diagnostics": [{"file": "broken-tool.json", "reason": "Invalid schema"}] if params == {} else []}
                    if method == "autohand.setContextCompact":
                        return {"enabled": params == {"enabled": True}}
                    return {"success": True}

                for raw_line in sys.stdin:
                    request = json.loads(raw_line)
                    request_id = request.get("id")
                    method = request.get("method")
                    params = request.get("params") or {}
                    if method == "autohand.test.emit":
                        notify(params["notificationMethod"], params["payload"])
                        reply(request_id, {"success": True})
                    else:
                        reply(request_id, result_for(method, params))
                """);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            return new FeatureRpcFixture(directory, path);
        }

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
