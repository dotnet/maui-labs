#nullable enable
using Comet;
using Microsoft.Maui.Graphics;

namespace CometSamples.BaristaNotes.Styles;

/// <summary>
/// Material Symbols glyph constants used as tab/toolbar icons.
/// The host registers the source alias <c>MaterialIcons</c> for the
/// Material Symbols font.
/// </summary>
public static class CoffeeIcons
{
    public const string FontFamily = "MaterialIcons";

    // Tab bar
    public const string Coffee   = "\uefef";
    public const string Feed     = "\uf009";
    public const string Settings = "\ue8b8"; // settings

    // Toolbar / inline
    public const string Add      = "\ue145";
    public const string Edit     = "\ue3c9";
    public const string Delete   = "\ue872";
    public const string Filter   = "\ue152"; // filter_list
    public const string Search   = "\ue8b6";
    public const string Mic      = "\ue029";
    public const string Camera   = "\ue3b0";
    public const string Ai       = "\uf136";
    public const string Back     = "\ue5c4";
    public const string ArrowRight = "\ue941";
    public const string Close    = "\ue5cd";
    public const string Collapse = "\ue5cf";
    public const string Chevron  = "\ue5cc";
    public const string Check    = "\ue5ca";
    public const string Person   = "\ue7fd";
    public const string Machine  = "\ue547";
    public const string Grinder  = "\ue3f4";
    public const string Bag      = "\ue8cc";
    public const string AddPhoto = "\ue439";

    // Rating sentiment
    public const string SentimentVeryDissatisfied = "\ue760";
    public const string SentimentDissatisfied     = "\ue811";
    public const string SentimentNeutral          = "\ue812";
    public const string SentimentSatisfied        = "\ue813";
    public const string SentimentVerySatisfied    = "\ue814";

    public static string RatingIcon(int rating) => rating switch
    {
        0 => SentimentVeryDissatisfied,
        1 => SentimentDissatisfied,
        3 => SentimentSatisfied,
        4 => SentimentVerySatisfied,
        _ => SentimentNeutral,
    };

    public static FontImageSource Source(string glyph, Color color, double size = 32) =>
        new(FontFamily, glyph, size, color);
}
