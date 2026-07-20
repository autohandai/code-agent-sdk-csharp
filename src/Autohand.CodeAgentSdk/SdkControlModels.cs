namespace Autohand.CodeAgentSdk;

/// <summary>Result returned after acknowledging receipt of a permission prompt.</summary>
public sealed record PermissionAcknowledgementResult(bool Success);

/// <summary>Result returned after resolving a directory-access prompt.</summary>
public sealed record DirectoryAccessResponseResult(bool Success);

/// <summary>Result returned after acknowledging receipt of a directory-access prompt.</summary>
public sealed record DirectoryAccessAcknowledgementResult(bool Success);
