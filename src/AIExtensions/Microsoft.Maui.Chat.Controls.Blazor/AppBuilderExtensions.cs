// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Chat.Controls.Themes;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Chat.Controls.Blazor;

/// <summary>Registers the neutral Blazor Hybrid chat controls with a MAUI application.</summary>
public static class AppBuilderExtensions
{
    /// <summary>
    /// Registers the Blazor Hybrid chat controls and neutral default composer services.
    /// </summary>
    /// <param name="builder">The MAUI app builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The static assets (<c>mchat.css</c>, <c>mchat.js</c>) still have to be referenced by the
    /// host <c>index.html</c>: link them from
    /// <c>_content/Microsoft.Maui.Chat.Controls.Blazor/mchat.css</c>.
    /// </para>
    /// </remarks>
    public static MauiAppBuilder AddChatControlsBlazor(this MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Microsoft.Maui.Chat.Controls.AppBuilderExtensions.AddChatControlsDefaults(builder.Services);
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IMauiInitializeService, ChatControlsBlazorInitializer>());
        return builder;
    }

    private sealed class ChatControlsBlazorInitializer : IMauiInitializeService
    {
        public void Initialize(IServiceProvider services) =>
            ChatControlsTheme.EnsureLoaded();
    }
}
