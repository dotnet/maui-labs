using System.Windows.Input;

namespace Microsoft.Maui.AI.Controls.Controls;

/// <summary>
/// A horizontal scrollable bar of suggestion buttons.
/// Hidden when <see cref="Suggestions"/> is empty or null.
/// </summary>
public partial class SuggestionBar : ContentView
{
    public static readonly BindableProperty SuggestionsProperty =
        BindableProperty.Create(
            nameof(Suggestions),
            typeof(IList<Suggestion>),
            typeof(SuggestionBar),
            null,
            propertyChanged: OnSuggestionsChanged);

    public static readonly BindableProperty SuggestionTappedProperty =
        BindableProperty.Create(
            nameof(SuggestionTapped),
            typeof(ICommand),
            typeof(SuggestionBar));

    public IList<Suggestion>? Suggestions
    {
        get => (IList<Suggestion>?)GetValue(SuggestionsProperty);
        set => SetValue(SuggestionsProperty, value);
    }

    public ICommand? SuggestionTapped
    {
        get => (ICommand?)GetValue(SuggestionTappedProperty);
        set => SetValue(SuggestionTappedProperty, value);
    }

    public SuggestionBar()
    {
        InitializeComponent();
        UpdateVisibility();
    }

    private static void OnSuggestionsChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is SuggestionBar bar)
            bar.UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        IsVisible = Suggestions is { Count: > 0 };
    }
}
