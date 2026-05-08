namespace Autohand.CodeAgentSdk;

public class AutohandSdkException : Exception
{
    public AutohandSdkException(string message)
        : base(message)
    {
    }

    public AutohandSdkException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class TransportNotStartedException : AutohandSdkException
{
    public TransportNotStartedException()
        : base("The Autohand transport has not been started.")
    {
    }
}

public sealed class RequestTimeoutException : AutohandSdkException
{
    public RequestTimeoutException(string method, TimeSpan timeout)
        : base($"Request timed out after {timeout}: {method}")
    {
        Method = method;
        Timeout = timeout;
    }

    public string Method { get; }
    public TimeSpan Timeout { get; }
}

public sealed class RpcException : AutohandSdkException
{
    public RpcException(int code, string message, JsonElement? data = null)
        : base($"RPC error {code}: {message}")
    {
        Code = code;
        RpcMessage = message;
        RpcData = data;
    }

    public int Code { get; }
    public string RpcMessage { get; }
    public JsonElement? RpcData { get; }
}

public sealed class StructuredOutputException : AutohandSdkException
{
    public StructuredOutputException(string message, string rawResponse)
        : base($"{message}\n\nRaw response preview:\n{Preview(rawResponse)}")
    {
        RawResponse = rawResponse;
    }

    public string RawResponse { get; }

    private static string Preview(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return "<empty>";
        }

        return trimmed.Length <= 1200 ? trimmed : string.Concat(trimmed.AsSpan(0, 1200), "\n...");
    }
}
