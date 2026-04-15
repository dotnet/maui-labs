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
    const string ProfilingEnvironmentVariable = "MAUI_STARTUP_PROFILING";
    const string DirectExitDelayEnvironmentVariable = "MAUI_STARTUP_PROFILING_DIRECT_EXIT_DELAY_MS";
    static int s_completionSignaled;

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (!IsProfilingSession())
            return;

        _ = WaitForStartupCompletionAsync();
    }

    static async Task WaitForStartupCompletionAsync()
    {
        for (var attempt = 0; attempt < 300; attempt++)
        {
            if (await IsMainPageReadyAsync().ConfigureAwait(false))
            {
                await CompleteOrExitAsync().ConfigureAwait(false);
                return;
            }

            await Task.Delay(100).ConfigureAwait(false);
        }

        await CompleteOrExitAsync().ConfigureAwait(false);
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

    static async Task CompleteOrExitAsync()
    {
        // For the simplified "exit from inside" experiment, do not rely on the
        // StartupComplete marker at all: just terminate from the app assembly.
        if (TryGetDirectExitDelay(out var delay) && delay > TimeSpan.Zero)
        {
            await Task.Delay(delay).ConfigureAwait(false);
            await ExitProcessAsync().ConfigureAwait(false);
            return;
        }

        await SignalStartupCompleteOnMainThreadAsync().ConfigureAwait(false);
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

    static async Task ExitProcessAsync()
    {
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() => Environment.Exit(0)).ConfigureAwait(false);
        }
        catch
        {
            Environment.Exit(0);
        }
    }

    static bool IsProfilingSession()
        => IsEnabledEnvironmentVariable(ProfilingEnvironmentVariable);

    static bool IsEnabledEnvironmentVariable(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    static bool TryGetDirectExitDelay(out TimeSpan delay)
    {
        delay = TimeSpan.Zero;

        var value = Environment.GetEnvironmentVariable(DirectExitDelayEnvironmentVariable);
        if (!int.TryParse(value, out var milliseconds) || milliseconds <= 0)
            return false;

        delay = TimeSpan.FromMilliseconds(milliseconds);
        return true;
    }
}
