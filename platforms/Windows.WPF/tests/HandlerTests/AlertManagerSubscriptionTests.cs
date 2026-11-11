using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.Windows.WPF;

namespace HandlerTests;

public class AlertManagerSubscriptionTests
{
#if NET10_0
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
#else
    [Theory]
    [InlineData(WPFDialogService.DisplayAlertKey, typeof(Func<Page, Microsoft.Maui.Controls.Internals.AlertArguments, Task<bool>>))]
    [InlineData(WPFDialogService.DisplayActionSheetKey, typeof(Func<Page, Microsoft.Maui.Controls.Internals.ActionSheetArguments, Task<string>>))]
    [InlineData(WPFDialogService.DisplayPromptKey, typeof(Func<Page, Microsoft.Maui.Controls.Internals.PromptArguments, Task<string>>))]
    public void Register_RegistersPublicKeyedDialogDelegate(string key, Type serviceType)
    {
        var services = new ServiceCollection();
        WPFDialogService.Register(services);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredKeyedService(serviceType, key));
    }
#endif
}
