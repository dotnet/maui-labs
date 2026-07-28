using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.Attributes.Tests;

public class AIFunctionDiscoveryTests
{
    [Fact]
    public void Discovers_three_tools_from_test_service()
    {
        var tools = TestToolContext.Default.Tools;

        Assert.Equal(3, tools.Count);
    }

    [Fact]
    public void Uses_custom_name_from_attribute()
    {
        var tools = TestToolContext.Default.Tools;

        Assert.Contains(tools, t => t.Name == "test_tool");
    }

    [Fact]
    public void Falls_back_to_method_name_when_no_name_is_set()
    {
        var tools = TestToolContext.Default.Tools;

        Assert.Contains(tools, t => t.Name == "GetCount");
    }

    [Fact]
    public void Uses_description_from_attribute()
    {
        var tool = TestToolContext.Default.Tools.First(t => t.Name == "test_tool");

        Assert.Equal("A test tool", tool.Description);
    }

    [Fact]
    public void Uses_description_fallback_from_description_attribute()
    {
        var tool = DescriptionFallbackToolContext.Default.Tools.First(t => t.Name == "fallback_desc");

        Assert.Equal("Method-level description from DescriptionAttribute", tool.Description);
    }

    [Fact]
    public void Ignores_methods_without_export_attribute()
    {
        var tools = TestToolContext.Default.Tools;

        Assert.DoesNotContain(tools, t => t.Name == "InternalMethod");
    }

    // Types_without_exported_functions and Abstract_types scenarios are
    // compile-time concerns — tested in GeneratorCompilationTests
    // (EmptyToolSource, MAUIAI003 diagnostic).
}
