using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Platform;
using Microsoft.Maui.Platforms.Windows.WPF;

namespace HandlerTests;

public class AlertManagerSubscriptionTests
{
    [Fact]
    public void Register_RegistersTheSubscriptionConsumedByMaui()
    {
        var services = new ServiceCollection();
        WPFAlertManagerSubscription.Register(services);

        using var provider = services.BuildServiceProvider();
        var subscription = provider.GetRequiredService<IAlertManagerSubscription>();
        Assert.IsType<WPFAlertManagerSubscription>(subscription);
        Assert.Same(subscription, provider.GetRequiredService<IAlertManagerSubscription>());
    }
}
