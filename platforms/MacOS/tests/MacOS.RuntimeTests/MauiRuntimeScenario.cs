using AppKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.MacOS.Essentials;
using Microsoft.Maui.Platforms.MacOS.Hosting;
using Microsoft.Maui.Platforms.MacOS.Platform;

namespace MacOS.RuntimeTests;

public abstract class MauiRuntimeScenario
{
    public NSApplicationDelegate CreateDelegate(RuntimeTestContext context)
    {
        Prepare(context);
        return new RuntimeMauiDelegate(this, context);
    }

    protected virtual void Prepare(RuntimeTestContext context) { }
    public virtual void Configure(MauiAppBuilder builder) { }
    public abstract Window CreateWindow(IActivationState? activationState);
    public abstract Task RunAsync(RuntimeTestContext context, Window window);
}

public sealed class RuntimeMauiApplication(MauiRuntimeScenario scenario) : Application
{
    protected override Window CreateWindow(IActivationState? activationState) => scenario.CreateWindow(activationState);
}

sealed class RuntimeMauiDelegate(MauiRuntimeScenario scenario, RuntimeTestContext context) : MacOSMauiApplication
{
    protected override MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.Services.AddSingleton(scenario);
        builder.UseMauiAppMacOS<RuntimeMauiApplication>().AddMacOSEssentials();
        scenario.Configure(builder);
        return builder.Build();
    }

    protected override void OnStarted() => BeginInvokeOnMainThread(async () =>
    {
        try
        {
            var application = (RuntimeMauiApplication)Application;
            if (application.Windows.Count != 1)
                throw new InvalidOperationException("The scenario must start with exactly one real MAUI window.");
            await scenario.RunAsync(context, application.Windows[0]);
            context.Complete();
        }
        catch (Exception ex)
        {
            context.Fail(ex);
        }
    });
}
