using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.Attributes.Tests;

public class AIFunctionScopedLifetimeTests
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
    public async Task Scoped_with_root_provider_and_validate_scopes_throws()
    {
        // The library never creates scopes. When the root provider is passed with
        // ValidateScopes=true, resolving a scoped service throws InvalidOperationException.
        var services = new ServiceCollection();
        services.AddScoped<InvocationCounterService>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var tool = InvocationCounterToolContext.Default.Tools.First(t => t.Name == "counter_tool") as AIFunction;
        var args = new AIFunctionArguments(new Dictionary<string, object?>()) { Services = provider };

        await Assert.ThrowsAsync<InvalidOperationException>(() => tool!.InvokeAsync(args).AsTask());
    }

    [Fact]
    public async Task Scoped_with_scoped_provider_returns_same_instance_within_scope()
    {
        var services = new ServiceCollection();
        services.AddScoped<InvocationCounterService>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var tool = InvocationCounterToolContext.Default.Tools.First(t => t.Name == "counter_tool") as AIFunction;

        using var scope = provider.CreateScope();
        var args1 = new AIFunctionArguments(new Dictionary<string, object?>()) { Services = scope.ServiceProvider };
        var args2 = new AIFunctionArguments(new Dictionary<string, object?>()) { Services = scope.ServiceProvider };

        var result1 = await tool!.InvokeAsync(args1);
        var result2 = await tool.InvokeAsync(args2);

        Assert.Equal(1, GetIntResult(result1));
        Assert.Equal(2, GetIntResult(result2));
    }

    [Fact]
    public async Task Scoped_with_different_scopes_returns_different_instances()
    {
        var services = new ServiceCollection();
        services.AddScoped<InvocationCounterService>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var tool = InvocationCounterToolContext.Default.Tools.First(t => t.Name == "counter_tool") as AIFunction;

        using var scope1 = provider.CreateScope();
        var args1 = new AIFunctionArguments(new Dictionary<string, object?>()) { Services = scope1.ServiceProvider };
        var result1 = await tool!.InvokeAsync(args1);

        using var scope2 = provider.CreateScope();
        var args2 = new AIFunctionArguments(new Dictionary<string, object?>()) { Services = scope2.ServiceProvider };
        var result2 = await tool.InvokeAsync(args2);

        Assert.Equal(1, GetIntResult(result1));
        Assert.Equal(1, GetIntResult(result2));
    }

    [Fact]
    public async Task Scoped_with_root_provider_without_validate_scopes_works()
    {
        var services = new ServiceCollection();
        services.AddScoped<InvocationCounterService>();
        using var provider = services.BuildServiceProvider();

        var tool = InvocationCounterToolContext.Default.Tools.First(t => t.Name == "counter_tool") as AIFunction;
        var result = await tool!.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()) { Services = provider });

        Assert.Equal(1, GetIntResult(result));
    }
}
