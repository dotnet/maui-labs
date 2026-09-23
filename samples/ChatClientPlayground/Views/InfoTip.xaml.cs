namespace ChatClientPlayground.Views;

/// <summary>Shows concise accessible help for a nearby setting.</summary>
public partial class InfoTip : ContentView
{
    /// <summary>Identifies the tooltip and semantic help text.</summary>
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(InfoTip),
        string.Empty,
        propertyChanged: static (bindable, _, value) =>
            ((InfoTip)bindable).ApplyText((string?)value));

    /// <summary>Gets or sets the tooltip and semantic help text.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Initializes the info affordance.</summary>
    public InfoTip()
    {
        InitializeComponent();
        ApplyText(Text);
    }

    private void ApplyText(string? text)
    {
        var helpText = text ?? string.Empty;
        ToolTipProperties.SetText(this, helpText);
        ToolTipProperties.SetText(InfoLabel, helpText);
        SemanticProperties.SetDescription(this, helpText);
        SemanticProperties.SetDescription(InfoLabel, helpText);
    }
}
