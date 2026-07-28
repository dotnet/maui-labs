using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.AI.Attributes;

namespace Microsoft.Maui.AI.Attributes.Tests;

/// <summary>
/// Runtime tests for static class, interface, and nested class tool contexts.
/// These verify that the generator-emitted code actually works at runtime —
/// tools are discovered, arguments are bound, services are resolved, and
/// results are returned correctly.
/// </summary>
public class StaticInterfaceNestedTests
{
    // ── Static class tools ──────────────────────────────────────────────

    [Fact]
    public void StaticClass_DiscoversTwoTools()
    {
        var tools = StaticMathToolContext.Default.Tools;
        Assert.Equal(2, tools.Count);
        Assert.Contains(tools, t => t.Name == "add_numbers");
        Assert.Contains(tools, t => t.Name == "negate_number");
    }

    [Fact]
    public async Task StaticClass_Add_InvokesWithoutServiceProvider()
    {
        var tool = StaticMathToolContext.Default.Tools.First(t => t.Name == "add_numbers") as AIFunction;
        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["a"] = 3,
            ["b"] = 7,
        });

        var result = await tool!.InvokeAsync(args);
        Assert.Equal(10, Convert.ToInt32(result));
    }

    [Fact]
    public async Task StaticClass_Negate_InvokesCorrectly()
    {
        var tool = StaticMathToolContext.Default.Tools.First(t => t.Name == "negate_number") as AIFunction;
        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["value"] = 42,
        });

        var result = await tool!.InvokeAsync(args);
        Assert.Equal(-42, Convert.ToInt32(result));
    }

    [Fact]
    public void StaticClass_Add_HasDescription()
    {
        var tool = StaticMathToolContext.Default.Tools.First(t => t.Name == "add_numbers");
        Assert.Equal("Adds two integers.", tool.Description);
    }

    // ── Mixed static + instance tools ───────────────────────────────────

    [Fact]
    public void MixedStaticInstance_DiscoversBothTools()
    {
        var tools = MixedStaticInstanceToolContext.Default.Tools;
        Assert.Equal(2, tools.Count);
        Assert.Contains(tools, t => t.Name == "static_echo");
        Assert.Contains(tools, t => t.Name == "instance_echo");
    }

    [Fact]
    public async Task MixedStaticInstance_StaticMethod_NeedsNoServiceProvider()
    {
        var tool = MixedStaticInstanceToolContext.Default.Tools.First(t => t.Name == "static_echo") as AIFunction;
        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["message"] = "hello",
        });

        var result = await tool!.InvokeAsync(args);
        Assert.Equal("static:hello", result?.ToString());
    }

    [Fact]
    public async Task MixedStaticInstance_InstanceMethod_ResolvesFromDI()
    {
        var svc = new MixedStaticInstanceService();
        var services = new ServiceCollection();
        services.AddSingleton(svc);
        using var provider = services.BuildServiceProvider();

        var tool = MixedStaticInstanceToolContext.Default.Tools.First(t => t.Name == "instance_echo") as AIFunction;
        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["message"] = "world",
        })
        { Services = provider };

        var result = await tool!.InvokeAsync(args);
        Assert.Equal("instance:world", result?.ToString());
        Assert.Equal(1, svc.CallCount);
    }

    // ── Interface tools ─────────────────────────────────────────────────

    [Fact]
    public void Interface_DiscoversTwoTools()
    {
        var tools = OrderArchiveToolContext.Default.Tools;
        Assert.Equal(2, tools.Count);
        Assert.Contains(tools, t => t.Name == "list_orders");
        Assert.Contains(tools, t => t.Name == "find_order");
    }

    [Fact]
    public void Interface_ListOrders_HasDescription()
    {
        var tool = OrderArchiveToolContext.Default.Tools.First(t => t.Name == "list_orders");
        Assert.Equal("Lists all past orders.", tool.Description);
    }

    [Fact]
    public async Task Interface_ListOrders_ResolvesImplementationFromDI()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOrderArchiveService, InMemoryOrderArchiveService>();
        using var provider = services.BuildServiceProvider();

        var tool = OrderArchiveToolContext.Default.Tools.First(t => t.Name == "list_orders") as AIFunction;
        var args = new AIFunctionArguments(new Dictionary<string, object?>()) { Services = provider };

        var result = await tool!.InvokeAsync(args);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Interface_FindOrder_ReturnsCorrectResult()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOrderArchiveService, InMemoryOrderArchiveService>();
        using var provider = services.BuildServiceProvider();

        var tool = OrderArchiveToolContext.Default.Tools.First(t => t.Name == "find_order") as AIFunction;
        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["orderId"] = "order-2",
        })
        { Services = provider };

        var result = await tool!.InvokeAsync(args);
        Assert.Equal("order-2", result?.ToString());
    }

    [Fact]
    public async Task Interface_FindOrder_ReturnsNotFound()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOrderArchiveService, InMemoryOrderArchiveService>();
        using var provider = services.BuildServiceProvider();

        var tool = OrderArchiveToolContext.Default.Tools.First(t => t.Name == "find_order") as AIFunction;
        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["orderId"] = "nonexistent",
        })
        { Services = provider };

        var result = await tool!.InvokeAsync(args);
        Assert.Equal("not found", result?.ToString());
    }

    // ── Interface with [FromServices] ───────────────────────────────────

    [Fact]
    public async Task InterfaceWithFromServices_ResolvesFromServicesParam()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBarService, BarServiceImpl>();
        services.AddSingleton<IFoo, FooImpl>();
        using var provider = services.BuildServiceProvider();

        var tool = BarToolContext.Default.Tools.First(t => t.Name == "bar_action") as AIFunction;
        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["input"] = "test",
        })
        { Services = provider };

        var result = await tool!.InvokeAsync(args);
        Assert.Equal("test:foo-impl", result?.ToString());
    }

    // ── Interface with property ─────────────────────────────────────────

    [Fact]
    public void InterfaceProperty_DiscoversPropertyTool()
    {
        var tools = CatalogToolContext.Default.Tools;
        Assert.Single(tools);
        Assert.Equal("all_products", tools[0].Name);
        Assert.Equal("All products.", tools[0].Description);
    }

    [Fact]
    public async Task InterfaceProperty_ReturnsPropertyValue()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICatalogService, CatalogServiceImpl>();
        using var provider = services.BuildServiceProvider();

        var tool = CatalogToolContext.Default.Tools[0] as AIFunction;
        var args = new AIFunctionArguments(new Dictionary<string, object?>()) { Services = provider };

        var result = await tool!.InvokeAsync(args);
        Assert.NotNull(result);
    }

    // ── Interface with ApprovalRequired ─────────────────────────────────

    [Fact]
    public void InterfaceApproval_WrapsCorrectTool()
    {
        var tools = DangerToolContext.Default.Tools;
        Assert.Equal(2, tools.Count);

        var safeTool = tools.First(t => t.Name == "safe_op");
        var dangerTool = tools.First(t => t.Name == "danger_op");

        Assert.IsNotType<ApprovalRequiredAIFunction>(safeTool);
        Assert.IsType<ApprovalRequiredAIFunction>(dangerTool);
    }

    // ── Nested class context ────────────────────────────────────────────

    [Fact]
    public void NestedClass_DiscoversSameToolsAsTopLevel()
    {
        var nestedTools = OuterClass.NestedToolContext.Default.Tools;
        var topLevelTools = TestToolContext.Default.Tools;

        // Both reference TestToolService — should discover the same tool names
        Assert.Equal(topLevelTools.Count, nestedTools.Count);
        foreach (var topTool in topLevelTools)
        {
            Assert.Contains(nestedTools, t => t.Name == topTool.Name);
        }
    }

    [Fact]
    public async Task NestedClass_InvokesCorrectly()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestToolService>();
        using var provider = services.BuildServiceProvider();

        var tool = OuterClass.NestedToolContext.Default.Tools.First(t => t.Name == "test_tool") as AIFunction;
        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["input"] = "nested",
        })
        { Services = provider };

        var result = await tool!.InvokeAsync(args);
        Assert.Equal("result: nested", result?.ToString());
    }

    // ── Deeply nested class context ─────────────────────────────────────

    [Fact]
    public void DeeplyNestedClass_DiscoversTwoTools()
    {
        var ctx = TopLevel.MidLevel.CreateDeep();
        var tools = ctx.Tools;
        Assert.Equal(2, tools.Count);
        Assert.Contains(tools, t => t.Name == "add_numbers");
        Assert.Contains(tools, t => t.Name == "negate_number");
    }

    [Fact]
    public async Task DeeplyNestedClass_StaticInvocation_WorksWithoutDI()
    {
        var ctx = TopLevel.MidLevel.CreateDeep();
        var tool = ctx.Tools.First(t => t.Name == "add_numbers") as AIFunction;
        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["a"] = 10,
            ["b"] = 20,
        });

        var result = await tool!.InvokeAsync(args);
        Assert.Equal(30, Convert.ToInt32(result));
    }
}
