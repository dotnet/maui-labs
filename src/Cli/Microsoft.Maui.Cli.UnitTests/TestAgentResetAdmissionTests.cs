using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Microsoft.Maui.DevFlow.Testing;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// Live validation proves selectors resolve. It cannot issue a reset, so a plan that declares a
/// reset contract no owner can prove used to validate clean, consume a commit approval and a
/// one-shot run grant, and only then fail admission. These pin the warning that prevents that.
/// </summary>
public class TestAgentResetAdmissionTests
{
    private static JsonElement Describe(MauiTestPlan? plan)
        => JsonSerializer.SerializeToElement(TestAgentValidationTool.DescribeResetAdmission(plan));

    [Fact]
    public void NoResetRequired_IsAdmissibleWithoutAnOwner()
    {
        var plan = new MauiTestPlan { Reset = new MauiTestResetRequirement { Required = false } };

        var described = Describe(plan);

        Assert.False(described.GetProperty("resetRequired").GetBoolean());
        Assert.True(described.GetProperty("admissible").GetBoolean());
    }

    [Fact]
    public void MissingPlan_IsTreatedAsNoResetRequirement()
    {
        var described = Describe(null);

        Assert.False(described.GetProperty("resetRequired").GetBoolean());
    }

    [Fact]
    public void ResetRequired_DoesNotClaimAdmissibleAndNamesTheGrantCost()
    {
        var plan = new MauiTestPlan
        {
            Reset = new MauiTestResetRequirement
            {
                Required = true,
                Strategy = "host-owned",
                ResetIdentity = "devflow-sample-v1",
            },
        };

        var described = Describe(plan);

        Assert.True(described.GetProperty("resetRequired").GetBoolean());
        // Never asserts admissibility it cannot prove.
        Assert.Equal(JsonValueKind.Null, described.GetProperty("admissible").ValueKind);
        Assert.Equal("devflow-sample-v1", described.GetProperty("resetIdentity").GetString());
        var note = described.GetProperty("note").GetString();
        Assert.Contains("fails closed", note, StringComparison.Ordinal);
        Assert.Contains("one-shot grant is consumed", note, StringComparison.Ordinal);
    }

    [Fact]
    public void ResetRequired_WithBlankContract_ListsWhatIsMissing()
    {
        var plan = new MauiTestPlan
        {
            Reset = new MauiTestResetRequirement { Required = true, Strategy = "", ResetIdentity = null },
        };

        var described = Describe(plan);
        var missing = described.GetProperty("declaredMissing")
            .EnumerateArray()
            .Select(static value => value.GetString())
            .ToArray();

        Assert.Contains("reset.strategy", missing);
        Assert.Contains("reset.resetIdentity", missing);
    }
}
