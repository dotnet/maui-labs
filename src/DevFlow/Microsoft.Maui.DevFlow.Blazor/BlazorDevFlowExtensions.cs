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
        // Delay to let the agent start first
        Task.Run(async () =>
        {
            await Task.Delay(1000);
            try
            {
                var app = Microsoft.Maui.Controls.Application.Current;
                if (app == null) return;

                // Find DevFlowAgentService via reflection to avoid package dependency
                var handler = app.Handler;
                var services = handler?.MauiContext?.Services;
                if (services == null) return;

                // Look for the agent service by type name
                foreach (var svcDescriptor in services.GetServices<object>())
                {
                    // Skip non-agent types
                }

                // Try to get the agent service directly by its well-known type
                var agentType = Type.GetType("Microsoft.Maui.DevFlow.Agent.Core.DevFlowAgentService, Microsoft.Maui.DevFlow.Agent.Abstractions");
                if (agentType == null)
                {
                    // Try scanning loaded assemblies for the Core type
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        agentType = asm.GetType("Microsoft.Maui.DevFlow.Agent.Core.DevFlowAgentService");
                        if (agentType != null) break;
                    }
                }

                if (agentType == null)
                {
                    // Fallback: try legacy type name
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        agentType = asm.GetType("Microsoft.Maui.DevFlow.Agent.DevFlowAgentService");
                        if (agentType != null) break;
                    }
                }

                if (agentType == null)
                {
                    System.Diagnostics.Debug.WriteLine("[Microsoft.Maui.DevFlow] Agent service type not found - CDP endpoint won't be available");
                    return;
                }

                var agentService = services.GetService(agentType);
                if (agentService == null)
                {
                    System.Diagnostics.Debug.WriteLine("[Microsoft.Maui.DevFlow] Agent service not registered - CDP endpoint won't be available");
                    return;
                }

                // Register each WebView bridge with the agent as they appear
                var registerMethod = agentType.GetMethod("RegisterCdpWebView");
                var updateMethod = agentType.GetMethod("UpdateCdpWebView");

                if (registerMethod != null)
                {
                    // Register existing bridges (if any were created before wiring)
                    foreach (var bridge in blazorService.Bridges)
                    {
                        var bridgeRef = bridge; // capture for closure
                        registerMethod.Invoke(agentService, new object?[]
                        {
                            new Func<string, Task<string>>(bridgeRef.SendCdpCommandAsync),
                            new Func<bool>(() => bridgeRef.IsReady),
                            bridgeRef.AutomationId,
                            bridgeRef.ElementId,
                            null // url
                        });
                    }

                    // Watch for new bridges via polling (bridges added when WebViews appear)
                    var registeredCount = blazorService.Bridges.Count;
                    _ = Task.Run(async () =>
                    {
                        // Poll for new bridges for up to 10 minutes
                        for (int i = 0; i < 600; i++)
                        {
                            await Task.Delay(1000);
                            while (registeredCount < blazorService.Bridges.Count)
                            {
                                var bridge = blazorService.Bridges[registeredCount];
                                try
                                {
                                    registerMethod.Invoke(agentService, new object?[]
                                    {
                                        new Func<string, Task<string>>(bridge.SendCdpCommandAsync),
                                        new Func<bool>(() => bridge.IsReady),
                                        bridge.AutomationId,
                                        bridge.ElementId,
                                        null
                                    });
                                    System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] Registered CDP WebView bridge {registeredCount}");
                                }
                                catch { }
                                registeredCount++;
                            }
                        }
                    });
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

                // Wire WebViewLogCallback → Agent.WriteWebViewLog
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

                System.Diagnostics.Debug.WriteLine("[Microsoft.Maui.DevFlow] Blazor CDP wired to Agent /api/cdp endpoint");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Microsoft.Maui.DevFlow] Failed to wire CDP to Agent: {ex.Message}");
            }
        });
    }
}
