// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ProfilingHelper;

internal static class __MauiProfilingHelperInjectedBootstrap
{
    static readonly TimeSpan s_pollInterval = TimeSpan.FromMilliseconds(100);
    static readonly TimeSpan s_maxWait = TimeSpan.FromSeconds(30);
    static int s_completionSignaled;

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (!MauiProfilingMarker.IsProfilingSession)
            return;

        _ = WaitForStartupCompletionAsync();
    }

    static async Task WaitForStartupCompletionAsync()
    {
        try
        {
            var deadline = DateTime.UtcNow + s_maxWait;
            while (DateTime.UtcNow < deadline)
            {
                if (await IsMainPageReadyAsync().ConfigureAwait(false))
                {
                    break;
                }

                await Task.Delay(s_pollInterval).ConfigureAwait(false);
            }

            await SignalStartupCompleteOnMainThreadAsync().ConfigureAwait(false);
        }
        catch
        {
            // This bootstrap is best-effort; emitting the marker here avoids
            // hanging the profiling session if a platform-specific dispatcher call fails.
            SignalStartupComplete();
        }
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

        MauiProfilingMarker.Complete();
    }
}
