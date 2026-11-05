#nullable enable
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using CometSamples.BaristaNotes.Styles;

namespace CometSamples.BaristaNotes.Components;

/// <summary>
/// Shared component factories for BaristaNotes form fields.
/// </summary>
public static class FormHelpers
{
    /// <summary>Empty-state placeholder with icon + message.</summary>
    public static View MakeEmptyState(string icon, string message, string? submessage = null)
    {
        var stack = new VStack(spacing: CoffeeSpacing.M)
        {
            new Text(icon)
                .FontFamily(CoffeeIcons.FontFamily)
                .FontSize(64f)
                .Color(CoffeeTheme.TextMuted),
            new Text(message).SecondaryText(),
        };
        if (submessage is not null)
            stack.Add(new Text(submessage).MutedText());
        return stack.Alignment(Comet.Alignment.Center);
    }
}
