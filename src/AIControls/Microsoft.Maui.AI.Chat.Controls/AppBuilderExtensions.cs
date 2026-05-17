using Microsoft.Maui.AI.Chat.Controls.Themes;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Extension methods for registering the AI Chat Controls library.
/// </summary>
public static class AppBuilderExtensions
{
    /// <summary>
    /// Registers AI Chat Controls, including default themes and resources.
    /// Call this in your <c>MauiProgram.CreateMauiApp()</c> builder chain.
    /// </summary>
    public static MauiAppBuilder UseChatControls(this MauiAppBuilder builder)
    {
        builder.ConfigureMauiHandlers(_ => { });

        // Merge the default ChatTheme into the app-level resources so that
        // implicit Styles for CopilotChatView and content templates resolve.
        var themeRegistered = false;
        builder.Services.AddSingleton<IMauiInitializeService>(
            new ChatControlsInitializer(() => themeRegistered, () => themeRegistered = true));

        return builder;
    }

    private sealed class ChatControlsInitializer(Func<bool> isRegistered, Action markRegistered) : IMauiInitializeService
    {
        public void Initialize(IServiceProvider services)
        {
            if (isRegistered())
                return;

            var app = Application.Current;
            if (app is null)
                return;

            ChatThemeLoader.EnsureLoaded(app.Resources);
            markRegistered();
        }
    }
}

/// <summary>
/// Ensures the ChatTheme resource dictionary is loaded into the given resources.
/// </summary>
internal static class ChatThemeLoader
{
    private static bool _loaded;

    public static void EnsureLoaded(ResourceDictionary resources)
    {
        if (_loaded)
            return;

        // Check if already merged
        foreach (var dict in resources.MergedDictionaries)
        {
            if (dict is ChatTheme)
                return;
        }

        resources.MergedDictionaries.Add(new ChatTheme());
        _loaded = true;
    }
}
