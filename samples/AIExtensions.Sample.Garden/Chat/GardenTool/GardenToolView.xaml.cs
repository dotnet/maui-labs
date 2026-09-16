using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls;

namespace AIExtensions.Sample.Garden.Chat;

/// <summary>
/// Renders a <see cref="FunctionInvocationContentBlock"/> (any tool not projected into a richer block,
/// e.g. cart, orders, navigation, reviews) as a compact, tap-to-expand card (see GardenToolView.xaml):
/// tool name up top, and — once expanded — its arguments and result.
/// <para>
/// The visual tree lives in XAML; this code-behind maps the block onto bindable properties and owns the
/// expand/collapse state.
/// </para>
/// </summary>
public partial class GardenToolView : ContentContextView
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static readonly BindableProperty DisplayNameProperty =
        BindableProperty.Create(nameof(DisplayName), typeof(string), typeof(GardenToolView));

    public static readonly BindableProperty ArgsTextProperty =
        BindableProperty.Create(nameof(ArgsText), typeof(string), typeof(GardenToolView),
            propertyChanged: (b, _, _) => ((GardenToolView)b).OnDetailsChanged());

    public static readonly BindableProperty ResultTextProperty =
        BindableProperty.Create(nameof(ResultText), typeof(string), typeof(GardenToolView),
            propertyChanged: (b, _, _) => ((GardenToolView)b).OnDetailsChanged());

    public static readonly BindableProperty HasArgsProperty =
        BindableProperty.Create(nameof(HasArgs), typeof(bool), typeof(GardenToolView));

    public static readonly BindableProperty HasResultProperty =
        BindableProperty.Create(nameof(HasResult), typeof(bool), typeof(GardenToolView));

    public static readonly BindableProperty HasDetailsProperty =
        BindableProperty.Create(nameof(HasDetails), typeof(bool), typeof(GardenToolView));

    public static readonly BindableProperty IsExpandedProperty =
        BindableProperty.Create(nameof(IsExpanded), typeof(bool), typeof(GardenToolView), false,
            propertyChanged: (b, _, _) => ((GardenToolView)b).OnIsExpandedChanged());

    public static readonly BindableProperty ExpandGlyphProperty =
        BindableProperty.Create(nameof(ExpandGlyph), typeof(string), typeof(GardenToolView), FluentIcons.ChevronRight);

    public GardenToolView()
    {
        ToggleCommand = new RelayCommand(() =>
        {
            if (HasDetails)
                IsExpanded = !IsExpanded;
        });
        InitializeComponent();
    }

    /// <summary>Tool name, with a trailing ellipsis while the call is still pending.</summary>
    public string? DisplayName
    {
        get => (string?)GetValue(DisplayNameProperty);
        set => SetValue(DisplayNameProperty, value);
    }

    public string? ArgsText
    {
        get => (string?)GetValue(ArgsTextProperty);
        set => SetValue(ArgsTextProperty, value);
    }

    public string? ResultText
    {
        get => (string?)GetValue(ResultTextProperty);
        set => SetValue(ResultTextProperty, value);
    }

    public bool HasArgs
    {
        get => (bool)GetValue(HasArgsProperty);
        set => SetValue(HasArgsProperty, value);
    }

    public bool HasResult
    {
        get => (bool)GetValue(HasResultProperty);
        set => SetValue(HasResultProperty, value);
    }

    public bool HasDetails
    {
        get => (bool)GetValue(HasDetailsProperty);
        set => SetValue(HasDetailsProperty, value);
    }

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>Chevron glyph — right when collapsed, down when expanded.</summary>
    public string ExpandGlyph
    {
        get => (string)GetValue(ExpandGlyphProperty);
        set => SetValue(ExpandGlyphProperty, value);
    }

    public IRelayCommand ToggleCommand { get; }

    protected override void RefreshFromContentContext()
    {
        if (ContentContext?.Block is not FunctionInvocationContentBlock block)
        {
            DisplayName = null;
            ArgsText = ResultText = null;
            return;
        }

        DisplayName = block.HasResult ? block.ToolName : $"{block.ToolName}\u2026";
        ArgsText = BuildArgs(block);
        ResultText = BuildResult(block);
    }

    private void OnDetailsChanged()
    {
        HasArgs = !string.IsNullOrEmpty(ArgsText);
        HasResult = !string.IsNullOrEmpty(ResultText);
        HasDetails = HasArgs || HasResult;
        if (!HasDetails)
            IsExpanded = false;
    }

    private void OnIsExpandedChanged() =>
        ExpandGlyph = IsExpanded ? FluentIcons.ChevronDown : FluentIcons.ChevronRight;

    private static string? BuildArgs(FunctionInvocationContentBlock block)
    {
        if (block.Arguments is not { Count: > 0 } args)
            return null;
        return string.Join("\n", args.Select(kv => $"{kv.Key}: {kv.Value}"));
    }

    private static string? BuildResult(FunctionInvocationContentBlock block)
    {
        var result = block.Result?.Result;
        if (result is null)
            return null;

        try
        {
            return result switch
            {
                string s => s,
                _ => JsonSerializer.Serialize(result, JsonOptions),
            };
        }
        catch
        {
            return result.ToString();
        }
    }
}
