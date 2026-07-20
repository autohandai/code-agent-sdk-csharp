# Community Skills and MCP Discovery

Community skill registry, installation, and MCP discovery calls are typed on
both `AutohandSdk` and `Agent`.

```csharp
var registry = await sdk.GetSkillsRegistryAsync(
    new GetSkillsRegistryParams(ForceRefresh: true));

var installed = await sdk.InstallSkillAsync(
    new InstallSkillParams(
        "csharp-quality",
        SkillInstallScope.Project,
        Force: true));

var servers = await sdk.ListMcpServersAsync();
var tools = await sdk.ListMcpToolsAsync(new McpListToolsParams("github"));
var configs = await sdk.GetMcpServerConfigsAsync();
```

Pass no registry parameters to use the CLI cache. Pass no MCP tool parameters
to list tools from every configured server. `SkillInstallScope.User` and
`SkillInstallScope.Project` serialize to the CLI's exact lowercase values.
