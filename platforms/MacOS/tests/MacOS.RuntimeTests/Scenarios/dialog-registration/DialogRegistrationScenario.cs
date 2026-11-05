using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Media;
using Microsoft.Maui.Platforms.MacOS.Hosting;
using Microsoft.Maui.Platforms.MacOS.Essentials;
#if NET11_0_OR_GREATER
using Microsoft.Maui.Controls.Platform;
using Microsoft.Maui.Platforms.MacOS.Handlers;
#endif
using Microsoft.Maui.Platforms.MacOS.Platform;

namespace MacOS.RuntimeTests.Scenarios;

static class DialogRegistrationScenario
{
    [ModuleInitializer]
    public static void Register() => ScenarioRegistry.Register(
        new("dialog-registration", ExpectedCases: 1, RunManaged: Run, ExpectedAssertions: 6));

    static void Run(RuntimeTestContext context)
    {
        var services = new ServiceCollection();
        AlertManagerSubscription.Register(services);

        var assembly = typeof(Window).Assembly;
#if NET11_0_OR_GREATER
        using var provider = services.BuildServiceProvider();
        var subscription = provider.GetService<IAlertManagerSubscription>();

        context.WriteJson("registration.json", new
        {
            assembly = assembly.FullName,
            version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            consumedType = typeof(IAlertManagerSubscription).FullName,
            registeredType = subscription?.GetType().FullName
        });
        context.Assert(typeof(IAlertManagerSubscription).IsPublic,
            "MAUI exposes a public IAlertManagerSubscription.");
        context.Assert(subscription is AlertManagerSubscription,
            "The consumed subscription resolves to the AppKit implementation.");
        context.Assert(subscription is IAlertManagerSubscription,
            "The AppKit subscription implements the typed public contract.");
        context.Assert(ReferenceEquals(subscription, provider.GetRequiredService<IAlertManagerSubscription>()),
            "Repeated resolution returns the same subscription instance.");
#else
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
#endif

        var overrideScreenshot = new OverrideScreenshot();
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiAppMacOS<OverrideApplication>();
        builder.Services.AddSingleton<IScreenshot>(overrideScreenshot);
        builder.AddMacOSEssentials();
        var screenshotRegistrations = builder.Services
            .Where(descriptor => descriptor.ServiceType == typeof(IScreenshot))
            .ToArray();
        context.Assert(
            screenshotRegistrations.Length == 1 &&
            ReferenceEquals(screenshotRegistrations[0].ImplementationInstance, overrideScreenshot),
            "Essentials registration must preserve an existing DI override.");
#if NET11_0_OR_GREATER
        context.Assert(
            builder.Services.Any(descriptor =>
                descriptor.ServiceType == typeof(IGesturePlatformManagerFactory) &&
                descriptor.ImplementationType == typeof(MacOSGesturePlatformManagerFactory)),
            "MAUI 11 startup must register the public AppKit gesture factory.");
#else
        context.Assert(
            builder.Services.All(descriptor => descriptor.ServiceType.FullName !=
                "Microsoft.Maui.Controls.Platform.IGesturePlatformManagerFactory"),
            "MAUI 10 startup must retain the compatibility gesture lifecycle.");
#endif
        context.Pass("dialog subscription registration");
    }

    sealed class OverrideApplication : Application;

    sealed class OverrideScreenshot : IScreenshot
    {
        public bool IsCaptureSupported => false;

        public Task<IScreenshotResult> CaptureAsync()
            => throw new NotSupportedException();
    }
}
