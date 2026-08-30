using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// Live validation used to report a bare match count, so a selector matching three controls read
/// exactly like one matching a single control. Observed against the sample app: two steps
/// addressing `TodoCheckBox` and `DeleteButton` — each repeated once per todo row — validated with
/// `matchCount: 3`, no warning, and `admissible: true`, so nothing told the authoring agent there
/// was a choice for the human to make.
/// </summary>
public class SelectorResolutionClassificationTests
{
    [Fact]
    public void OneMatch_IsUniqueAndNeedsNoConversation()
    {
        var (status, resolution) = TestAgentValidationTool.ClassifySelectorResolution(1);

        Assert.Equal("unique", status);
        Assert.Null(resolution);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(17)]
    public void SeveralMatches_AreAmbiguousAndAskTheHuman(int matchCount)
    {
        var (status, resolution) = TestAgentValidationTool.ClassifySelectorResolution(matchCount);

        Assert.Equal("ambiguous", status);
        // The manager's worked example: several controls share an AutomationId, and the model must
        // come back and ask which one the test means rather than resolving by position.
        Assert.Contains("Ask the human", resolution!, StringComparison.Ordinal);
        Assert.Contains("document order", resolution!, StringComparison.Ordinal);
    }

    [Fact]
    public void NoMatch_IsUnresolvedRatherThanAmbiguous()
    {
        // A selector that matches nothing is a different conversation from one that matches many:
        // the first is a wrong route or a wrong id, the second is an undeclared choice.
        var (status, resolution) = TestAgentValidationTool.ClassifySelectorResolution(0);

        Assert.Equal("unresolved", status);
        Assert.Contains("matches nothing", resolution!, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryOutcomeIsDistinguishable()
    {
        // The defect was that all three outcomes were indistinguishable to a reader.
        var statuses = new[] { 0, 1, 2 }
            .Select(TestAgentValidationTool.ClassifySelectorResolution)
            .Select(static verdict => verdict.Status)
            .ToArray();

        Assert.Equal(statuses.Length, statuses.Distinct(StringComparer.Ordinal).Count());
    }
}
