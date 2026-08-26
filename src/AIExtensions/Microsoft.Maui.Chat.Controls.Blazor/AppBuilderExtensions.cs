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
    /// registers the neutral multimodal service defaults so the Blazor composer resolves the same
    /// <see cref="IChatAttachmentPicker"/>, <see cref="IChatAudioRecorder"/>, and
    /// <see cref="IChatSpeechRecognizer"/> that the native XAML control uses. Call it from
    /// <c>MauiProgram.CreateMauiApp()</c>.
    /// </summary>
    /// <param name="builder">The MAUI app builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This method delegates to
    /// <see cref="Microsoft.Maui.Chat.Controls.AppBuilderExtensions.AddChatControlsDefaults"/> so
    /// calling <see cref="AddChatControlsBlazor"/> alone (without also calling
    /// <see cref="Microsoft.Maui.Chat.Controls.AppBuilderExtensions.UseChatControls"/>) still yields
    /// resolvable service defaults for the Blazor composer.
    /// </para>
    /// <para>
    /// The registrations use <c>TryAddSingleton</c>, so an app-supplied registration (a simulated
    /// recorder in tests, a cloud picker) always wins if registered first.
    /// </para>
    /// <para>
    /// The static assets (<c>mchat.css</c>, <c>mchat.js</c>) still have to be referenced by the
    /// host <c>index.html</c>: link them from
    /// <c>_content/Microsoft.Maui.Chat.Controls.Blazor/mchat.css</c>.
    /// </para>
    /// </remarks>
    public static MauiAppBuilder AddChatControlsBlazor(this MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<IMauiInitializeService, ChatControlsBlazorInitializer>();
        builder.Services.AddChatControlsDefaults();
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

