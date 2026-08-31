using Microsoft.Maui.DevFlow.Testing;
using FlowMcpTools = Microsoft.Maui.Cli.DevFlow.Flows.FlowTools;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>Unit tests for the flow-test <c>.md</c> format (parse/serialize) and validation.</summary>
public class FlowFormatTests
{
    [Fact]
    public void Validator_RejectsUnsupportedFutureSchema()
    {
        var flow = new MauiFlow
        {
            Schema = MauiFlow.CurrentSchema + 1,
            Steps =
            [
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Tap,
                    Target = new FlowSelector { AutomationId = "Button" }
                }
            ]
        };

        var validation = FlowValidator.Validate(flow);

        Assert.False(validation.Ok);
        Assert.Contains(validation.Errors, error =>
            error.Contains("newer than supported", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validator_RejectsInvalidLowerSchemas(int schema)
    {
        var flow = new MauiFlow
        {
            Schema = schema,
            Steps =
            [
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Back
                }
            ]
        };

        var validation = FlowValidator.Validate(flow);

        Assert.False(validation.Ok);
        Assert.Contains(validation.Errors, error =>
            error.Contains("schema", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_RejectsEmptyFlow()
    {
        var validation = FlowValidator.Validate(new MauiFlow());

        Assert.False(validation.Ok);
        Assert.Contains(validation.Errors, error =>
            error.Contains("at least one step", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task McpValidate_RejectsEmptyFlow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"empty-flow-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(
            path,
            FlowMarkdown.Serialize(new MauiFlow { Name = "empty" }));
        try
        {
            var json = await FlowMcpTools.Validate(null!, path);
            using var result = System.Text.Json.JsonDocument.Parse(json);

            Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
            Assert.Contains(
                result.RootElement.GetProperty("errors").EnumerateArray(),
                error => error.GetString()?.Contains(
                    "at least one step",
                    StringComparison.OrdinalIgnoreCase) == true);
        }
        finally
        {
            File.Delete(path);
        }
    }

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

    [Theory]
    [InlineData("""{"name":"missing schema","steps":[]}""", "schema")]
    [InlineData("""{"schema":2,"name":"missing steps"}""", "steps")]
    [InlineData("""{"schema":"2","name":"bad schema","steps":[]}""", "schema")]
    public void Parse_RequiresExplicitSchemaAndSteps(string json, string expected)
    {
        var result = FlowMarkdown.Parse($"```json maui-test\n{json}\n```");

        Assert.False(result.Ok);
        Assert.Contains(expected, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_LegacySchemaVersion_NormalizesToIntegerSchema()
    {
        var result = FlowMarkdown.Parse("""
            ```json maui-test
            {"schemaVersion":1,"name":"legacy","steps":[{"seq":1,"action":"back"}]}
            ```
            """);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(1, result.Flow!.Schema);
        Assert.Single(result.Flow.Steps);
    }

    [Fact]
    public void Parse_LegacySchemaVersionWithoutIntegerValue_FailsWithMigrationGuidance()
    {
        var result = FlowMarkdown.Parse("""
            ```json maui-test
            {"schemaVersion":"one","name":"legacy","steps":[{"seq":1,"action":"back"}]}
            ```
            """);

        Assert.False(result.Ok);
        Assert.Contains("Replace schemaVersion with schema", result.Error, StringComparison.Ordinal);
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
    public void Schema2_SelectorDiagnostics_RoundTripWhileSchema1RemainsReadable()
    {
        var flow = new MauiFlow
        {
            Schema = 2,
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Tap,
                    Args = new FlowStepArgs
                    {
                        Selector = new FlowSelector
                        {
                            AutomationId = "save",
                            MatchCount = 1,
                            Quality = "durable"
                        }
                    }
                }
            }
        };

        var parsed = FlowMarkdown.Parse(FlowMarkdown.Serialize(flow));

        Assert.True(parsed.Ok, parsed.Error);
        Assert.Equal(2, parsed.Flow!.Schema);
        Assert.Equal(1, parsed.Flow.Steps[0].Args!.Selector!.MatchCount);
        Assert.Equal("durable", parsed.Flow.Steps[0].Args!.Selector!.Quality);
        Assert.True(FlowValidator.Validate(FlowMarkdown.Parse(SampleMd).Flow!).Ok);
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

    [Fact]
    public void Validate_NotExistsHardAssert_RequiresSelector()
    {
        var flow = new MauiFlow
        {
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = "assert",
                    Asserts = new() { new FlowAssert { Kind = "notExists", Verify = true } },
                },
            },
        };

        var validation = FlowValidator.Validate(flow);

        Assert.False(validation.Ok);
        Assert.Contains(validation.Errors, error => error.Contains("notExists assertion requires a selector"));
    }

    [Fact]
    public void Validate_NotExistsHardAssertWithSelector_IsAccepted()
    {
        var flow = new MauiFlow
        {
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = "assert",
                    Asserts = new()
                    {
                        new FlowAssert
                        {
                            Kind = "notExists",
                            Verify = true,
                            Selector = new FlowSelector { AutomationId = "dismissed-dialog" },
                        },
                    },
                },
            },
        };

        var validation = FlowValidator.Validate(flow);

        Assert.True(validation.Ok, string.Join("; ", validation.Errors));
    }

    [Theory]
    [InlineData("customCheck", "unknown assert kind")]
    [InlineData("routeIs", "requires an expected route")]
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

    [Fact]
    public void Validate_DuplicateStepId_IsError()
    {
        var flow = new MauiFlow
        {
            Steps =
            {
                new FlowStep { Seq = 1, StepId = "save-order", Action = FlowActions.Assert },
                new FlowStep { Seq = 2, StepId = "save-order", Action = FlowActions.Assert },
            },
        };

        var validation = FlowValidator.Validate(flow);

        Assert.False(validation.Ok);
        Assert.Contains(validation.Errors, error => error.Contains("duplicate stepId", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("1", "numeric stepId")]
    [InlineData(" save-order", "leading or trailing whitespace")]
    [InlineData("save/order", "contain only")]
    [InlineData("step-0001", "sequence-shaped stepId")]
    public void Validate_UnsafeOrCollidingStepId_IsError(string stepId, string expectedError)
    {
        var flow = new MauiFlow
        {
            Steps =
            {
                new FlowStep { Seq = 1, StepId = "first-step", Action = FlowActions.Assert },
                new FlowStep { Seq = 2, StepId = stepId, Action = FlowActions.Assert },
            },
        };

        var validation = FlowValidator.Validate(flow);

        Assert.False(validation.Ok);
        Assert.Contains(validation.Errors, error => error.Contains(expectedError, StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_DuplicateSequence_IsError()
    {
        var flow = new MauiFlow
        {
            Steps =
            {
                new FlowStep { Seq = 1, StepId = "first-step", Action = FlowActions.Assert },
                new FlowStep { Seq = 1, StepId = "second-step", Action = FlowActions.Assert },
            },
        };

        var validation = FlowValidator.Validate(flow);

        Assert.False(validation.Ok);
        Assert.Contains(validation.Errors, error => error.Contains("duplicate seq", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveSequence_IsError(int sequence)
    {
        var flow = new MauiFlow
        {
            Steps =
            {
                new FlowStep { Seq = sequence, Action = FlowActions.Assert },
            },
        };

        var validation = FlowValidator.Validate(flow);

        Assert.False(validation.Ok);
        Assert.Contains(validation.Errors, error => error.Contains("positive integer", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("criterion/unsafe")]
    public void Validate_InvalidAcceptanceCriterionId_IsError(string criterionId)
    {
        var flow = new MauiFlow
        {
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Assert,
                    AcceptanceCriterionIds = [criterionId],
                },
            },
        };

        var validation = FlowValidator.Validate(flow);

        Assert.False(validation.Ok);
        Assert.Contains(
            validation.Errors,
            error => error.Contains("acceptanceCriterionIds", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_CanonicalRecorderStepIds_AreAccepted()
    {
        var flow = new MauiFlow
        {
            Steps =
            {
                new FlowStep { Seq = 1, StepId = "step-0001", Action = FlowActions.Assert },
                new FlowStep { Seq = 2, StepId = "step-0002", Action = FlowActions.Assert },
            },
        };

        var validation = FlowValidator.Validate(flow);

        Assert.True(validation.Ok, string.Join("; ", validation.Errors));
    }
}
