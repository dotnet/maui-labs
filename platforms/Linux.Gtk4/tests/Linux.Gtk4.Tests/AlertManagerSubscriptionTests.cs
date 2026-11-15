using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Platform;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

public class AlertManagerSubscriptionTests
{
    [Fact]
    public void Register_RegistersTheSubscriptionConsumedByMaui()
    {
        var services = new ServiceCollection();
        GtkAlertManager.Register(services);

        // The default AlertManager resolves its subscription through this property's type.
        var managerType = typeof(Window).Assembly.GetType(
            "Microsoft.Maui.Controls.Platform.AlertManager", throwOnError: true)!;
        var subscriptionProperty = managerType.GetProperty("Subscription");
        Assert.True(subscriptionProperty is not null,
            $"No AlertManager.Subscription property in {typeof(Window).Assembly.FullName}.");
        Assert.Equal(typeof(IAlertManagerSubscription), subscriptionProperty.PropertyType);

        using var provider = services.BuildServiceProvider();
        var subscription = provider.GetRequiredService<IAlertManagerSubscription>();
        Assert.IsType<GtkAlertManager.AlertSubscription>(subscription);
        Assert.Same(subscription, provider.GetRequiredService<IAlertManagerSubscription>());
    }
}
