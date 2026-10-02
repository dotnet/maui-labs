using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Flows;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Microsoft.Maui.Cli.DevFlow.Testing;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class Goal4ProfileTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false")]
    public void RestrictedProfile_IsOffByDefault(string? flag) =>
        Assert.Throws<InvalidOperationException>(() => McpServerHost.IsTestAgentProfile("test-agent", flag, null));

    [Fact]
    public void FullProfile_RemainsDefaultAndPreviewKillSwitchIsEnforced()
    {
        Assert.False(McpServerHost.IsTestAgentProfile("full", null, null));
        Assert.True(McpServerHost.IsTestAgentProfile("test-agent", "true", null));
        Assert.Throws<InvalidOperationException>(() =>
            McpServerHost.IsTestAgentProfile("test-agent", "true", "source-proposals, agent-authoring"));
        Assert.Throws<ArgumentException>(() => McpServerHost.IsTestAgentProfile("typo", "true", null));
    }

    [Fact]
    public void RestrictedToolInventory_HasOnlyInertAuthorAndValidate()
    {
        var methods = typeof(PreviewTestAgentTools).GetMethods()
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null).ToArray();
        Assert.Equal(new[] { "maui_test_author", "maui_test_validate" },
            methods.Select(method => method.GetCustomAttribute<McpServerToolAttribute>()!.Name).Order());
        foreach (var method in methods)
            Assert.All(method.GetParameters().Skip(1),
                parameter => Assert.NotNull(parameter.GetCustomAttribute<DescriptionAttribute>()));
    }

    [Fact]
    public void Draft_ReturnsDigestsButNoObservedTargetOrAuthority()
    {
        var json = JsonSerializer.Serialize(Goal4FoundationTests.Flow(), PreviewTestingJsonContext.Default.MauiFlow);
        var result = PreviewTestAgentTools.Author(new(), "begin", json,
            "agent", "instance", "build", "seed", "checkpoint", "none", "plan", 1);
        using var document = JsonDocument.Parse(result);
        Assert.Equal("inert-draft", document.RootElement.GetProperty("State").GetString());
        Assert.False(document.RootElement.GetProperty("NativeApproval").GetBoolean());
        Assert.False(document.RootElement.GetProperty("ExecutionAvailable").GetBoolean());
        Assert.False(document.RootElement.GetProperty("TargetObserved").GetBoolean());
        Assert.DoesNotContain("result", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Draft_AcceptsCurrentFlowSchema(bool author)
    {
        using var result = JsonDocument.Parse(CreateDraft(author, MauiFlow.CurrentSchema));
        Assert.Equal("inert-draft", result.RootElement.GetProperty("State").GetString());
    }

    [Theory]
    [InlineData(false, -1)]
    [InlineData(false, 1)]
    [InlineData(true, -1)]
    [InlineData(true, 1)]
    public void Draft_RejectsOtherSchemasAndNamesSupportedVersion(bool author, int offset)
    {
        var error = Assert.Throws<McpException>(() => CreateDraft(author, MauiFlow.CurrentSchema + offset));
        Assert.Equal($"invalid-request: unsupported flow schema; expected {MauiFlow.CurrentSchema}.", error.Message);
    }

    private static string CreateDraft(bool author, int schema)
    {
        var flow = Goal4FoundationTests.Flow();
        flow.Schema = schema;
        var json = JsonSerializer.Serialize(flow, PreviewTestingJsonContext.Default.MauiFlow);
        return author
            ? PreviewTestAgentTools.Author(new(), "begin", json,
                "agent", "instance", "build", "seed", "checkpoint", "none", "plan", 1)
            : PreviewTestAgentTools.Validate(new(), json,
                "agent", "instance", "build", "seed", "checkpoint", "none", "plan", 1);
    }

    [Theory]
    [InlineData("approval-request")]
    [InlineData("commit")]
    [InlineData("run")]
    [InlineData("repair")]
    public void AgentCannotRequestOrManufactureAuthority(string operation) =>
        Assert.Throws<McpException>(() => PreviewTestAgentTools.Author(new(), operation, "{}",
            "agent", "instance", "build", "seed", "checkpoint", "none", "plan", 1));

    [Theory]
    [InlineData("{\"schema\":1,\"schema\":1}")]
    [InlineData("{\"schema\":1,\"unsupported\":true}")]
    [InlineData("{\"schema\":1,\"steps\":null}")]
    [InlineData("null")]
    [InlineData("{\"steps\":[]}")]
    public void Validator_RejectsAmbiguousOrUnsupportedDocuments(string json) =>
        Assert.Throws<McpException>(() => PreviewTestAgentTools.Validate(new(), json,
            "agent", "instance", "build", "seed", "checkpoint", "none", "plan", 1));
}
