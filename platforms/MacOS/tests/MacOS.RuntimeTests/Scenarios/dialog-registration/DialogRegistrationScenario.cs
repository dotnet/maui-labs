using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Platform;
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
        context.Assert(property?.PropertyType == typeof(IAlertManagerSubscription),
            "MAUI consumes the public IAlertManagerSubscription through AlertManager.Subscription.");
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAlertManagerSubscription));
        context.Assert(descriptor?.Lifetime == ServiceLifetime.Singleton,
            "Exactly one singleton IAlertManagerSubscription is registered.");
        using var provider = services.BuildServiceProvider();
        var subscription = provider.GetService<IAlertManagerSubscription>();

        context.WriteJson("registration.json", new
        {
            assembly = assembly.FullName,
            version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            consumedType = property?.PropertyType.FullName,
            registeredType = subscription?.GetType().FullName
        });
        context.Assert(subscription is AlertManagerSubscription,
            "The consumed subscription resolves to the AppKit implementation.");
        context.Assert(ReferenceEquals(subscription, provider.GetRequiredService<IAlertManagerSubscription>()),
            "Repeated resolution returns the same subscription instance.");
        context.Pass("dialog subscription registration");
    }
}
