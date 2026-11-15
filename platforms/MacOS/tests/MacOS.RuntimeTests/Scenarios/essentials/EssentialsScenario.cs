using System.Runtime.CompilerServices;
using Foundation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Platforms.MacOS.Platform;
using Microsoft.Maui.Storage;

namespace MacOS.RuntimeTests.Scenarios.Essentials;

static class Registration
{
    [ModuleInitializer]
    public static void Register() =>
        ScenarioRegistry.Register(new("essentials", 3, context => new EssentialsScenario().CreateDelegate(context),
            ExpectedAssertions: 40));
}

// UseMauiAppMacOS + AddMacOSEssentials register the AppKit implementations and the application
// dispatcher in DI. MAUI 11's Essentials initializer assigns them to the static facades and bridges
// MainThread from that dispatcher; no reflection is involved.
sealed class EssentialsScenario : MauiRuntimeScenario
{
    public override Window CreateWindow(IActivationState? activationState) =>
        new(new ContentPage { Content = new Label { Text = "Essentials" } });

    public override async Task RunAsync(RuntimeTestContext context, Window window)
    {
        var services = window.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("The window has no MAUI services.");

        // 30 registered Essentials interfaces, one assertion each, plus two.
        var essentials = typeof(IFileSystem).Assembly;
        var interfaces = services.GetRequiredService<RegisteredEssentials>().Types;
        var facades = new List<object>();
        foreach (var serviceType in interfaces)
        {
            var facade = essentials.GetType($"{serviceType.Namespace}.{serviceType.Name[1..]}", throwOnError: true)!;
            var property = facade.GetProperty("Current") ?? facade.GetProperty("Default")
                ?? throw new InvalidOperationException($"{facade.Name} has no Current/Default property.");
            var implementation = services.GetRequiredService(serviceType);
            var value = property.GetValue(null);
            context.Assert(ReferenceEquals(implementation, value),
                $"{facade.Name}.{property.Name} is the DI {serviceType.Name} ({implementation.GetType().Name}).",
                "essentials.facade");
            facades.Add(new { facade = facade.Name, implementation = value?.GetType().FullName });
        }
        context.Assert(interfaces.Count == 30, $"30 Essentials interfaces are registered; actual {interfaces.Count}.", "essentials.count");
        context.Assert(AppInfo.Current.GetType().Assembly.GetName().Name == "Microsoft.Maui.Platforms.MacOS.Essentials",
            "AppInfo.Current comes from the macOS Essentials package.", "essentials.appinfo");
        context.WriteJson("facades.json", facades);
        context.Pass("facades");

        context.Assert(services.GetKeyedService<IDispatcher>(typeof(IApplication)) is MacOSDispatcher,
            "The application dispatcher is the macOS dispatcher.", "essentials.dispatcher");
        context.Assert(NSThread.IsMain && MainThread.IsMainThread,
            "MainThread.IsMainThread is true on the AppKit main thread.", "essentials.main-thread");
        var (backgroundIsMain, ranOnMain) = await Task.Run(async () =>
        {
            var isMain = MainThread.IsMainThread;
            var marshalled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            MainThread.BeginInvokeOnMainThread(() => marshalled.SetResult(NSThread.IsMain && MainThread.IsMainThread));
            return (isMain, await marshalled.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        });
        context.Assert(!backgroundIsMain, "MainThread.IsMainThread is false on a thread-pool thread.", "essentials.background");
        context.Assert(ranOnMain, "MainThread.BeginInvokeOnMainThread runs the action on the AppKit main thread.",
            "essentials.begin-invoke");
        var result = await MainThread.InvokeOnMainThreadAsync(() => NSThread.IsMain);
        context.Assert(result, "MainThread.InvokeOnMainThreadAsync runs on the AppKit main thread.", "essentials.invoke-async");
        context.Pass("main-thread");

        context.Assert(VersionTracking.CurrentVersion == AppInfo.VersionString,
            $"VersionTracking.CurrentVersion ({VersionTracking.CurrentVersion}) matches AppInfo ({AppInfo.VersionString}).",
            "essentials.version");
        context.Assert(VersionTracking.VersionHistory.Contains(AppInfo.VersionString),
            "UseVersionTracking recorded the current version at startup.", "essentials.version-history");
        context.Assert(!string.IsNullOrEmpty(FileSystem.AppDataDirectory) && Directory.Exists(FileSystem.AppDataDirectory),
            $"FileSystem.AppDataDirectory exists ({FileSystem.AppDataDirectory}).", "essentials.filesystem");
        context.WriteJson("essentials.json", new
        {
            appInfo = new { AppInfo.Name, AppInfo.PackageName, AppInfo.VersionString, theme = AppInfo.RequestedTheme.ToString() },
            versionTracking = new { VersionTracking.CurrentVersion, VersionTracking.IsFirstLaunchEver, history = VersionTracking.VersionHistory },
            FileSystem.AppDataDirectory
        });
        context.Pass("version-tracking");
    }

    public override void Configure(Microsoft.Maui.Hosting.MauiAppBuilder builder)
    {
        var types = builder.Services
            .Where(d => !d.IsKeyedService && d.ServiceType.IsInterface && d.ServiceType.Assembly == typeof(IFileSystem).Assembly)
            .Select(d => d.ServiceType)
            .Distinct()
            .ToList();
        builder.Services.AddSingleton(new RegisteredEssentials(types));
    }

    sealed record RegisteredEssentials(IReadOnlyList<Type> Types);
}
