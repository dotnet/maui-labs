using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Sdk;

namespace Microsoft.Maui.DevFlow.Inspector.Tests;

public class LayoutQaSessionTests
{
    [Theory]
    [InlineData(null, "12345", false)]
    [InlineData("0", "12345", false)]
    [InlineData("1", null, false)]
    [InlineData("1", "invalid", false)]
    [InlineData("1", "0", false)]
    [InlineData("1", "65536", false)]
    [InlineData("1", "12345", true)]
    public void LayoutQaFact_GeneralLiveOptInDoesNotReplaceDedicatedPrerequisites(
        string? qaOptIn, string? port, bool enabled)
    {
        var skip = LayoutQaFactAttribute.GetSkipReason(name => name switch
        {
            "MAUI_INSPECTOR_LIVE_TESTS" => "1",
            "MAUI_INSPECTOR_LAYOUT_QA_TESTS" => qaOptIn,
            "LAYOUT_QA_AGENT_PORT" => port,
            _ => null
        });
        Assert.Equal(enabled, skip is null);
        var method = typeof(InspectorPageTests).GetMethod(
            nameof(InspectorPageTests.LayoutBaselineFindings_RespectOutcomeFilterAndHighlightParent))!;
        Assert.True(Attribute.IsDefined(method, typeof(LayoutQaFactAttribute)));
        Assert.False(Attribute.IsDefined(method, typeof(LiveInspectorFactAttribute)));
    }

    [Fact]
    public async Task CreateAsync_WrongAppRejectsTargetBeforeAnyMutation()
    {
        await using var agent = new InspectorLayoutWorkspaceTests.ShellAgent(appName: "Unrelated app");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => LayoutQaSession.CreateAsync(agent.Port));
        Assert.Contains(LayoutQaSession.AppName, exception.Message);
        var request = Assert.Single(agent.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/api/v1/agent/status", request.Path);
    }

    [Fact]
    public async Task CreateAsync_InspectorAndDirectClientUseTheSameVerifiedQaAgent()
    {
        await using var unrelated = new InspectorLayoutWorkspaceTests.ShellAgent(appName: "Unrelated app");
        await using var agent = new InspectorLayoutWorkspaceTests.ShellAgent(appName: LayoutQaSession.AppName);
        await using var qa = await LayoutQaSession.CreateAsync(agent.Port);
        using var http = new HttpClient();
        var html = await http.GetStringAsync(qa.InspectorUrl);
        var inspectorAgentPort = Regex.Match(html,
            "<meta\\s+name=\"devflow-agent-port\"\\s+content=\"([^\"]+)\"").Groups[1].Value;
        Assert.Equal(agent.Port.ToString(), inspectorAgentPort);
        Assert.Equal("/api/v1/agent/status", agent.Requests.First().Path);
        Assert.Contains(agent.Requests, request => request.Path == "/api/v1/ui/tree");
        Assert.Contains(agent.Requests, request => request.Path == "/api/v1/ui/screenshot");
        Assert.DoesNotContain(agent.Requests, request => request.Method == "POST");
        Assert.Empty(unrelated.Requests);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("assertion")]
    [InlineData("navigation")]
    [InlineData("exception")]
    public async Task PrepareAsync_SuccessOrFailureReleasesTheAcquiredLease(string outcome)
    {
        await using var agent = new InspectorLayoutWorkspaceTests.ShellAgent(
            appName: LayoutQaSession.AppName, failNavigation: outcome == "navigation");
        await using var qa = await LayoutQaSession.CreateAsync(agent.Port);
        var failure = await Record.ExceptionAsync(() => qa.PrepareAsync(async client =>
        {
            Assert.True(await client.NavigateAsync("//layoutdiagnostics"));
            Assert.Equal(client.MutationLeaseId, agent.HeldLeaseId);
            Assert.True(outcome != "assertion", "Synthetic report assertion failed.");
            if (outcome == "exception")
                throw new InvalidOperationException("Synthetic scan failure.");
        }));
        if (outcome == "success") Assert.Null(failure);
        else if (outcome == "exception") Assert.IsType<InvalidOperationException>(failure);
        else Assert.IsAssignableFrom<XunitException>(failure);

        var requests = agent.Requests.ToArray();
        Assert.Equal("/api/v1/agent/status", requests[0].Path);
        var leaseRequests = requests.Where(request => request.Path == "/api/v1/agent/lease").ToArray();
        Assert.Equal(2, leaseRequests.Length);
        using var claim = JsonDocument.Parse(leaseRequests[0].Body);
        using var release = JsonDocument.Parse(leaseRequests[1].Body);
        Assert.Equal("claim", claim.RootElement.GetProperty("action").GetString());
        Assert.Equal("release", release.RootElement.GetProperty("action").GetString());
        var leaseId = claim.RootElement.GetProperty("leaseId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(leaseId));
        Assert.Equal(leaseId, release.RootElement.GetProperty("leaseId").GetString());
        Assert.Equal("/api/v1/agent/lease", requests[^1].Path);
        Assert.Null(agent.HeldLeaseId);
    }
}
