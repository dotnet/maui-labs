using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
#if NET11_0_OR_GREATER
using Microsoft.Maui.Controls.Platform;
#else
using Microsoft.Maui.Controls;
#endif
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

public class AlertManagerSubscriptionTests
{
    [Fact]
    public void Register_RegistersTheSubscriptionConsumedByMaui()
    {
        var services = new ServiceCollection();
        GtkAlertManager.Register(services);

        using var provider = services.BuildServiceProvider();
#if NET11_0_OR_GREATER
        var subscription = provider.GetRequiredService<IAlertManagerSubscription>();
        Assert.IsType<GtkAlertManager>(subscription);
        Assert.Same(subscription, provider.GetRequiredService<IAlertManagerSubscription>());
#else
        var managerType = typeof(Window).Assembly.GetType(
            "Microsoft.Maui.Controls.Platform.AlertManager", throwOnError: true)!;
        var subscriptionType = typeof(Window).Assembly.GetType(
            "Microsoft.Maui.Controls.Platform.IAlertManagerSubscription")
            ?? managerType.GetNestedType(
                "IAlertManagerSubscription",
                BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("MAUI 10 alert subscription type was not found.");
        var subscription = provider.GetRequiredService(subscriptionType);
        Assert.IsAssignableFrom<DispatchProxy>(subscription);
        Assert.Same(subscription, provider.GetRequiredService(subscriptionType));
#endif
    }
}
