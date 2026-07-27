using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;

namespace Microsoft.Maui.DevFlow.Blazor;

/// <summary>
/// Extension methods for registering Microsoft.Maui.DevFlow Blazor debug tools.
/// </summary>
public static class BlazorDevFlowExtensions
{
    /// <summary>
    /// Adds Microsoft.Maui.DevFlow Blazor WebView debugging tools to the MAUI app.
    /// Enables Chrome DevTools Protocol (CDP) access to BlazorWebView content.
    /// Chobitsu.js is auto-injected via a Blazor JS initializer — no manual script tag needed.
    /// </summary>
    public static MauiAppBuilder AddMauiBlazorDevFlowTools(this MauiAppBuilder builder, Action<BlazorWebViewDebugOptions>? configure = null)
    {
        var options = new BlazorWebViewDebugOptions();
        configure?.Invoke(options);

        if (!options.Enabled) return builder;

#if ANDROID
        var service = new BlazorWebViewDebugService();
        if (options.EnableLogging)
        {
            service.LogCallback = (msg) => System.Diagnostics.Debug.WriteLine(msg);
        }

        builder.Services.AddSingleton(service);
        builder.Services.AddSingleton<BlazorWebViewDebugServiceBase>(sp => sp.GetRequiredService<BlazorWebViewDebugService>());

        service.ConfigureHandler();

        builder.ConfigureLifecycleEvents(lifecycle =>
        {
            lifecycle.AddAndroid(android =>
            {
                android.OnResume(activity =>
                {
                    service.Initialize();
                    WireAgentCdp(service);
                    System.Diagnostics.Debug.WriteLine("[Microsoft.Maui.DevFlow] Blazor CDP initialized");
                });
            });
        });
#elif IOS || MACCATALYST
        var service = new BlazorWebViewDebugService();
        if (options.EnableLogging)
        {
            service.LogCallback = (msg) => System.Diagnostics.Debug.WriteLine(msg);
        }

        builder.Services.AddSingleton(service);
        builder.Services.AddSingleton<BlazorWebViewDebugServiceBase>(sp => sp.GetRequiredService<BlazorWebViewDebugService>());

        // Configure handler to capture WebView reference
        service.ConfigureHandler();

        builder.ConfigureLifecycleEvents(lifecycle =>
        {
            lifecycle.AddiOS(ios =>
            {
                ios.FinishedLaunching((_, _) =>
                {
                    service.Initialize();
                    WireAgentCdp(service);
                    System.Diagnostics.Debug.WriteLine("[Microsoft.Maui.DevFlow] Blazor CDP initialized");
                    return true;
                });
            });
        });
#elif WINDOWS
        var service = new BlazorWebViewDebugService();
        if (options.EnableLogging)
        {
            service.LogCallback = (msg) => System.Diagnostics.Debug.WriteLine(msg);
        }

        builder.Services.AddSingleton(service);
        builder.Services.AddSingleton<BlazorWebViewDebugServiceBase>(sp => sp.GetRequiredService<BlazorWebViewDebugService>());

        service.ConfigureHandler();

        builder.ConfigureLifecycleEvents(lifecycle =>
        {
            lifecycle.AddWindows(windows =>
            {
                windows.OnLaunched((_, _) =>
                {
                    service.Initialize();
                    WireAgentCdp(service);
                    System.Diagnostics.Debug.WriteLine("[Microsoft.Maui.DevFlow] Blazor CDP initialized");
                });
            });
        });
#elif MACOS
        var service = new BlazorWebViewDebugService();
        if (options.EnableLogging)
        {
            service.LogCallback = (msg) => System.Diagnostics.Debug.WriteLine(msg);
        }

        builder.Services.AddSingleton(service);
        builder.Services.AddSingleton<BlazorWebViewDebugServiceBase>(sp => sp.GetRequiredService<BlazorWebViewDebugService>());

        service.ConfigureHandler();

        builder.ConfigureLifecycleEvents(lifecycle =>
        {
            lifecycle.AddMacOS(macos =>
            {
                macos.DidFinishLaunching(_ =>
                {
                    service.Initialize();
                    WireAgentCdp(service);
                    System.Diagnostics.Debug.WriteLine("[Microsoft.Maui.DevFlow] Blazor CDP initialized");
                });
            });
        });
#endif

        return builder;
    }

