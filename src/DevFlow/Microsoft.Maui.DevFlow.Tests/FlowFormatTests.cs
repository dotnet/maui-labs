using Microsoft.Maui.Cli.DevFlow.Flows;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>Unit tests for the flow-test <c>.md</c> format (parse/serialize) and validation.</summary>
public class FlowFormatTests
{
    private const string SampleMd = """
# Scenario: login

- **App:** Demo
- **Steps:** 2

## Steps

```json maui-test
{
  "schema": 1,
  "name": "login",
  "app": "Demo",
  "platform": "windows",
  "steps": [
    {
      "seq": 1,
      "action": "tap",
      "target": { "automationId": "submit" },
      "args": { "selector": { "automationId": "submit" } },
      "asserts": [ { "kind": "exists", "selector": { "automationId": "submit" }, "verify": true } ]
    },
    {
      "seq": 2,
      "action": "fill",
      "target": { "automationId": "name" },
      "value": "hello",
      "args": { "selector": { "automationId": "name" }, "text": "hello" },
      "asserts": [ { "kind": "propEquals", "selector": { "automationId": "name" }, "name": "Text", "expected": "hello", "verify": true } ]
    }
  ]
}
```
""";

    [Fact]
    public void Parse_ExtractsSteps()
    {
        var r = FlowMarkdown.Parse(SampleMd);
        Assert.True(r.Ok, r.Error);
        Assert.Equal("login", r.Flow!.Name);
        Assert.Equal(2, r.Flow.Steps.Count);
        Assert.Equal("tap", r.Flow.Steps[0].Action);
        Assert.Equal("submit", r.Flow.Steps[0].Args!.Selector!.AutomationId);
        Assert.Equal("fill", r.Flow.Steps[1].Action);
        Assert.Equal("hello", r.Flow.Steps[1].Args!.Text);
        Assert.Equal("propEquals", r.Flow.Steps[1].Asserts![0].Kind);
        Assert.True(r.Flow.Steps[1].Asserts![0].Verify);
    }

    [Fact]
    public void Parse_NoBlock_Fails()
    {
        var r = FlowMarkdown.Parse("# Just prose, no test block.");
        Assert.False(r.Ok);
        Assert.Contains("maui-test", r.Error);
    }

    [Fact]
    public void Parse_MultipleBlocks_Fails()
    {
        var md = SampleMd + "\n\n" + SampleMd;
        var r = FlowMarkdown.Parse(md);
        Assert.False(r.Ok);
        Assert.Contains("exactly one", r.Error);
    }

    [Fact]
    public void Parse_BadJson_Fails()
    {
        var md = "```json maui-test\n{ not json }\n```";
        var r = FlowMarkdown.Parse(md);
        Assert.False(r.Ok);
        Assert.Contains("Invalid JSON", r.Error);
    }

    [Fact]
    public void Serialize_RoundTrips()
    {
        var original = FlowMarkdown.Parse(SampleMd).Flow!;
        var md = FlowMarkdown.Serialize(original);
        Assert.Contains("```json maui-test", md);
        Assert.Contains("# Scenario: login", md);

        var reparsed = FlowMarkdown.Parse(md);
        Assert.True(reparsed.Ok, reparsed.Error);
        Assert.Equal(original.Steps.Count, reparsed.Flow!.Steps.Count);
        Assert.Equal(original.Steps[1].Args!.Text, reparsed.Flow.Steps[1].Args!.Text);
    }

    [Fact]
    public void Validate_ValidFlow_HasNoErrors()
    {
        var flow = FlowMarkdown.Parse(SampleMd).Flow!;
        var v = FlowValidator.Validate(flow);
        Assert.True(v.Ok, string.Join("; ", v.Errors));
    }

    [Fact]
    public void Validate_UnknownAction_IsError()
    {
        var flow = new MauiFlow { Steps = { new FlowStep { Seq = 1, Action = "frobnicate" } } };
        var v = FlowValidator.Validate(flow);
        Assert.False(v.Ok);
        Assert.Contains(v.Errors, e => e.Contains("unknown action"));
    }

