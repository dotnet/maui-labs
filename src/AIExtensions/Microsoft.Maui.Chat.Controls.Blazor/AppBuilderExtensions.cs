// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Chat.Controls.Themes;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Chat.Controls.Blazor;

/// <summary>Registers the neutral Blazor Hybrid chat controls with a MAUI application.</summary>
public static class AppBuilderExtensions
{
    /// <summary>
    /// Ensures the shared <c>Microsoft.Maui.Chat.Controls</c> theme is loaded at startup and
    /// records the DI marker service the Blazor components look for. Call it from
    /// <c>MauiProgram.CreateMauiApp()</c>.
    /// </summary>
    /// <param name="builder">The MAUI app builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The Blazor components do not depend on any XAML resources at runtime — they render into
    /// the WebView — but calling this method keeps the composer's optional platform services
    /// (attachment picker, audio recorder, speech recognizer) available even when the app has
    /// not called the native <c>UseChatControls()</c>.
    /// </para>
    /// <para>
    /// The static assets (<c>mchat.css</c>, <c>mchat.js</c>) still have to be referenced by the
    /// host <c>index.html</c>: link them from
    /// <c>_content/Microsoft.Maui.Chat.Controls.Blazor/mchat.css</c> and
    /// <c>_content/Microsoft.Maui.Chat.Controls.Blazor/mchat.js</c>.
    /// </para>
    /// </remarks>
    public static MauiAppBuilder AddChatControlsBlazor(this MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<IMauiInitializeService, ChatControlsBlazorInitializer>();
        return builder;
    }

    private sealed class ChatControlsBlazorInitializer : IMauiInitializeService
    {
        public void Initialize(IServiceProvider services) =>
            // The neutral XAML theme carries the MauiChat.* colour tokens the sample can bind
            // via App.xaml. Loading it defensively costs nothing on the Blazor side and keeps
            // hybrid apps with both native and Blazor chat pages consistent.
            ChatControlsTheme.EnsureLoaded();
    }
}