    /// <summary>
    /// Wire the Blazor CDP service to the Agent's /api/cdp endpoint via reflection.
    /// Uses reflection to avoid a direct package dependency from Blazor → Agent.
    /// </summary>
    private static void WireAgentCdp(BlazorWebViewDebugServiceBase blazorService)
    {
        if (!blazorService.TryBeginAgentWiring())
            return;

        Task.Run(async () =>
        {
            var wired = false;
            Action<int, BlazorWebViewDebugServiceBase.WebViewBridge>? bridgeAddedHandler = null;
            await Task.Delay(1000);
            try
            {
                Type? agentType = null;
                object? agentService = null;
                IServiceProvider? services = null;

                // Lifecycle callbacks can run before the app handler and DevFlow agent service are
                // fully available. Retry here so one-shot iOS/Windows/macOS launch callbacks are as
                // reliable as Android's repeatable OnResume callback.
                for (var attempt = 0; attempt < 120 && agentService is null; attempt++)
                {
                    var app = Microsoft.Maui.Controls.Application.Current;
                    services = app?.Handler?.MauiContext?.Services;
                    agentType = FindAgentType();
                    if (services is not null && agentType is not null)
                        agentService = services.GetService(agentType);

                    if (agentService is null)
                        await Task.Delay(500);
                }

                if (agentType is null || agentService is null || services is null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "[Microsoft.Maui.DevFlow] Agent service not available after 60 seconds - CDP endpoint won't be available");
                    return;
                }

                // Wire WebViewLogCallback → Agent.WriteWebViewLog before subscribing for bridges,
                // so no failure after subscription can leave a duplicate handler on a retry.
                var writeLogMethod = agentType.GetMethod("WriteWebViewLog");
                if (writeLogMethod != null)
                {
                    blazorService.WebViewLogCallback = (level, message, exception) =>
                    {
                        try
                        {
                            writeLogMethod.Invoke(agentService, new object?[] { level, "WebView.Console", message, exception });
                        }
                        catch { /* ignore logging failures */ }
                    };
                }

                // Register each WebView bridge with the agent as they appear
                var registerMethod = agentType.GetMethod("RegisterCdpWebView");

                if (registerMethod != null)
                {
                    var registered = new HashSet<BlazorWebViewDebugServiceBase.WebViewBridge>(
                        ReferenceEqualityComparer.Instance);
                    var registering = new HashSet<BlazorWebViewDebugServiceBase.WebViewBridge>(
                        ReferenceEqualityComparer.Instance);
                    var registrationGate = new object();

                    async Task<bool> RegisterBridgeAsync(
                        int index,
                        BlazorWebViewDebugServiceBase.WebViewBridge bridge)
                    {
                        lock (registrationGate)
                        {
                            if (registered.Contains(bridge))
                                return true;
                            if (!registering.Add(bridge))
                                return true;
                        }

                        try
                        {
                            for (var attempt = 1; attempt <= 3; attempt++)
                            {
                                try
                                {
                                    // Serialize reflection calls because the agent registry is also
                                    // read by concurrent HTTP requests.
                                    lock (registrationGate)
                                    {
                                        registerMethod.Invoke(agentService, new object?[]
                                        {
                                            new Func<string, Task<string>>(bridge.SendCdpCommandAsync),
                                            new Func<bool>(() => bridge.IsReady),
                                            bridge.AutomationId,
                                            bridge.ElementId,
                                            null // url
                                        });
                                        registered.Add(bridge);
                                    }
                                    System.Diagnostics.Debug.WriteLine(
                                        $"[Microsoft.Maui.DevFlow] Registered CDP WebView bridge {index}");
                                    return true;
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine(
                                        $"[Microsoft.Maui.DevFlow] Failed to register CDP WebView bridge {index} " +
                                        $"(attempt {attempt}/3): {ex.Message}");
                                    if (attempt < 3)
                                        await Task.Delay(500);
                                }
                            }
                            return false;
                        }
                        finally
                        {
                            lock (registrationGate)
                                registering.Remove(bridge);
                        }
                    }

                    // Subscribe before enumerating existing bridges so a WebView created during
                    // wiring cannot be missed. Reference deduplication closes the overlap race.
                    bridgeAddedHandler = (index, bridge) => _ = RegisterBridgeAsync(index, bridge);
                    blazorService.WebViewBridgeAdded += bridgeAddedHandler;
                    var existingBridges = blazorService.Bridges;
                    var registrations = new List<Task<bool>>(existingBridges.Count);
                    for (var i = 0; i < existingBridges.Count; i++)
                        registrations.Add(RegisterBridgeAsync(i, existingBridges[i]));
                    await Task.WhenAll(registrations);
                }
                else
                {
                    // Fallback: legacy single-delegate wiring
                    var handlerProp = agentType.GetProperty("CdpCommandHandler");
                    var readyProp = agentType.GetProperty("CdpReadyCheck");

                    if (handlerProp != null)
                    {
                        var handler2 = new Func<string, Task<string>>(blazorService.SendCdpCommandAsync);
                        handlerProp.SetValue(agentService, handler2);
                    }

                    if (readyProp != null)
                    {
                        var readyCheck = new Func<bool>(() => blazorService.IsReady);
                        readyProp.SetValue(agentService, readyCheck);
                    }
                }

                wired = true;
                System.Diagnostics.Debug.WriteLine("[Microsoft.Maui.DevFlow] Blazor CDP wired to Agent /api/cdp endpoint");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] Failed to wire CDP to Agent: {ex.Message}");
            }
            finally
            {
                if (!wired)
                {
                    if (bridgeAddedHandler is not null)
                        blazorService.WebViewBridgeAdded -= bridgeAddedHandler;
                    blazorService.ResetAgentWiring();
                }
            }
        });
    }

    private static Type? FindAgentType()
    {
        var agentType = Type.GetType(
            "Microsoft.Maui.DevFlow.Agent.Core.DevFlowAgentService, Microsoft.Maui.DevFlow.Agent.Core");
        if (agentType is not null)
            return agentType;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            agentType = assembly.GetType("Microsoft.Maui.DevFlow.Agent.Core.DevFlowAgentService")
                ?? assembly.GetType("Microsoft.Maui.DevFlow.Agent.DevFlowAgentService");
            if (agentType is not null)
                return agentType;
        }

        return null;
    }
}
