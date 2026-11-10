using Microsoft.Maui.AI.Chat.Controls.Themes;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Registers the AI chat controls with a MAUI application.</summary>
public static class AppBuilderExtensions
{
    /// <summary>
    /// Registers the provider-neutral chat controls (see <c>UseChatControls</c>) and loads the AI chat
    /// theme into the application's resources at startup. Call it from <c>MauiProgram.CreateMauiApp()</c>.
    /// </summary>
    /// <param name="builder">The app builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static MauiAppBuilder UseCopilotChat(this MauiAppBuilder builder)
    {
        builder.UseChatControls();

        // Merge the AI theme into the app-level resources so that implicit styles for the
        // CopilotChatView content views resolve.
        builder.Services.AddSingleton<IMauiInitializeService, CopilotChatInitializer>();

        return builder;
    }

    private sealed class CopilotChatInitializer : IMauiInitializeService
    {
        public void Initialize(IServiceProvider services)
        {
            if (Application.Current is { } app)
                ChatThemeLoader.EnsureLoaded(app.Resources);
        }
    }
}

/// <summary>
/// Ensures the ChatTheme resource dictionary is loaded into the given resources.
/// </summary>
internal static class ChatThemeLoader
{
    public static void EnsureLoaded(ResourceDictionary resources)
    {
        // Check if already merged (no static flag — supports hot reload and tests)
        foreach (var dict in resources.MergedDictionaries)
        {
            if (dict is ChatTheme)
                return;
        }

        resources.MergedDictionaries.Add(new ChatTheme());
    }
}
