using System.Collections.Generic;
using Microsoft.Maui.AI.Chat.Controls;

namespace AIExtensions.Sample.Garden.Chat;

/// <summary>
/// Renders a <see cref="GardenFormattedTextBlock"/> as an assistant speech bubble (see
/// GardenFormattedTextView.xaml). All parsing happens in the handler; this code-behind only exposes the
/// pre-parsed lines, and a converter turns each line into a MAUI <see cref="FormattedString"/>.
/// </summary>
public partial class GardenFormattedTextView : ContentContextView
{
    public static readonly BindableProperty LinesProperty =
        BindableProperty.Create(nameof(Lines), typeof(IReadOnlyList<FormattedLine>), typeof(GardenFormattedTextView));

    public GardenFormattedTextView()
    {
        InitializeComponent();
    }

    /// <summary>The parsed lines, as a fresh list per refresh so the bindable layout re-renders.</summary>
    public IReadOnlyList<FormattedLine>? Lines
    {
        get => (IReadOnlyList<FormattedLine>?)GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    protected override void RefreshFromContentContext()
    {
        Lines = ContentContext?.Block is GardenFormattedTextBlock block
            ? [.. block.Lines]
            : null;
    }
}
