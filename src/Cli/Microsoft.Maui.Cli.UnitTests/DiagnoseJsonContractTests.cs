// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.Models;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class DiagnoseJsonContractTests
{
    // Use the production source-gen context so tests catch issues that would
    // surface only at runtime under JsonSerializerIsReflectionEnabledByDefault=false.
    private static string Serialize(DiagnoseResult result) =>
        JsonSerializer.Serialize(result, DevFlowCliJsonContext.Default.DiagnoseResult);

    [Fact]
    public void TopLevel_HasVersionStatusMessageChecksKeys()
    {
        var result = new DiagnoseResult { Status = DiagnoseStatus.Passed, Message = "ok", Checks = [] };
        using var doc = JsonDocument.Parse(Serialize(result));
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("version", out var ver));
        Assert.Equal(1, ver.GetInt32());
        Assert.True(root.TryGetProperty("status", out _));
        Assert.True(root.TryGetProperty("message", out _));
        Assert.True(root.TryGetProperty("checks", out _));
    }

    [Fact]
    public void TopLevel_DoesNotHaveFlatLegacyKeys()
    {
        var result = new DiagnoseResult { Status = DiagnoseStatus.Passed, Message = "ok", Checks = [] };
        using var doc = JsonDocument.Parse(Serialize(result));
        var root = doc.RootElement;
        Assert.False(root.TryGetProperty("broker_running", out _));
        Assert.False(root.TryGetProperty("broker_port", out _));
        Assert.False(root.TryGetProperty("agent_count", out _));
        Assert.False(root.TryGetProperty("agents", out _));
        Assert.False(root.TryGetProperty("projects", out _));
        Assert.False(root.TryGetProperty("cli_version", out _));
    }

    [Fact]
    public void Check_HasIdNameStatusMessageKeys()
    {
        var check = new DiagnoseCheckResult { Id = "broker", Name = "DevFlow broker", Status = DiagnoseCheckStatus.Passed, Message = "Broker running on port 12345" };
        var result = new DiagnoseResult { Status = DiagnoseStatus.Passed, Message = "ok", Checks = [check] };
        using var doc = JsonDocument.Parse(Serialize(result));
        var checkEl = doc.RootElement.GetProperty("checks")[0];
        Assert.Equal("broker", checkEl.GetProperty("id").GetString());
        Assert.Equal("DevFlow broker", checkEl.GetProperty("name").GetString());
        Assert.Equal("passed", checkEl.GetProperty("status").GetString());
        Assert.Equal("Broker running on port 12345", checkEl.GetProperty("message").GetString());
    }

    [Fact]
    public void Check_Remediation_OmittedWhenNull()
    {
        var check = new DiagnoseCheckResult { Id = "agents", Name = "Connected agents", Status = DiagnoseCheckStatus.Passed, Message = "1 agent(s) connected" };
        var result = new DiagnoseResult { Status = DiagnoseStatus.Passed, Message = "ok", Checks = [check] };
        using var doc = JsonDocument.Parse(Serialize(result));
        Assert.False(doc.RootElement.GetProperty("checks")[0].TryGetProperty("remediation", out _));
    }

    [Fact]
    public void Check_Remediation_PresentWhenSet()
    {
        var check = new DiagnoseCheckResult
        {
            Id = "broker", Name = "DevFlow broker", Status = DiagnoseCheckStatus.Failed, Message = "Broker is not running",
            Remediation = new RemediationResult { Type = "command", Command = "maui devflow broker start" },
        };
        var result = new DiagnoseResult { Status = DiagnoseStatus.Failed, Message = "failed", Checks = [check] };
        using var doc = JsonDocument.Parse(Serialize(result));
        var checkEl = doc.RootElement.GetProperty("checks")[0];
        Assert.True(checkEl.TryGetProperty("remediation", out var rem));
        Assert.Equal("command", rem.GetProperty("type").GetString());
        Assert.Equal("maui devflow broker start", rem.GetProperty("command").GetString());
    }

    [Theory]
    [InlineData(DiagnoseStatus.Passed, "passed")]
    [InlineData(DiagnoseStatus.Warning, "warning")]
    [InlineData(DiagnoseStatus.Failed, "failed")]
    public void DiagnoseStatus_SerializesLowercase(DiagnoseStatus status, string expected)
    {
        var result = new DiagnoseResult { Status = status, Message = "m", Checks = [] };
        using var doc = JsonDocument.Parse(Serialize(result));
        Assert.Equal(expected, doc.RootElement.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(DiagnoseCheckStatus.Passed, "passed")]
    [InlineData(DiagnoseCheckStatus.Warning, "warning")]
    [InlineData(DiagnoseCheckStatus.Failed, "failed")]
    [InlineData(DiagnoseCheckStatus.Skipped, "skipped")]
    public void DiagnoseCheckStatus_SerializesLowercase(DiagnoseCheckStatus status, string expected)
    {
        var check = new DiagnoseCheckResult { Id = "x", Name = "X", Status = status, Message = "m" };
        var result = new DiagnoseResult { Status = DiagnoseStatus.Passed, Message = "ok", Checks = [check] };
        using var doc = JsonDocument.Parse(Serialize(result));
        Assert.Equal(expected, doc.RootElement.GetProperty("checks")[0].GetProperty("status").GetString());
    }

    [Fact]
    public void ComputeStatus_AllPassed_ReturnsPassed()
    {
        var checks = new[] { Check(DiagnoseCheckStatus.Passed), Check(DiagnoseCheckStatus.Passed) };
        Assert.Equal(DiagnoseStatus.Passed, DiagnoseResult.ComputeStatus(checks));
    }

    [Fact]
    public void ComputeStatus_AnyWarning_ReturnsWarning()
    {
        var checks = new[] { Check(DiagnoseCheckStatus.Passed), Check(DiagnoseCheckStatus.Warning) };
        Assert.Equal(DiagnoseStatus.Warning, DiagnoseResult.ComputeStatus(checks));
    }

    [Fact]
    public void ComputeStatus_AnyFailed_ReturnsFailed()
    {
        var checks = new[] { Check(DiagnoseCheckStatus.Warning), Check(DiagnoseCheckStatus.Failed) };
        Assert.Equal(DiagnoseStatus.Failed, DiagnoseResult.ComputeStatus(checks));
    }

    [Fact]
    public void ComputeStatus_SkippedOnly_ReturnsPassed()
    {
        var checks = new[] { Check(DiagnoseCheckStatus.Skipped) };
        Assert.Equal(DiagnoseStatus.Passed, DiagnoseResult.ComputeStatus(checks));
    }

    [Fact]
    public void Checks_PreservesOrder()
    {
        var checks = new[]
        {
            new DiagnoseCheckResult { Id = "broker", Name = "Broker", Status = DiagnoseCheckStatus.Passed, Message = "ok" },
            new DiagnoseCheckResult { Id = "agents", Name = "Agents", Status = DiagnoseCheckStatus.Warning, Message = "warn" },
            new DiagnoseCheckResult { Id = "devflow-projects", Name = "Projects", Status = DiagnoseCheckStatus.Passed, Message = "ok" },
        };
        var result = new DiagnoseResult { Status = DiagnoseStatus.Warning, Message = "m", Checks = checks };
        using var doc = JsonDocument.Parse(Serialize(result));
        var arr = doc.RootElement.GetProperty("checks");
        Assert.Equal("broker", arr[0].GetProperty("id").GetString());
        Assert.Equal("agents", arr[1].GetProperty("id").GetString());
        Assert.Equal("devflow-projects", arr[2].GetProperty("id").GetString());
    }

    [Fact]
    public void ComputeStatus_EmptyChecks_ReturnsPassed()
    {
        Assert.Equal(DiagnoseStatus.Passed, DiagnoseResult.ComputeStatus([]));
    }

    private static DiagnoseCheckResult Check(DiagnoseCheckStatus status) =>
        new() { Id = "x", Name = "X", Status = status, Message = "m" };
}