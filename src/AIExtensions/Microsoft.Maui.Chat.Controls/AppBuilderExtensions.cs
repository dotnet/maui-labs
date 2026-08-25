using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Chat.Controls.Themes;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Chat.Controls;

/// <summary>Registers the chat controls with a MAUI application.</summary>
public static class AppBuilderExtensions
{
    /// <summary>
    /// Loads the chat control theme into the application's resources at startup, and registers the
    /// default multimodal services (<see cref="IChatAttachmentPicker"/>, <see cref="IChatAudioRecorder"/>,
    /// <see cref="IChatSpeechRecognizer"/>) via <c>TryAddSingleton</c> so <see cref="ChatView"/>
    /// consumers, downstream Blazor hosts, and DI-driven composers all resolve the same defaults.
    /// Call it from <c>MauiProgram.CreateMauiApp()</c>.
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
    /// implementation (a simulated recorder in tests, a cloud-backed picker, and so on) before
    /// calling <see cref="UseChatControls"/> keeps its registration.
    /// </para>
    /// </remarks>
    public static MauiAppBuilder UseChatControls(this MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<IMauiInitializeService, ChatControlsInitializer>();

        // Register the default multimodal services so downstream code (Blazor Hybrid,
        // custom composer templates, tests) can resolve them from DI. TryAddSingleton
        // means an app-supplied registration wins.
        builder.Services.TryAddSingleton<IChatAttachmentPicker>(FileChatAttachmentPicker.Default);
        builder.Services.TryAddSingleton<IChatAudioRecorder, MauiChatAudioRecorder>();
        builder.Services.TryAddSingleton<IChatSpeechRecognizer, MauiChatSpeechRecognizer>();

        return builder;
    }

    private sealed class ChatControlsInitializer : IMauiInitializeService
    {
        public void Initialize(IServiceProvider services) => ChatControlsTheme.EnsureLoaded();
    }
}

