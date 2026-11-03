using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Chat.Controls.Themes;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Chat.Controls;

/// <summary>Registers the chat controls with a MAUI application.</summary>
public static class AppBuilderExtensions
{
    /// <summary>
    /// Loads the chat control theme into the application's resources at startup and registers the
    /// neutral multimodal service defaults via <see cref="AddChatControlsDefaults"/>. Call it from
    /// <c>MauiProgram.CreateMauiApp()</c>.
    /// </summary>
    /// <param name="builder">The app builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The theme part is optional for the native XAML controls: they load the theme themselves when
    /// they join a visual tree. Calling it up front makes the <c>MauiChat.*</c> resources available
    /// before that, so application resources can build on them.
    /// </para>
    /// <para>
    /// The service registrations use <c>TryAddSingleton</c>, so an app that registers its own
    /// implementation before calling <see cref="UseChatControls"/> keeps its registration.
    /// </para>
    /// </remarks>
    public static MauiAppBuilder UseChatControls(this MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<IMauiInitializeService, ChatControlsInitializer>();
        builder.Services.AddChatControlsDefaults();
        return builder;
    }

    /// <summary>
    /// Registers the neutral <see cref="IChatAttachmentPicker"/>, <see cref="IChatAudioRecorder"/>,
    /// and <see cref="IChatSpeechRecognizer"/> defaults via <c>TryAddSingleton</c> so a downstream
    /// package (for example the Blazor Hybrid chat controls) can chain to a single source of truth.
    /// </summary>
    /// <param name="services">The service collection to extend.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This is the seam layer-2 packages (and other consumers) call to guarantee the neutral
    /// defaults are present regardless of whether the app also called <see cref="UseChatControls"/>.
    /// The registrations use <c>TryAddSingleton</c>, so an app-supplied registration always wins.
    /// </remarks>
    public static IServiceCollection AddChatControlsDefaults(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IChatAttachmentPicker>(FileChatAttachmentPicker.Default);
        services.TryAddSingleton<IChatAudioRecorder, MauiChatAudioRecorder>();
        services.TryAddSingleton<IChatSpeechRecognizer, MauiChatSpeechRecognizer>();
        return services;
    }

    private sealed class ChatControlsInitializer : IMauiInitializeService
    {
        public void Initialize(IServiceProvider services) => ChatControlsTheme.EnsureLoaded();
    }
}


