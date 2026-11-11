using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// The <c>set_theme</c> UI action, which switches the app to the light or dark theme, or back to the system's. The app
/// runs it, not the model's client, because it changes the app itself.
/// </summary>
/// <remarks>
/// The action is an <see cref="ApprovalRequiredAIFunction"/>, so it can ask before it runs.
/// <see cref="PlaygroundChatClient"/> declares it as one only while "Ask before running tools" is on; otherwise the
/// action runs as soon as the model calls it.
/// </remarks>
public static class ThemeAction
{
    /// <summary>The name of the action.</summary>
    public const string ToolName = "set_theme";

    /// <summary>Creates the action.</summary>
    /// <param name="apply">Applies the theme; <see cref="AppTheme.Unspecified"/> follows the system.</param>
    public static AIFunction Create(Action<AppTheme> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(
            ([Description("light, dark, or system")] string theme) =>
            {
                AppTheme? choice = theme.Trim().ToLowerInvariant() switch
                {
                    "light" => AppTheme.Light,
                    "dark" => AppTheme.Dark,
                    "system" => AppTheme.Unspecified,
                    _ => null,
                };
                if (choice is not { } appTheme)
                    return "Error: the theme must be light, dark, or system.";

                apply(appTheme);
                return appTheme == AppTheme.Unspecified
                    ? "The app now follows the system theme."
                    : $"The app now uses the {theme.Trim().ToLowerInvariant()} theme.";
            },
            ToolName,
            "Switches this app to the light or dark theme, or back to the system's theme."));
    }
}
