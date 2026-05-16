// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Maui.AI.Controls.Controls;

/// <summary>
/// Reusable card that displays an agent <see cref="Plan"/> with step status indicators
/// and optional confirm/reject buttons for Human-in-the-Loop workflows.
/// </summary>
public partial class PlanCardView : ContentView
{
    public static readonly BindableProperty PlanProperty =
        BindableProperty.Create(nameof(Plan), typeof(Plan), typeof(PlanCardView), propertyChanged: OnPlanChanged);

    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(PlanCardView), "📋 Plan");

    public static readonly BindableProperty ShowConfirmationProperty =
        BindableProperty.Create(nameof(ShowConfirmation), typeof(bool), typeof(PlanCardView), false);

    public static readonly BindableProperty ConfirmTextProperty =
        BindableProperty.Create(nameof(ConfirmText), typeof(string), typeof(PlanCardView), "✅ Confirm");

    public static readonly BindableProperty RejectTextProperty =
        BindableProperty.Create(nameof(RejectText), typeof(string), typeof(PlanCardView), "❌ Reject");

    public static readonly BindableProperty CardBackgroundProperty =
        BindableProperty.Create(nameof(CardBackground), typeof(Color), typeof(PlanCardView));

    public static readonly BindableProperty CardStrokeProperty =
        BindableProperty.Create(nameof(CardStroke), typeof(Color), typeof(PlanCardView));

    public static readonly BindableProperty TitleColorProperty =
        BindableProperty.Create(nameof(TitleColor), typeof(Color), typeof(PlanCardView));

    /// <summary>The plan to display.</summary>
    public Plan? Plan
    {
        get => (Plan?)GetValue(PlanProperty);
        set => SetValue(PlanProperty, value);
    }

    /// <summary>Header title for the card.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Whether to show confirm/reject buttons.</summary>
    public bool ShowConfirmation
    {
        get => (bool)GetValue(ShowConfirmationProperty);
        set => SetValue(ShowConfirmationProperty, value);
    }

    /// <summary>Text for the confirm button.</summary>
    public string ConfirmText
    {
        get => (string)GetValue(ConfirmTextProperty);
        set => SetValue(ConfirmTextProperty, value);
    }

    /// <summary>Text for the reject button.</summary>
    public string RejectText
    {
        get => (string)GetValue(RejectTextProperty);
        set => SetValue(RejectTextProperty, value);
    }

    /// <summary>Background color of the card border.</summary>
    public Color CardBackground
    {
        get => (Color)GetValue(CardBackgroundProperty);
        set => SetValue(CardBackgroundProperty, value);
    }

    /// <summary>Stroke color of the card border.</summary>
    public Color CardStroke
    {
        get => (Color)GetValue(CardStrokeProperty);
        set => SetValue(CardStrokeProperty, value);
    }

    /// <summary>Color for the title text.</summary>
    public Color TitleColor
    {
        get => (Color)GetValue(TitleColorProperty);
        set => SetValue(TitleColorProperty, value);
    }

    /// <summary>Raised when the user taps Confirm.</summary>
    public event EventHandler? Confirmed;

    /// <summary>Raised when the user taps Reject.</summary>
    public event EventHandler? Rejected;

    public PlanCardView()
    {
        InitializeComponent();
    }

    private static void OnPlanChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is PlanCardView card)
            card.RefreshSteps();
    }

    /// <summary>Rebuilds the step rows from the current <see cref="Plan"/>.</summary>
    public void RefreshSteps()
    {
        StepsLayout.Children.Clear();

        if (Plan is null) return;

        foreach (var step in Plan.Steps)
        {
            var row = new HorizontalStackLayout { Spacing = 8 };
            row.Children.Add(new Label
            {
                Text = step.StatusText,
                FontSize = 14,
                VerticalOptions = LayoutOptions.Center
            });
            row.Children.Add(new Label
            {
                Text = step.Description,
                FontSize = 13,
                VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.WordWrap,
                TextDecorations = step.IsCompleted ? TextDecorations.Strikethrough : TextDecorations.None,
                Opacity = step.IsCompleted ? 0.6 : 1.0
            });
            StepsLayout.Children.Add(row);
        }
    }

    private void OnConfirmClicked(object? sender, EventArgs e) => Confirmed?.Invoke(this, EventArgs.Empty);

    private void OnRejectClicked(object? sender, EventArgs e) => Rejected?.Invoke(this, EventArgs.Empty);
}
