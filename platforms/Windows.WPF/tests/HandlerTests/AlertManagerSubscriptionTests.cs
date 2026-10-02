using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.Windows.WPF;

namespace HandlerTests;

public class AlertManagerSubscriptionTests
{
    [Fact]
    public void Register_RegistersTheSubscriptionConsumedByMaui()
    {
        var services = new ServiceCollection();
        WPFAlertManagerSubscription.Register(services);

        var managerType = typeof(Window).Assembly.GetType(
            "Microsoft.Maui.Controls.Platform.AlertManager", throwOnError: true)!;
        var subscriptionProperty = managerType.GetProperty("Subscription");
        Assert.True(subscriptionProperty is not null,
            $"No AlertManager.Subscription property in {typeof(Window).Assembly.FullName}.");
        var subscriptionType = subscriptionProperty.PropertyType;

        using var provider = services.BuildServiceProvider();
        var subscription = provider.GetRequiredService(subscriptionType);
        Assert.IsAssignableFrom<DispatchProxy>(subscription);
        Assert.Equal(typeof(WPFAlertManagerSubscriptionProxy<>),
            subscription.GetType().BaseType!.GetGenericTypeDefinition());
        Assert.Same(subscription, provider.GetRequiredService(subscriptionType));
    }
}
