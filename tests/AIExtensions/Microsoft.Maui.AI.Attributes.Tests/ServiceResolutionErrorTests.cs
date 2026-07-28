using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.AI.Attributes;

namespace Microsoft.Maui.AI.Attributes.Tests;

/// <summary>
/// Tests every service-resolution error path in generated code:
/// - Null <c>arguments.Services</c> on instance methods
/// - Null <c>arguments.Services</c> on <c>[FromServices]</c> parameters
/// - Non-null provider that returns null for source type (<c>EmptyServiceProvider</c> pattern)
/// - Non-null provider that returns null for <c>[FromServices]</c> parameter
/// - Non-null provider that doesn't support keyed services (<c>IKeyedServiceProvider</c>)
/// - Keyed provider where the keyed service isn't registered
/// - Error messages contain contextual details (tool name, type name, parameter name, key)
/// </summary>
public class ServiceResolutionErrorTests
{
    // ── Null Services ───────────────────────────────────────────────────

    [Fact]
    public async Task InstanceMethod_NullServices_ThrowsWithToolNameAndSourceType()
    {
        var tool = (AIFunction)TestToolContext.Default.Tools.First(t => t.Name == "test_tool");
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["input"] = "x" });
        // Services is null (not set)

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tool.InvokeAsync(args).AsTask());

        Assert.Contains("test_tool", ex.Message);
        Assert.Contains("TestToolService", ex.Message);
        Assert.Contains("no IServiceProvider was supplied", ex.Message);
    }

    [Fact]
    public async Task FromServicesParam_NullServices_ThrowsWithToolName()
    {
        var tool = (AIFunction)ContactsToolContext.Default.Tools.First(t => t.Name == "from_services_tool");
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["name"] = "x" });
        // Services is null — tool needs provider for both source type + [FromServices] param

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tool.InvokeAsync(args).AsTask());

        // Should fail at the provider-null check first (before parameter resolution)
        Assert.Contains("no IServiceProvider was supplied", ex.Message);
    }

    [Fact]
    public async Task FromKeyedServicesParam_NullServices_ThrowsWithToolName()
    {
        var tool = (AIFunction)ContactsToolContext.Default.Tools.First(t => t.Name == "from_keyed_tool");
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["name"] = "x" });
        // Services is null

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tool.InvokeAsync(args).AsTask());

        Assert.Contains("no IServiceProvider was supplied", ex.Message);
    }

    [Fact]
    public async Task StaticMethod_NullServices_DoesNotThrow()
    {
        // Static methods with no [FromServices] params don't need a provider
        var tool = (AIFunction)StaticMathToolContext.Default.Tools.First(t => t.Name == "add_numbers");
        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["a"] = 1,
            ["b"] = 2,
        });

        var result = await tool.InvokeAsync(args);
        Assert.Equal(3, Convert.ToInt32(result));
    }

    // ── Provider returns null for source type (EmptyServiceProvider) ────

    [Fact]
    public async Task InstanceMethod_ProviderReturnsNullForSourceType_ThrowsContextualError()
    {
        // Build a real provider but DON'T register the source type
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();

        var tool = (AIFunction)TestToolContext.Default.Tools.First(t => t.Name == "test_tool");
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["input"] = "x" })
        {
            Services = provider
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tool.InvokeAsync(args).AsTask());

        Assert.Contains("could not resolve its source type", ex.Message);
        Assert.Contains("TestToolService", ex.Message);
        Assert.Contains("Register the service", ex.Message);
    }

    [Fact]
    public async Task InterfaceMethod_ProviderReturnsNullForSourceType_ThrowsContextualError()
    {
        // Register nothing — the interface source type won't resolve
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();

        var tool = (AIFunction)OrderArchiveToolContext.Default.Tools.First(t => t.Name == "list_orders");
        var args = new AIFunctionArguments(new Dictionary<string, object?>()) { Services = provider };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tool.InvokeAsync(args).AsTask());

        Assert.Contains("could not resolve its source type", ex.Message);
        Assert.Contains("IOrderArchiveService", ex.Message);
    }

    // ── Provider returns null for [FromServices] parameter ──────────────

    [Fact]
    public async Task FromServices_ProviderReturnsNullForParam_ThrowsWithParamNameAndType()
    {
        // Register the source type but NOT the [FromServices] dependency
        var services = new ServiceCollection();
        services.AddSingleton<ContactsToolService>();
        // NOT registering IAddressBook
        using var provider = services.BuildServiceProvider();

        var tool = (AIFunction)ContactsToolContext.Default.Tools.First(t => t.Name == "from_services_tool");
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["name"] = "x" })
        {
            Services = provider
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tool.InvokeAsync(args).AsTask());

        Assert.Contains("book", ex.Message); // parameter name
        Assert.Contains("IAddressBook", ex.Message); // parameter type
        Assert.Contains("could not be resolved", ex.Message);
    }

    [Fact]
    public async Task FromServices_OnInterface_ProviderReturnsNullForParam_ThrowsWithParamDetails()
    {
        // Register the interface source type but NOT the [FromServices] dependency
        var services = new ServiceCollection();
        services.AddSingleton<IBarService, BarServiceImpl>();
        // NOT registering IFoo
        using var provider = services.BuildServiceProvider();

        var tool = (AIFunction)BarToolContext.Default.Tools.First(t => t.Name == "bar_action");
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["input"] = "test" })
        {
            Services = provider
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tool.InvokeAsync(args).AsTask());

        Assert.Contains("foo", ex.Message); // parameter name
        Assert.Contains("IFoo", ex.Message); // parameter type
    }

    // ── [FromKeyedServices] with non-IKeyedServiceProvider ──────────────

    [Fact]
    public async Task FromKeyedServices_NonKeyedProvider_ThrowsContextualError()
    {
        // MinimalServiceProvider doesn't implement IKeyedServiceProvider
        var provider = new MinimalServiceProvider(new Dictionary<Type, object>
        {
            [typeof(ContactsToolService)] = new ContactsToolService(),
        });

        var tool = (AIFunction)ContactsToolContext.Default.Tools.First(t => t.Name == "from_keyed_tool");
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["name"] = "x" })
        {
            Services = provider
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tool.InvokeAsync(args).AsTask());

        Assert.Contains("book", ex.Message); // parameter name
        Assert.Contains("IAddressBook", ex.Message); // parameter type
        Assert.Contains("primary", ex.Message); // key value
        Assert.Contains("IKeyedServiceProvider", ex.Message); // guidance
    }

    // ── [FromKeyedServices] with keyed provider but missing registration ─

    [Fact]
    public async Task FromKeyedServices_KeyedProvider_MissingRegistration_ThrowsWithKeyInMessage()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ContactsToolService>();
        // Register IAddressBook unkeyed but NOT with key "primary"
        services.AddSingleton<IAddressBook, AddressBook>();
        using var provider = services.BuildServiceProvider();

        var tool = (AIFunction)ContactsToolContext.Default.Tools.First(t => t.Name == "from_keyed_tool");
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["name"] = "x" })
        {
            Services = provider
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tool.InvokeAsync(args).AsTask());

        Assert.Contains("primary", ex.Message);
        Assert.Contains("book", ex.Message);
        Assert.Contains("IAddressBook", ex.Message);
    }

    // ── Happy paths (regression guards) ─────────────────────────────────

    [Fact]
    public async Task FromServices_WithValidProvider_ResolvesSuccessfully()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAddressBook, AddressBook>();
        services.AddSingleton<ContactsToolService>();
        using var provider = services.BuildServiceProvider();

        var tool = (AIFunction)ContactsToolContext.Default.Tools.First(t => t.Name == "from_services_tool");
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["name"] = "alice" })
        {
            Services = provider
        };

        var result = await tool.InvokeAsync(args);
        Assert.Equal("addr:alice", result?.ToString());
    }

    [Fact]
    public async Task FromKeyedServices_WithValidProvider_ResolvesSuccessfully()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IAddressBook, AddressBook>("primary");
        services.AddSingleton<ContactsToolService>();
        using var provider = services.BuildServiceProvider();

        var tool = (AIFunction)ContactsToolContext.Default.Tools.First(t => t.Name == "from_keyed_tool");
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["name"] = "bob" })
        {
            Services = provider
        };

        var result = await tool.InvokeAsync(args);
        Assert.Equal("addr:bob", result?.ToString());
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// A minimal IServiceProvider that does NOT implement IKeyedServiceProvider.
    /// This simulates EmptyServiceProvider or any custom provider without keyed support.
    /// </summary>
    private sealed class MinimalServiceProvider : IServiceProvider
    {
        private readonly Dictionary<Type, object> _services;

        public MinimalServiceProvider(Dictionary<Type, object> services) => _services = services;

        public object? GetService(Type serviceType) =>
            _services.TryGetValue(serviceType, out var svc) ? svc : null;
    }
}
