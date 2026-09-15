namespace Microsoft.Maui.Chat.Controls.Themes;

/// <summary>
/// The complete default theme for the chat controls: styles plus the control templates for
/// <see cref="ChatMessagesView"/> and <see cref="ChatView"/>.
/// </summary>
/// <remarks>
/// The controls load this dictionary themselves when they join a visual tree, so nothing has to be
/// registered for them to look right. Merge it explicitly (or call
/// <see cref="AppBuilderExtensions.UseChatControls"/>) to load it earlier, and override any
/// <c>MauiChat.*</c> key afterwards to restyle.
/// </remarks>
public partial class ChatControlsTheme : ResourceDictionary
{
    /// <summary>Creates the dictionary.</summary>
    public ChatControlsTheme() => InitializeComponent();

    /// <summary>Merges the theme into the current application's resources when it is not already there.</summary>
    public static void EnsureLoaded()
    {
        if (Application.Current is { } application)
            EnsureLoaded(application.Resources);
    }

    /// <summary>Merges the theme into <paramref name="resources"/> when it is not already there.</summary>
    /// <param name="resources">The dictionary to merge into, usually <c>Application.Current.Resources</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resources"/> is <see langword="null"/>.</exception>
    public static void EnsureLoaded(ResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        // No static flag: hot reload and tests can rebuild the resources at any time.
        foreach (var dictionary in resources.MergedDictionaries)
        {
            if (dictionary is ChatControlsTheme)
                return;
        }

        resources.MergedDictionaries.Add(new ChatControlsTheme());
    }
}
