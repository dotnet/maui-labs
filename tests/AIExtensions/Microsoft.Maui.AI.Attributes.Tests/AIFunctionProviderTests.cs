using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.Attributes.Tests;

public class AIToolContextTests
{
    [Fact]
    public void Context_creates_tools_from_source_types()
    {
        var tools = TestToolContext.Default.Tools;

        Assert.Equal(3, tools.Count);
        Assert.Contains(tools, t => t.Name == "test_tool");
    }

    [Fact]
    public void Default_instance_returns_same_context()
    {
        var first = TestToolContext.Default;
        var second = TestToolContext.Default;

        Assert.Same(first, second);
    }

    [Fact]
    public void Context_with_multiple_sources_aggregates_tools()
    {
        var tools = CompositeToolContext.Default.Tools;

        Assert.Equal(4, tools.Count); // 3 from TestToolService + 1 from MultiParamService
        Assert.Contains(tools, t => t.Name == "test_tool");
        Assert.Contains(tools, t => t.Name == "multi_param");
    }
}
