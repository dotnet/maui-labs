using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.Attributes.Tests;

public class AIFunctionInvocationTests
{
    private static int GetIntResult(object? result)
    {
        if (result is JsonElement jsonElement)
        {
            return jsonElement.GetInt32();
        }

        return Convert.ToInt32(result);
    }

    [Fact]
    public async Task Singleton_invocation_uses_same_instance()
    {
        var services = new ServiceCollection();
        services.AddSingleton<InvocationCounterService>();
        using var provider = services.BuildServiceProvider();

        var tool = InvocationCounterToolContext.Default.Tools.First(t => t.Name == "counter_tool") as AIFunction;
        var args = new AIFunctionArguments(new Dictionary<string, object?>()) { Services = provider };

        var result1 = await tool!.InvokeAsync(args);
        var result2 = await tool.InvokeAsync(args);

        Assert.Equal(1, GetIntResult(result1));
        Assert.Equal(2, GetIntResult(result2));
    }

    [Fact]
    public async Task Transient_invocation_uses_different_instance()
    {
        var services = new ServiceCollection();
        services.AddTransient<InvocationCounterService>();
        using var provider = services.BuildServiceProvider();

        var tool = InvocationCounterToolContext.Default.Tools.First(t => t.Name == "counter_tool") as AIFunction;
        var args = new AIFunctionArguments(new Dictionary<string, object?>()) { Services = provider };

        var result1 = await tool!.InvokeAsync(args);
        var result2 = await tool.InvokeAsync(args);

        Assert.Equal(1, GetIntResult(result1));
        Assert.Equal(1, GetIntResult(result2));
    }

    [Fact]
    public async Task Invocation_returns_correct_value()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestToolService>();
        using var provider = services.BuildServiceProvider();

        var tool = TestToolContext.Default.Tools.First(t => t.Name == "test_tool") as AIFunction;
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["input"] = "hello" }) { Services = provider };

        var result = await tool!.InvokeAsync(args);

        Assert.Equal("result: hello", result?.ToString());
    }

    [Fact]
    public async Task Async_method_returns_correct_value()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestToolService>();
        using var provider = services.BuildServiceProvider();

        var tool = TestToolContext.Default.Tools.First(t => t.Name == "async_tool") as AIFunction;
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["input"] = "world" }) { Services = provider };

        var result = await tool!.InvokeAsync(args);

        Assert.Equal("async: world", result?.ToString());
    }

    [Fact]
    public async Task Multiple_parameters_bind_correctly()
    {
        var services = new ServiceCollection();
        services.AddSingleton<MultiParamService>();
        using var provider = services.BuildServiceProvider();

        var tool = MultiParamToolContext.Default.Tools.First(t => t.Name == "multi_param") as AIFunction;
        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["firstName"] = "Alice",
            ["lastName"] = "Smith",
            ["age"] = 30,
        })
        { Services = provider };

        var result = await tool!.InvokeAsync(args);

        Assert.Equal("Alice Smith, age 30", result?.ToString());
    }

    [Fact]
    public async Task Disposable_service_is_not_disposed_after_invocation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DisposableToolService>();
        using var provider = services.BuildServiceProvider();

        var disposableService = provider.GetRequiredService<DisposableToolService>();
        var tool = DisposableToolContext.Default.Tools.First(t => t.Name == "disposable_tool") as AIFunction;

        var result = await tool!.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()) { Services = provider });

        Assert.Equal("value", result?.ToString());
        Assert.False(disposableService.IsDisposed);
    }

    [Fact]
    public async Task Missing_service_registration_throws_on_invocation()
    {
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();

        var tool = TestToolContext.Default.Tools.First(t => t.Name == "test_tool") as AIFunction;
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["input"] = "test" }) { Services = provider };

        await Assert.ThrowsAsync<InvalidOperationException>(() => tool!.InvokeAsync(args).AsTask());
    }
}
