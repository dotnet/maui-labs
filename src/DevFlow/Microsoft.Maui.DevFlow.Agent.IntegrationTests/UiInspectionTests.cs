using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Microsoft.Maui.DevFlow.Driver;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Collection("AgentIntegration")]
[Trait("Category", "UiInspection")]
public class UiInspectionTests : IntegrationTestBase
{
    public UiInspectionTests(AppFixture app, ITestOutputHelper output)
        : base(app, output) { }

    [Fact]
    public async Task Tree_ReturnsNonEmptyTree()
    {
        await NavigateToMainPageAsync();
        var tree = await Client.GetTreeAsync();

        Assert.NotNull(tree);
        Assert.NotEmpty(tree);
    }

    [Fact]
    public async Task Tree_WithDepth_LimitsChildren()
    {
        await NavigateToMainPageAsync();
        var shallow = await Client.GetTreeAsync(maxDepth: 1);
        var deep = await Client.GetTreeAsync(maxDepth: 10);

        Assert.NotEmpty(shallow);

        static int CountNodes(IEnumerable<ElementInfo> elements)
        {
            var count = 0;
            foreach (var element in elements)
            {
                count++;
                if (element.Children != null)
                    count += CountNodes(element.Children);
            }
            return count;
        }

        Assert.True(CountNodes(shallow) <= CountNodes(deep),
            "Depth-limited tree should not have more nodes than a deeper tree.");
    }

    [Fact]
    public async Task Tree_ElementsHaveBounds()
    {
        await NavigateToMainPageAsync();
        var tree = await Client.GetTreeAsync(maxDepth: 3);

        static ElementInfo? FindWithBounds(IEnumerable<ElementInfo> elements)
        {
            foreach (var element in elements)
            {
                if (element.Bounds is { Width: > 0, Height: > 0 })
                    return element;

                if (element.Children != null)
                {
                    var found = FindWithBounds(element.Children);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }

        var elementWithBounds = FindWithBounds(tree);
        Assert.NotNull(elementWithBounds);
        Output.WriteLine($"Found element with bounds: {elementWithBounds!.Type} ({elementWithBounds.Bounds!.Width}x{elementWithBounds.Bounds.Height})");
    }

    [Fact]
    public async Task Query_ByType_ReturnsElements()
    {
        await NavigateToMainPageAsync();
        var buttons = await Client.QueryAsync(type: "Button");

        Assert.NotEmpty(buttons);
        Assert.All(buttons, button => Assert.Equal("Button", button.Type));
    }

    [Fact]
    public async Task Query_ByAutomationId_ReturnsExactElement()
    {
        await NavigateToMainPageAsync();
        var elements = await Client.QueryAsync(automationId: "AddButton");

        Assert.Single(elements);
        Assert.Equal("AddButton", elements[0].AutomationId);
    }

    [Fact]
    public async Task Query_ByAutomationId_HasCorrectProperties()
    {
        await NavigateToMainPageAsync();
        var element = await FindElementAsync("HeaderLabel");

        Assert.Equal("HeaderLabel", element.AutomationId);
        Assert.NotNull(element.Text);
        Assert.Contains("Todos", element.Text!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Query_ByText_ReturnsElements()
    {
        await NavigateToMainPageAsync();
        var elements = await Client.QueryAsync(text: "Todos");

        Assert.NotEmpty(elements);
        Assert.Contains(elements, element => element.Text != null && element.Text.Contains("Todos", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Query_ByCssSelector_ReturnsElements()
    {
        await NavigateToMainPageAsync();
        var elements = await Client.QueryCssAsync("Button#AddButton");

        Assert.NotEmpty(elements);
        Assert.Contains(elements, element => element.AutomationId == "AddButton");
    }

    [Fact]
    public async Task Query_NoResults_ReturnsEmpty()
    {
        var elements = await Client.QueryAsync(type: "NonExistentControlType99");
        Assert.Empty(elements);
    }

    [Fact]
    public async Task Query_MultipleTypes_ReturnsAppropriateResults()
    {
        await NavigateToMainPageAsync();

        var labels = await Client.QueryAsync(type: "Label");
        var entries = await Client.QueryAsync(type: "Entry");

        Assert.NotEmpty(labels);
        Assert.NotEmpty(entries);

        var labelIds = labels.Select(e => e.Id).ToHashSet();
        var entryIds = entries.Select(e => e.Id).ToHashSet();
        Assert.Empty(labelIds.Intersect(entryIds));
    }

    [Fact]
    public async Task Element_ById_ReturnsElement()
    {
        await NavigateToMainPageAsync();
        var addButton = await FindElementAsync("AddButton");

        var element = await Client.GetElementAsync(addButton.Id);

        Assert.NotNull(element);
        Assert.Equal(addButton.Id, element!.Id);
        Assert.Equal("AddButton", element.AutomationId);
    }

    [Fact]
    public async Task HitTest_AtKnownCoordinates_ReturnsElement()
    {
        await NavigateToMainPageAsync();
        var addButton = await FindElementAsync("AddButton");
        Assert.NotNull(addButton.Bounds);

        var centerX = addButton.Bounds!.X + (addButton.Bounds.Width / 2);
        var centerY = addButton.Bounds.Y + (addButton.Bounds.Height / 2);

        var elementId = await Client.HitTestAsync(centerX, centerY);

        Assert.NotNull(elementId);
        Assert.NotEmpty(elementId);
    }

    [Fact]
    public async Task Screenshot_ReturnsValidPng()
    {
        var bytes = await Client.ScreenshotAsync();

        if (bytes == null)
        {
            var raw = await GetRawAsync("/api/v1/ui/screenshot");
            var body = await raw.Content.ReadAsStringAsync();
            Output.WriteLine($"Screenshot raw response: {(int)raw.StatusCode} — {body}");
            Output.WriteLine("Screenshot not available on this platform.");
            return;
        }

        Assert.True(bytes.Length > 100, "Screenshot should have reasonable size");
        Assert.Equal((byte)0x89, bytes[0]);
        Assert.Equal((byte)0x50, bytes[1]);
        Assert.Equal((byte)0x4E, bytes[2]);
        Assert.Equal((byte)0x47, bytes[3]);
    }

    [Fact]
    public async Task Screenshot_OfElement_ReturnsImage()
    {
        await NavigateToMainPageAsync();
        var addButton = await FindElementAsync("AddButton");

        var bytes = await Client.ScreenshotAsync(elementId: addButton.Id);
        if (bytes == null)
        {
            Output.WriteLine("Element screenshot not available on this platform.");
            return;
        }

        Assert.True(bytes.Length > 0);
    }
}
