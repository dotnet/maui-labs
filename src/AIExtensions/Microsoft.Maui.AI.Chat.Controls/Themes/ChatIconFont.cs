using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// The icon font embedded in this library: AIChatIcons, a subset of the Fluent UI System Icons. The theme's
/// <see cref="AIChatThemeKeys.IconFontFamily"/> key names it, and the <c>AIChat.Icon.*</c> keys hold its glyphs.
/// </summary>
internal static class ChatIconFont
{
    public const string FileName = "AIChatIcons.ttf";

    public const string FamilyName = "AIChatIcons";

    private static readonly ConditionalWeakTable<IFontRegistrar, string> Registrars = new();

    /// <summary>
    /// Registers the font with the app's fonts, once per app, so the app needs no <c>ConfigureFonts</c> call. Call it
    /// before the parts that use the font get their handlers: the registrar remembers a font it could not find.
    /// </summary>
    public static void Register(IServiceProvider? services)
    {
        if (services?.GetService<IFontRegistrar>() is { } registrar && Registrars.TryAdd(registrar, FamilyName))
            registrar.Register(FileName, FamilyName, typeof(ChatIconFont).Assembly);
    }
}
