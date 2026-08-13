using GitHub.Copilot;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.CopilotSdk.Tests;

public class PermissionPolicyTests
{
    private static PermissionInvocation Invocation() => new() { SessionId = "session-1" };

    private static PermissionRequestCustomTool CustomTool(string name) =>
        new() { ToolName = name, ToolDescription = "a tool" };

    [Fact]
    public async Task Safe_policy_approves_allowlisted_tools()
    {
        var policy = CopilotSafePermissionPolicy.Create(new HashSet<string>(StringComparer.Ordinal) { "get_weather" });

        var decision = await policy(CustomTool("get_weather"), Invocation());

        Assert.Equal(CopilotSdkPermissionDecision.Approve, decision);
    }

    [Fact]
    public async Task Safe_policy_denies_tools_not_in_the_allowlist()
    {
        var policy = CopilotSafePermissionPolicy.Create(new HashSet<string>(StringComparer.Ordinal) { "get_weather" });

        var decision = await policy(CustomTool("delete_everything"), Invocation());

        Assert.Equal(CopilotSdkPermissionDecision.Deny, decision);
    }

    [Fact]
    public async Task Safe_policy_denies_same_named_mcp_tool()
    {
        var policy = CopilotSafePermissionPolicy.Create(
            new HashSet<string>(StringComparer.Ordinal) { "get_weather" });
        var request = new PermissionRequestMcp
        {
            ReadOnly = true,
            ServerName = "weather-server",
            ToolName = "get_weather",
            ToolTitle = "Weather",
        };

        var decision = await policy(request, Invocation());

        Assert.Equal(CopilotSdkPermissionDecision.Deny, decision);
    }

    [Theory]
    [InlineData("shell")]
    [InlineData("write")]
    [InlineData("read")]
    [InlineData("url")]
    public async Task Safe_policy_denies_ambient_operations(string kind)
    {
        var policy = CopilotSafePermissionPolicy.Create(new HashSet<string>(StringComparer.Ordinal) { "get_weather" });

        var decision = await policy(new PermissionRequest { Kind = kind }, Invocation());

        Assert.Equal(CopilotSdkPermissionDecision.Deny, decision);
    }

    [Fact]
    public async Task Client_wires_the_safe_default_policy_when_none_is_configured()
    {
        var backend = new FakeCopilotBackend();
        backend.AddSession(new FakeCopilotSession
        {
            OnSend = (s, _) =>
            {
                s.EmitAll(SdkEvents.Delta("ok", "m1"), SdkEvents.Idle());
                return Task.CompletedTask;
            },
        });

        await using var client = TestChatClient.Create(backend);
        var tool = AIFunctionFactory.Create((string city) => city, "get_weather");
        await client.GetResponseAsync(TestExtensions.UserMessage("hi"), new ChatOptions { Tools = [tool] });

        var handler = backend.Calls[0].Parameters.PermissionHandler;
        Assert.Equal(
            CopilotSdkPermissionDecision.Approve,
            await handler(CustomTool("get_weather"), Invocation()));
        Assert.Equal(
            CopilotSdkPermissionDecision.Deny,
            await handler(new PermissionRequest { Kind = "shell" }, Invocation()));
    }

    [Fact]
    public async Task Client_uses_the_configured_permission_handler_when_supplied()
    {
        CopilotSdkPermissionHandler custom = (_, _) =>
            new ValueTask<CopilotSdkPermissionDecision>(
                CopilotSdkPermissionDecision.Approve);

        var backend = new FakeCopilotBackend();
        backend.AddSession(new FakeCopilotSession
        {
            OnSend = (s, _) =>
            {
                s.EmitAll(SdkEvents.Delta("ok", "m1"), SdkEvents.Idle());
                return Task.CompletedTask;
            },
        });

        await using var client = TestChatClient.Create(backend, new CopilotSdkConfiguration { PermissionHandler = custom });
        await client.GetResponseAsync(TestExtensions.UserMessage("hi"));

        Assert.Same(custom, backend.Calls[0].Parameters.PermissionHandler);
    }
}
