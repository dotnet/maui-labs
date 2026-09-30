using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.MacOS.Platform;

var services = new ServiceCollection();
AlertManagerSubscription.Register(services);

var managerType = typeof(Window).Assembly.GetType(
    "Microsoft.Maui.Controls.Platform.AlertManager", throwOnError: true)!;
var subscriptionType = managerType.GetProperty("Subscription")?.PropertyType
    ?? throw new InvalidOperationException($"No AlertManager.Subscription property in {typeof(Window).Assembly.FullName}.");

using var provider = services.BuildServiceProvider();
var subscription = provider.GetRequiredService(subscriptionType);
if (subscription is not DispatchProxy
    || subscription.GetType().BaseType?.GetGenericTypeDefinition() != typeof(AlertManagerSubscription<>)
    || !ReferenceEquals(subscription, provider.GetRequiredService(subscriptionType)))
{
    throw new InvalidOperationException($"Unexpected AppKit dialog subscription: {subscription.GetType()}.");
}

Console.WriteLine($"PASS: {typeof(Window).Assembly.FullName}: {subscriptionType.FullName} -> {subscription.GetType()}");

if (args.Contains("--native"))
{
    AppKit.NSApplication.Init();
    AppKit.NSApplication.SharedApplication.Delegate = new NativeDialogProbe();
    AppKit.NSApplication.Main([]);
}
