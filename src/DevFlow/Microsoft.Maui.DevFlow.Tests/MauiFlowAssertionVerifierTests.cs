using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class MauiFlowAssertionVerifierTests
{
    [Fact]
    public async Task VerifyAsync_PropEquals_UsesRunnerComparisonRules()
    {
        var result = await MauiFlowAssertionVerifier.VerifyAsync(
            new AssertionDriver { PropertyValue = " 42 " },
            new FlowAssert
            {
                Kind = "propEquals",
                Verify = true,
                Selector = new FlowSelector { AutomationId = "save" },
                Name = "Text",
                Expected = "42",
            });

        Assert.True(result.Passed is true);
        Assert.Equal(1, result.MatchCount);
    }

    [Fact]
    public async Task VerifyAsync_PageChanged_IsObservationOnly()
    {
        var result = await MauiFlowAssertionVerifier.VerifyAsync(
            new AssertionDriver(),
            new FlowAssert { Kind = "pageChanged", Verify = false, Note = "Observed navigation." });

        Assert.True(result.ObservationOnly);
        Assert.True(result.Skipped);
        Assert.Null(result.Passed);
    }

    [Fact]
    public async Task VerifyAsync_AmbiguousSelector_DoesNotChooseFirstMatch()
    {
        var result = await MauiFlowAssertionVerifier.VerifyAsync(
            new AssertionDriver { DuplicateText = true },
            new FlowAssert
            {
                Kind = "exists",
                Verify = true,
                Selector = new FlowSelector { Text = "Save" },
            });

        Assert.True(result.Passed is false);
        Assert.Equal(2, result.MatchCount);
    }

    [Fact]
    public async Task VerifyAsync_NotExists_PassesOnlyWhenSelectorIsAbsent()
    {
        var absent = await MauiFlowAssertionVerifier.VerifyAsync(
            new AssertionDriver(),
            new FlowAssert
            {
                Kind = "notExists",
                Verify = true,
                Selector = new FlowSelector { AutomationId = "missing" },
            });
        var present = await MauiFlowAssertionVerifier.VerifyAsync(
            new AssertionDriver(),
            new FlowAssert
            {
                Kind = "notExists",
                Verify = true,
                Selector = new FlowSelector { AutomationId = "save" },
            });

        Assert.True(absent.Passed is true);
        Assert.Equal(0, absent.MatchCount);
        Assert.True(present.Passed is false);
        Assert.Equal(1, present.MatchCount);
    }

    [Fact]
    public async Task VerifyAsync_PropEquals_RetriesUntilUiSettles()
    {
        var driver = new AssertionDriver();
        driver.PropertyValues.Enqueue("6 items, 2 completed");
        driver.PropertyValues.Enqueue("6 items, 2 completed");
        driver.PropertyValues.Enqueue("7 items, 2 completed");

        var result = await MauiFlowAssertionVerifier.VerifyAsync(
            driver,
            new FlowAssert
            {
                Kind = "propEquals",
                Verify = true,
                Selector = new FlowSelector { AutomationId = "save" },
                Name = "Text",
                Expected = "7 items, 2 completed",
            },
            pollTries: 3,
            pollGapMs: 0);

        Assert.True(result.Passed is true);
        Assert.Equal("7 items, 2 completed", result.Actual);
    }

    private sealed class AssertionDriver : IMauiFlowDriver
    {
        private readonly ElementInfo _first = new()
        {
            Id = "save-1",
            AutomationId = "save",
            Text = "Save",
            Type = "Button",
            IsVisible = true,
            IsEnabled = true,
            Bounds = new BoundsInfo { X = 0, Y = 0, Width = 20, Height = 20 },
        };

        public string? PropertyValue { get; set; } = "value";
        public Queue<string?> PropertyValues { get; } = new();
        public bool DuplicateText { get; set; }
        public WorkflowCommandReceipt? LastWorkflowCommandReceipt => null;

        public Task<List<ElementInfo>> QueryAsync(string? type = null, string? automationId = null, string? text = null)
        {
            var matches = new List<ElementInfo>();
            if ((automationId is null || automationId == _first.AutomationId) &&
                (text is null || text == _first.Text))
            {
                matches.Add(_first);
                if (DuplicateText && text is not null)
                {
                    matches.Add(new ElementInfo
                    {
                        Id = "save-2",
                        Text = "Save",
                        Type = "Button",
                        IsVisible = true,
                        IsEnabled = true,
                        Bounds = new BoundsInfo { X = 25, Y = 0, Width = 20, Height = 20 },
                    });
                }
            }
            return Task.FromResult(matches);
        }

        public Task<ElementInfo?> GetElementAsync(string id)
            => Task.FromResult<ElementInfo?>(id == _first.Id ? _first : null);

        public Task<bool> TapAsync(string elementId) => Task.FromResult(true);
        public Task<bool> FillAsync(string elementId, string text) => Task.FromResult(true);
        public Task<bool> SetPropertyAsync(string elementId, string propertyName, string value) => Task.FromResult(true);
        public Task<bool> ScrollAsync(string? elementId = null, double deltaX = 0, double deltaY = 0, bool animated = true, int? itemIndex = null, string? scrollToPosition = null) => Task.FromResult(true);
        public Task<bool> NavigateAsync(string route) => Task.FromResult(true);
        public Task<bool> BackAsync() => Task.FromResult(true);
        public Task<ThemeResult> SetThemeAsync(DevFlowTheme theme) => Task.FromResult(new ThemeResult { Success = true });
        public Task<string?> GetPropertyAsync(string elementId, string propertyName)
            => Task.FromResult(PropertyValues.Count > 0 ? PropertyValues.Dequeue() : PropertyValue);
        public Task<AgentStatus?> GetStatusAsync() => Task.FromResult<AgentStatus?>(new AgentStatus { Route = "/home" });
    }
}