    [Fact]
    public void Validate_TapWithoutSelector_IsError()
    {
        var flow = new MauiFlow { Steps = { new FlowStep { Seq = 1, Action = "tap" } } };
        var v = FlowValidator.Validate(flow);
        Assert.False(v.Ok);
        Assert.Contains(v.Errors, e => e.Contains("missing a target selector"));
    }

    [Fact]
    public void Validate_UnknownTheme_IsError()
    {
        var flow = new MauiFlow { Steps = { new FlowStep { Seq = 1, Action = "setTheme", Args = new FlowStepArgs { Theme = "purple" } } } };
        var v = FlowValidator.Validate(flow);
        Assert.False(v.Ok);
        Assert.Contains(v.Errors, e => e.Contains("light|dark|system"));
    }

    [Fact]
    public void Validate_FragileSelector_IsWarning()
    {
        var flow = new MauiFlow
        {
            Steps = { new FlowStep { Seq = 1, Action = "tap", Fragile = true, Args = new FlowStepArgs { Selector = new FlowSelector { Id = "elem_3" } } } },
        };
        var v = FlowValidator.Validate(flow);
        Assert.True(v.Ok, string.Join("; ", v.Errors));
        Assert.Contains(v.Warnings, w => w.Contains("fragile"));
    }

    [Fact]
    public void Validate_HardAssertWithoutSelector_IsError()
    {
        var flow = new MauiFlow
        {
            Steps =
            {
                new FlowStep
                {
                    Seq = 1, Action = "tap",
                    Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "ok" } },
                    Asserts = new() { new FlowAssert { Kind = "propEquals", Verify = true } },
                },
            },
        };
        var v = FlowValidator.Validate(flow);
        Assert.False(v.Ok);
        Assert.Contains(v.Errors, e => e.Contains("requires a selector"));
        Assert.Contains(v.Errors, e => e.Contains("requires a property name"));
    }

    [Theory]
    [InlineData("customCheck", "unknown assert kind")]
    [InlineData("routeIs", "report-only")]
    [InlineData("pageChanged", "report-only")]
    public void Validate_UnsupportedHardAssert_IsError(string kind, string expectedError)
    {
        var flow = new MauiFlow
        {
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = "assert",
                    Asserts = new() { new FlowAssert { Kind = kind, Verify = true } },
                },
            },
        };

        var validation = FlowValidator.Validate(flow);

        Assert.False(validation.Ok);
        Assert.Contains(validation.Errors, error => error.Contains(expectedError));
    }

    [Fact]
    public void Validate_UnknownReportOnlyAssert_RemainsWarning()
    {
        var flow = new MauiFlow
        {
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = "assert",
                    Asserts = new() { new FlowAssert { Kind = "futureNote", Verify = false } },
                },
            },
        };

        var validation = FlowValidator.Validate(flow);

        Assert.True(validation.Ok, string.Join("; ", validation.Errors));
        Assert.Contains(validation.Warnings, warning => warning.Contains("unknown assert kind"));
    }

    [Fact]
    public void Validate_NoOpScroll_IsWarning()
    {
        var flow = new MauiFlow { Steps = { new FlowStep { Seq = 1, Action = "scroll", Args = new FlowStepArgs() } } };
        var v = FlowValidator.Validate(flow);
        Assert.True(v.Ok, string.Join("; ", v.Errors));
        Assert.Contains(v.Warnings, w => w.Contains("no-op"));
    }

    [Fact]
    public void Validate_ScrollWithOnlyStaleElement_IsWarning()
    {
        // args.element is a stale runtime id that replay ignores (it re-resolves target) — no-op.
        var flow = new MauiFlow { Steps = { new FlowStep { Seq = 1, Action = "scroll", Args = new FlowStepArgs { Element = "scrollViewId" } } } };
        var v = FlowValidator.Validate(flow);
        Assert.True(v.Ok, string.Join("; ", v.Errors));
        Assert.Contains(v.Warnings, w => w.Contains("no-op"));
    }
}
