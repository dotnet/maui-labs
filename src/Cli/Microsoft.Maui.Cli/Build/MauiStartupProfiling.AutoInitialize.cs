// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.StartupProfiling;

internal static class __MauiStartupProfilingInjectedBootstrap
{
    static int s_completionSignaled;

    [ModuleInitializer]
    internal static void Initialize()
    {
        // Force the injected helper assembly to load immediately so its own module
        // initializer can wire up diagnostics and the exit-control channel.
        _ = StartupProfilingMarker.IsProfilingSession;

        if (!StartupProfilingMarker.IsProfilingSession)
            return;

        _ = WaitForStartupCompletionAsync();
    }

    static async Task WaitForStartupCompletionAsync()
    {
        for (var attempt = 0; attempt < 300; attempt++)
        {
            if (await IsMainPageReadyAsync().ConfigureAwait(false))
            {
                await SignalStartupCompleteOnMainThreadAsync().ConfigureAwait(false);
                return;
            }

            await Task.Delay(100).ConfigureAwait(false);
        }

        await SignalStartupCompleteOnMainThreadAsync().ConfigureAwait(false);
    }

    static async Task<bool> IsMainPageReadyAsync()
    {
        try
        {
            return await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var app = Application.Current;
                var page = app?.Windows?.FirstOrDefault()?.Page ?? app?.MainPage;
                return page?.Handler is not null;
            }).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    static async Task SignalStartupCompleteOnMainThreadAsync()
    {
        try
        {
            await MainThread.InvokeOnMainThreadAsync(SignalStartupComplete).ConfigureAwait(false);
        }
        catch
        {
            SignalStartupComplete();
        }
    }

    static void SignalStartupComplete()
    {
        if (Interlocked.Exchange(ref s_completionSignaled, 1) != 0)
            return;

        StartupProfilingMarker.Complete();
    }
}
