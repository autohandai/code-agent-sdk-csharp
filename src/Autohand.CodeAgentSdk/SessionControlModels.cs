namespace Autohand.CodeAgentSdk;

/// <summary>Result returned after replacing the active conversation.</summary>
public sealed record ResetResult(string SessionId);

public sealed record BrowserHandoffCreateParams(
    string? ExtensionId = null,
    string? InstallUrl = null);

public sealed record BrowserHandoffCreateResult(
    string Token,
    string SessionId,
    string WorkspaceRoot,
    string CreatedAt,
    string ExpiresAt,
    string Url);

public sealed record BrowserHandoffAttachParams(string Token);

public sealed record BrowserHandoffAttachResult(
    bool Success,
    string? SessionId = null,
    string? WorkspaceRoot = null,
    int? MessageCount = null);
