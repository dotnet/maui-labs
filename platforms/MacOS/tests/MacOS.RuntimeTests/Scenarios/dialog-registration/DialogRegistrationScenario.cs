using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.MacOS.Platform;

namespace MacOS.RuntimeTests.Scenarios;

static class DialogRegistrationScenario
{
    [ModuleInitializer]
    public static void Register() => ScenarioRegistry.Register(
        new("dialog-registration", ExpectedCases: 1, RunManaged: Run, ExpectedAssertions: 4));

    static void Run(RuntimeTestContext context)
    {
        var services = new ServiceCollection();
        AlertManagerSubscription.Register(services);

        var assembly = typeof(Window).Assembly;
        var managerType = assembly.GetType("Microsoft.Maui.Controls.Platform.AlertManager", throwOnError: true)!;
        var property = managerType.GetProperty("Subscription");
        context.Assert(property is not null, "MAUI exposes AlertManager.Subscription.");
        var subscriptionType = property!.PropertyType;
        using var provider = services.BuildServiceProvider();
        var subscription = provider.GetService(subscriptionType);
        var proxyBase = subscription?.GetType().BaseType;

        context.WriteJson("registration.json", new
        {
            assembly = assembly.FullName,
            version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            consumedType = subscriptionType.FullName,
            registeredType = subscription?.GetType().FullName
        });
        context.Assert(subscription is DispatchProxy, "The consumed subscription resolves to a DispatchProxy.");
        context.Assert(proxyBase?.IsGenericType == true &&
            proxyBase.GetGenericTypeDefinition() == typeof(AlertManagerSubscription<>),
            "The subscription uses the AppKit proxy implementation.");
        context.Assert(ReferenceEquals(subscription, provider.GetRequiredService(subscriptionType)),
            "Repeated resolution returns the same subscription instance.");
        context.Pass("dialog subscription registration");
    }
}
