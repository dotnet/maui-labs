using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.Attributes.Tests;

public class AIFunctionRegistrationTests
{
    [Fact]
    public void Multiple_source_types_are_aggregated()
    {
        var tools = RegistrationTestToolContext.Default.Tools;

        Assert.True(tools.Count >= 4, $"Expected at least 4 tools, got {tools.Count}");
        Assert.Contains(tools, t => t.Name == "test_tool");
        Assert.Contains(tools, t => t.Name == "disposable_tool");
        Assert.Contains(tools, t => t.Name == "fallback_desc");
    }

    [Fact]
    public void Explicit_type_scanning_registers_expected_tools()
    {
        var tools = TestToolContext.Default.Tools;

        Assert.Equal(3, tools.Count);
    }

    [Fact]
    public void Default_context_instance_is_a_singleton()
    {
        var first = TestToolContext.Default;
        var second = TestToolContext.Default;
        Assert.Same(first, second);

        // Tools are cached — same list reference and same instances on repeated access.
        var toolsA = first.Tools;
        var toolsB = second.Tools;
        Assert.Same(toolsA, toolsB);
    }
}
