using System.Text.Json.Nodes;
namespace Autohand.CodeAgentSdk;

/// <summary>Private run-only provider settings, applied before runtime construction.</summary>
internal static class RuntimeSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    internal static JsonObject Build(AutohandOptions options)
    {
        var settings = options.RunSettings?.DeepClone().AsObject() ?? new JsonObject();
        ValidateKeys(settings);
        if (options.Features is not null)
            Merge(settings, new JsonObject { ["features"] = JsonSerializer.SerializeToNode(options.Features, JsonOptions) });
        if (options.Provider is null)
        {
            if (new object?[] { options.ApiKey, options.BaseUrl, options.AutohandAiPlan, options.Port,
                options.OpenaiAuthMode, options.ChatgptAccessToken, options.ChatgptAccountId,
                options.AzureAuthMethod, options.AzureTenantId, options.AzureClientId, options.AzureClientSecret,
                options.AzureResourceName, options.AzureDeploymentName, options.AzureApiVersion }.Any(value => value is not null))
                throw new AutohandSdkException("A provider is required for provider credentials and settings.");
            return settings;
        }
        var provider = options.Provider;
        var section = new JsonObject();
        void Put(string key, string? value) { if (value is not null) section[key] = value; }
        Put("model", options.Model);
        if (options.ApiKey is not null)
        {
            Put(provider == "vertexai" ? "authToken" : "apiKey", options.ApiKey);
            switch (provider)
            {
                case "autohandai": case "openai": case "xai": section["authMode"] = "api-key"; break;
                case "azure": section["authMethod"] = "api-key"; break;
                case "bedrock": section["authMode"] = "bedrock-api-key"; break;
            }
        }
        if (options.BaseUrl is not null)
        {
            var key = provider is "vertexai" or "bedrock" ? "endpoint" : "baseUrl";
            Put(key, provider == "vertexai" ? VertexEndpoint(options.BaseUrl) : options.BaseUrl);
        }
        else if (options.Port is not null && provider is "ollama" or "llamacpp" or "mlx")
            section["baseUrl"] = $"http://localhost:{options.Port}";
        if (provider == "autohandai")
        {
            if (options.AutohandAiPlan is not null) section["plan"] = options.AutohandAiPlan;
            else if ((options.ApiKey is not null || options.BaseUrl is not null) && settings[provider]?["plan"] is null)
                section["plan"] = "cloud";
        }
        if (provider == "openai")
        { Put("authMode", options.OpenaiAuthMode); Put("chatgptAccessToken", options.ChatgptAccessToken); Put("chatgptAccountId", options.ChatgptAccountId); }
        if (provider == "azure")
        {
            Put("authMethod", options.AzureAuthMethod); Put("tenantId", options.AzureTenantId);
            Put("clientId", options.AzureClientId); Put("clientSecret", options.AzureClientSecret);
            Put("resourceName", options.AzureResourceName); Put("deploymentName", options.AzureDeploymentName); Put("apiVersion", options.AzureApiVersion);
        }
        if (section.Count > 0)
        {
            var overlay = provider.StartsWith("custom:", StringComparison.Ordinal)
                ? new JsonObject { ["customProviders"] = new JsonObject { [provider[7..]] = section } }
                : provider.StartsWith("extension:", StringComparison.Ordinal)
                    ? new JsonObject { ["extensionProviders"] = new JsonObject { [provider] = section } }
                    : new JsonObject { [provider] = section };
            Merge(settings, overlay);
        }
        return settings;
    }

    internal static bool Needed(AutohandOptions options) => options.Features is not null || options.RunSettings is not null
        || (options.Provider is not null and not "autohandai" && new object?[] {
            options.ApiKey, options.BaseUrl, options.Port, options.OpenaiAuthMode, options.ChatgptAccessToken,
            options.ChatgptAccountId, options.AzureAuthMethod, options.AzureTenantId, options.AzureClientId,
            options.AzureClientSecret, options.AzureResourceName, options.AzureDeploymentName, options.AzureApiVersion }.Any(value => value is not null));

    private static string VertexEndpoint(string input)
    {
        if (Uri.TryCreate(input.Contains("://", StringComparison.Ordinal) ? input : "https://" + input, UriKind.Absolute, out var uri)
            && uri.Scheme == "https" && uri.Host.Length > 0 && uri.UserInfo.Length == 0 && uri.IsDefaultPort
            && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0)
            return uri.Host;
        throw new AutohandSdkException("Vertex endpoint must be an HTTPS origin or hostname.");
    }
    private static void ValidateKeys(JsonNode? node, int depth = 0)
    {
        if (depth > 256) throw new AutohandSdkException("Run settings exceed the maximum nesting depth.");
        if (node is JsonObject obj) foreach (var pair in obj)
        {
            if (pair.Key is "__proto__" or "prototype" or "constructor") throw new AutohandSdkException("Run settings contain an unsafe setting path.");
            ValidateKeys(pair.Value, depth + 1);
        }
        else if (node is JsonArray array) foreach (var value in array) ValidateKeys(value, depth + 1);
    }
    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var pair in source)
        {
            if (pair.Value is JsonObject child && target[pair.Key] is JsonObject existing) Merge(existing, child);
            else target[pair.Key] = pair.Value?.DeepClone();
        }
    }
}
