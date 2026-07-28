using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.Attributes.Tests;

public class AIToolCompositionTests
{
    [Fact]
    public void Classic_and_discovered_tools_coexist_in_enumerable()
    {
        var classicTool = AIFunctionFactory.Create(
            () => "2024-01-15",
            "get_current_date",
            "Gets the current date");

        IList<AITool> allTools = [classicTool, .. TestToolContext.Default.Tools];

        Assert.Equal(4, allTools.Count);
        Assert.Contains(allTools, t => t.Name == "test_tool");
        Assert.Contains(allTools, t => t.Name == "GetCount");
        Assert.Contains(allTools, t => t.Name == "async_tool");
        Assert.Contains(allTools, t => t.Name == "get_current_date");
    }

    [Fact]
    public void Ad_hoc_tools_can_be_spread_into_options()
    {
        var registeredTools = TestToolContext.Default.Tools;

        var adHocTool = AIFunctionFactory.Create(
            (string query) => $"Search results for: {query}",
            "search_web",
            "Searches the web for information");

        IList<AITool> allTools = [adHocTool, .. registeredTools];

        Assert.Equal(4, allTools.Count);
        Assert.Contains(allTools, t => t.Name == "test_tool");
        Assert.Contains(allTools, t => t.Name == "search_web");
    }

    [Fact]
    public void Classic_tools_registered_before_add_ai_tools_preserve_order()
    {
        var classic = AIFunctionFactory.Create(
            () => 42,
            "answer_everything",
            "The answer to everything");

        IList<AITool> allTools = [classic, .. TestToolContext.Default.Tools];

        Assert.Equal(4, allTools.Count);
        Assert.Equal("answer_everything", allTools[0].Name);
        Assert.Contains(allTools, t => t.Name == "test_tool");
    }
}
