using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Shows a <c>set_accent_color</c> UI action: running, or the color it set.</summary>
public partial class AccentActionView : ScenarioCardView
{
    public AccentActionView() => InitializeComponent();

    protected override void Refresh()
    {
        if (TitleLabel is null || Block is not UIActionBlock action)
            return;

        var requested = ScenarioJson.ReadString(action.Arguments, "color") ?? "a color";
        var isColor = AccentColorController.TryParse(requested, out var color);
        Swatch.BackgroundColor = isColor ? color : Colors.Transparent;

        if (!action.IsComplete)
        {
            TitleLabel.Text = IsRunning(action) ? "Running set_accent_color in the app\u2026" : "set_accent_color did not run.";
            DetailLabel.Text = requested;
        }
        else if (isColor)
        {
            // A name such as "rebeccapurple" also shows the color it became; a hex value shows once.
            var hex = AccentColorController.ToHex(color);
            TitleLabel.Text = "Ran set_accent_color in the app";
            DetailLabel.Text = string.Equals(requested.Trim(), hex, StringComparison.OrdinalIgnoreCase)
                ? hex
                : $"{requested} \u00B7 {hex}";
        }
        else
        {
            TitleLabel.Text = "set_accent_color ran, but nothing changed";
            DetailLabel.Text = $"\u201C{requested}\u201D is not a color.";
        }

        SemanticProperties.SetDescription(Card, $"{TitleLabel.Text}. {DetailLabel.Text}");
    }
}
