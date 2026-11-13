namespace AIExtensions.Sample.ChatPlayground;

/// <summary>The shared notes, as a bar below the messages or a column beside them.</summary>
public partial class NotesView : ContentView
{
    /// <summary>Identifies the <see cref="IsCompact"/> property.</summary>
    public static readonly BindableProperty IsCompactProperty = BindableProperty.Create(
        nameof(IsCompact), typeof(bool), typeof(NotesView), false);

    /// <summary>Initializes the notes view.</summary>
    public NotesView() => InitializeComponent();

    /// <summary>
    /// Gets or sets whether the notes are a bar on a narrow screen, collapsed until tapped, rather than a column.
    /// </summary>
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }
}
