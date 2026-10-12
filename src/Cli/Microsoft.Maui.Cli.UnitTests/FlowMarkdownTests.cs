using Microsoft.Maui.Cli.DevFlow.Flows;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class FlowMarkdownTests
{
    [Fact]
    public void ParseAndSerialize_RoundTripNestedWorkflowMetadata()
    {
        const string markdown = """
            ```json maui-test
            {"schema":1,"name":"login","app":"Sample","steps":[
              {"seq":1,"action":"fill","target":{"typeIndex":{"type":"Entry","index":2}},
               "args":{"selector":{"automationId":"Username"},"text":"Ada","dx":1.5,"animated":false},
               "asserts":[{"kind":"propEquals","selector":{"automationId":"Username"},"name":"Text","expected":"Ada","verify":true}]}
            ]}
            ```
            """;

        var parsed = FlowMarkdown.Parse(markdown);
        Assert.True(parsed.Ok, parsed.Error);
        var serialized = FlowMarkdown.Serialize(parsed.Flow!);
        Assert.Contains("\n  \"schema\": 1,", serialized, StringComparison.Ordinal);
        var roundTrip = FlowMarkdown.Parse(serialized);

        Assert.True(roundTrip.Ok, roundTrip.Error);
        Assert.Equal("login", roundTrip.Flow!.Name);
        Assert.Equal("Sample", roundTrip.Flow.App);
        var step = Assert.Single(roundTrip.Flow.Steps);
        Assert.Equal(2, step.Target!.TypeIndex!.Index);
        Assert.Equal("Entry", step.Target.TypeIndex.Type);
        Assert.Equal("Username", step.Args!.Selector!.AutomationId);
        Assert.Equal("Ada", step.Args.Text);
        Assert.Equal(1.5, step.Args.Dx);
        Assert.False(step.Args.Animated);
        var assertion = Assert.Single(step.Asserts!);
        Assert.Equal("Username", assertion.Selector!.AutomationId);
        Assert.Equal("Ada", assertion.Expected);
        Assert.True(assertion.Verify);
    }

    [Fact]
    public void Parse_PreservesCaseInsensitivePropertyMatching()
    {
        var parsed = FlowMarkdown.Parse("""
            ```json maui-test
            {"SCHEMA":1,"NAME":"mixed case","STEPS":[{"SEQ":1,"ACTION":"tap","TARGET":{"AUTOMATIONID":"Submit"}}]}
            ```
            """);

        Assert.True(parsed.Ok, parsed.Error);
        Assert.Equal("mixed case", parsed.Flow!.Name);
        var step = Assert.Single(parsed.Flow.Steps);
        Assert.Equal("tap", step.Action);
        Assert.Equal("Submit", step.Target!.AutomationId);
    }
}
