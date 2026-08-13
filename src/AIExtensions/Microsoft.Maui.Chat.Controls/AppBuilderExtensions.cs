using Microsoft.Maui.Chat.Controls.Themes;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Chat.Controls;

/// <summary>Registers the chat controls with a MAUI application.</summary>
public static class AppBuilderExtensions
{
    /// <summary>
    /// Loads the chat control theme into the application's resources at startup. Call it from
    /// <c>MauiProgram.CreateMauiApp()</c>.
    /// </summary>
    /// <param name="builder">The app builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This is optional: a chat control loads the theme itself when it joins a visual tree. Calling it
    /// makes the <c>MauiChat.*</c> resources available before that, so application resources can build on
    /// them.
    /// </remarks>
    public static MauiAppBuilder UseChatControls(this MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<IMauiInitializeService, ChatControlsInitializer>();
        return builder;
    }

    private sealed class ChatControlsInitializer : IMauiInitializeService
    {
        public void Initialize(IServiceProvider services) => ChatControlsTheme.EnsureLoaded();
    }
}
