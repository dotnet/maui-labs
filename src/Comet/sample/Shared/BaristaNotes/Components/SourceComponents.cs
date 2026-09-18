#nullable enable
using System;
using System.Collections.Generic;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using CometSamples.BaristaNotes.Styles;

namespace CometSamples.BaristaNotes.Components;

public sealed record BottomAction(
    string AutomationId,
    string Icon,
    Action Invoke,
    string? Label = null,
    bool Inverted = false);

public abstract class BaristaManagementPage : View
{
    protected BaristaManagementPage() =>
        this.BackButtonBehavior(new BackButtonBehavior { IsVisible = false });
}

public static class BaristaPageHeader
{
    public static View Build(
        string label,
        string title,
        Thickness safeArea,
        string automationId,
        double titleFontSize = 28,
        string? titleAutomationId = null,
        string? labelAutomationId = null,
        bool rangeCaption = false)
    {
        var caption = new Text(label);
        if (rangeCaption)
            caption.RangeCaption();
        else
            caption.SectionLabel();
        if (labelAutomationId is not null)
            caption.AutomationId(labelAutomationId);

        var value = new Text(title)
            .FontFamily("ManropeSemibold")
            .FontSize(titleFontSize)
            .Color(CoffeeTheme.TextPrimary)
            .LineBreakMode(LineBreakMode.WordWrap)
            .MaxLines(2)
            .Alignment(Comet.Alignment.BottomLeading);
        if (titleAutomationId is not null)
            value.AutomationId(titleAutomationId);

        return new Grid(columns: new object[] { "*" }, rows: new object[] { "Auto", "*" })
        {
            caption.Cell(row: 0),
            value.Cell(row: 1),
        }
        .Padding(BaristaSafeAreaLayout.HeaderPadding(safeArea))
        .MinimumHeight(BaristaSafeAreaLayout.HeaderMinimumHeight())
        .Background(CoffeeTheme.SurfaceColor)
        .AutomationId(automationId);
    }

    public static double TitleFontSize(string title) => title.Length switch
    {
        <= 12 => 28,
        <= 20 => 22,
        <= 28 => 18,
        _ => 16,
    };
}

public static class BaristaSections
{
    public static VStack Create(params View[] sections)
    {
        var stack = new VStack(spacing: CoffeeSpacing.Divider)
            .Background(CoffeeTheme.OutlineColor);
        foreach (var section in sections)
            stack.Add(section);
        return stack;
    }

    public static View Scroll(VStack sections, string? automationId = null)
    {
        var scroll = new ScrollView { sections }.Background(CoffeeTheme.SurfaceColor);
        if (automationId is not null)
            scroll.AutomationId(automationId);

        // Native scroll content is intrinsic; paint the full viewport separately.
        return new Grid { scroll }.Background(CoffeeTheme.SurfaceColor);
    }
}

public static class ManagementListRow
{
    public static View Build(string label, string name, string automationId, Action onTap) =>
        new Grid(
            columns: new object[] { "*", "Auto" },
            rows: new object[] { "*", "Auto", "Auto", "*" },
            columnSpacing: CoffeeSpacing.S)
        {
            new Text(label.ToUpperInvariant()).SectionLabel()
                .MaxLines(1).LineBreakMode(LineBreakMode.TailTruncation)
                .Cell(row: 1),
            new Text(name).FontFamily("ManropeSemibold").FontSize(20)
                .Color(CoffeeTheme.TextPrimary)
                .MaxLines(1).LineBreakMode(LineBreakMode.TailTruncation)
                .Cell(row: 2),
            Chevron().Cell(row: 0, column: 1, rowSpan: 4),
        }
        .Padding(new Thickness(CoffeeSpacing.M))
        .MinimumHeight(80)
        .Background(CoffeeTheme.SurfaceColor)
        .Margin(bottom: CoffeeSpacing.Divider)
        .AutomationId(automationId)
        .OnTap(_ => onTap());

    internal static View Chevron() => new Text(CoffeeIcons.Chevron)
        .FontFamily(CoffeeIcons.FontFamily)
        .FontSize(24)
        .Color(CoffeeTheme.TextPrimary)
        .Center();
}

public static class ProfileListRow
{
    public static View Build(string name, View avatar, string automationId, Action onTap) =>
        new Grid(
            columns: new object[] { "Auto", "*", "Auto" },
            rows: new object[] { "Auto", "Auto" },
            columnSpacing: 12)
        {
            new Text("MEMBER").SectionLabel().Cell(row: 0, colSpan: 2),
            avatar.Center().Cell(row: 1),
            new Text(name).FontFamily("ManropeSemibold").FontSize(20)
                .Color(CoffeeTheme.TextPrimary)
                .MaxLines(1).LineBreakMode(LineBreakMode.TailTruncation)
                .Leading().Cell(row: 1, column: 1),
            ManagementListRow.Chevron().Cell(row: 0, column: 2, rowSpan: 2),
        }
        .Padding(new Thickness(CoffeeSpacing.M))
        .MinimumHeight(80)
        .Background(CoffeeTheme.SurfaceColor)
        .Margin(bottom: CoffeeSpacing.Divider)
        .AutomationId(automationId)
        .OnTap(_ => onTap());
}

public sealed class AdaptiveTwoLineTile : View
{
    readonly string _label;
    readonly Func<string> _value;
    readonly Func<string?> _unit;
    readonly Action? _onTap;
    readonly bool _inverted;
    readonly float _minimumHeight;
    readonly string _automationId;
    readonly View? _leading;
    readonly View? _trailing;
    readonly Action? _onLongPress;
    readonly bool _singleLineTailTruncation;
    readonly bool _singleLineValue;
    readonly double _valueCharacterSpacing;
    readonly double? _valueFontSize;

    public AdaptiveTwoLineTile(
        string label,
        Func<string> value,
        string automationId,
        Action? onTap = null,
        Func<string?>? unit = null,
        bool inverted = false,
        float minimumHeight = 120,
        View? leading = null,
        View? trailing = null,
        Action? onLongPress = null,
        bool singleLineTailTruncation = false,
        bool singleLineValue = false,
        double valueCharacterSpacing = 0,
        double? valueFontSize = null)
    {
        _label = label;
        _value = value;
        _automationId = automationId;
        _onTap = onTap;
        _unit = unit ?? (() => null);
        _inverted = inverted;
        _minimumHeight = minimumHeight;
        _leading = leading;
        _trailing = trailing;
        _onLongPress = onLongPress;
        _singleLineTailTruncation = singleLineTailTruncation;
        _singleLineValue = singleLineValue;
        _valueCharacterSpacing = valueCharacterSpacing;
        _valueFontSize = valueFontSize;
    }

    [Body]
    View body() => Build(
        _label,
        _value(),
        _automationId,
        _onTap,
        _unit(),
        _inverted,
        _minimumHeight,
        _leading,
        _trailing,
        _onLongPress,
        _singleLineTailTruncation,
        _singleLineValue,
        _valueCharacterSpacing,
        _valueFontSize);

    public static View Build(
        string label,
        string value,
        string automationId,
        Action? onTap = null,
        string? unit = null,
        bool inverted = false,
        float minimumHeight = 120,
        View? leading = null,
        View? trailing = null,
        Action? onLongPress = null,
        bool singleLineTailTruncation = false,
        bool singleLineValue = false,
        double valueCharacterSpacing = 0,
        double? valueFontSize = null)
    {
        var background = inverted ? CoffeeTheme.TextPrimary : CoffeeTheme.SurfaceColor;
        var foreground = inverted ? CoffeeTheme.SurfaceColor : CoffeeTheme.TextPrimary;
        var labelColor = inverted
            ? CoffeeTheme.SurfaceColor.WithAlpha(.72f)
            : CoffeeTheme.TextSecondary;
        var valueSize = valueFontSize ?? ValueFontSize(value, unit is not null);
        var columns = leading is not null && trailing is not null
            ? new object[] { "Auto", "*", "Auto" }
            : leading is not null
                ? new object[] { "Auto", "*" }
                : trailing is not null
                    ? new object[] { "*", "Auto" }
                    : new object[] { "*" };
        var contentColumn = leading is null ? 0 : 1;

        var content = new Grid(columns: columns, rows: new object[] { "Auto", "*" }, columnSpacing: CoffeeSpacing.S)
        {
            new Text(label.ToUpperInvariant())
                .SectionLabel()
                .Color(labelColor)
                .Cell(row: 0, column: contentColumn),
            ValueLine(
                value,
                unit,
                valueSize,
                foreground,
                singleLineTailTruncation,
                singleLineValue,
                valueCharacterSpacing)
                .Cell(row: 1, column: contentColumn),
        };

        if (leading is not null)
            content.Add(leading.Cell(row: 0, column: 0, rowSpan: 2).Center());
        if (trailing is not null)
            content.Add(trailing.Cell(row: 0, column: contentColumn + 1, rowSpan: 2).Center());

        View tile = content
            .Padding(new Thickness(CoffeeSpacing.M, BaristaSafeAreaLayout.HeaderVerticalPadding))
            .Background(background)
            .MinimumHeight(minimumHeight)
            .AutomationId(automationId);

        if (onTap is not null)
            tile = tile.OnTap(_ => onTap());
        if (onLongPress is not null)
            tile = tile.OnLongPress(_ => onLongPress());

        return tile;
    }

    static View ValueLine(
        string value,
        string? unit,
        double valueSize,
        Color color,
        bool singleLineTailTruncation,
        bool singleLineValue,
        double valueCharacterSpacing)
    {
        var valueText = new Text(value)
            .FontFamily("ManropeSemibold")
            .FontSize(valueSize)
            .Color(color)
            .CharacterSpacing(valueCharacterSpacing)
            .MaxLines(singleLineTailTruncation || singleLineValue ? 1 : 2)
            .Alignment(Comet.Alignment.BottomLeading)
            .Cell(row: 0, column: 0);
        if (singleLineTailTruncation)
            valueText.LineBreakMode(LineBreakMode.TailTruncation);
        else if (singleLineValue)
            valueText.LineBreakMode(LineBreakMode.NoWrap);

        var grid = new Grid(
            columns: unit is null ? new object[] { "*" } : new object[] { "Auto", "Auto" },
            rows: new object[] { "*" },
            columnSpacing: CoffeeSpacing.XS)
        {
            valueText,
        };
        if (unit is not null)
        {
            grid.Add(new Text(unit)
                .FontFamily("Manrope")
                .FontSize(Math.Max(12, valueSize * .45))
                .Color(color.WithAlpha(.6f))
                .Margin(bottom: valueSize >= 32 ? 8 : 4)
                .Alignment(Comet.Alignment.BottomLeading)
                .Cell(row: 0, column: 1));
        }
        return grid;
    }

    internal static double ValueFontSize(string value, bool hasUnit) => (value.Length, hasUnit) switch
    {
        ( <= 3, _) => 44,
        ( <= 6, true) => 36,
        ( <= 6, false) => 38,
        ( <= 10, _) => 28,
        ( <= 14, _) => 22,
        ( <= 20, _) => 18,
        _ => 16,
    };
}

public sealed class SectionListRow : View
{
    readonly string _label;
    readonly Func<string> _value;
    readonly string _automationId;
    readonly Action? _onTap;
    readonly string? _status;

    public SectionListRow(
        string label,
        Func<string> value,
        string automationId,
        Action? onTap = null,
        string? status = null)
    {
        _label = label;
        _value = value;
        _automationId = automationId;
        _onTap = onTap;
        _status = status;
    }

    [Body]
    View body() => Build(_label, _value(), _automationId, _onTap, _status);

    public static View Build(
        string label,
        string value,
        string automationId,
        Action? onTap = null,
        string? status = null)
    {
        var row = new Grid(
            columns: new object[] { "*", "Auto" },
            rows: new object[] { "Auto", "Auto" })
        {
            new Text(label.ToUpperInvariant()).SectionLabel().Cell(row: 0, column: 0),
            new Text(value).FontFamily("ManropeSemibold").FontSize(18)
                .Color(CoffeeTheme.TextPrimary).MaxLines(1)
                .Alignment(Comet.Alignment.BottomLeading)
                .Cell(row: 1, column: 0),
            new Text(status ?? CoffeeIcons.Chevron)
                .FontFamily(status is null ? CoffeeIcons.FontFamily : "Manrope")
                .FontSize(status is null ? 24 : 10)
                .Color(CoffeeTheme.TextSecondary)
                .Center()
                .Cell(row: 0, column: 1, rowSpan: 2),
        };
        View result = row.Padding(new Thickness(CoffeeSpacing.M, 14))
            .Background(CoffeeTheme.SurfaceColor)
            .Frame(height: 80)
            .AutomationId(automationId);
        return onTap is null ? result : result.OnTap(_ => onTap());
    }
}

public sealed class FixedBottomActionRow : View
{
    readonly IReadOnlyList<BottomAction> _actions;

    public FixedBottomActionRow(params BottomAction[] actions) => _actions = actions;

    [Body]
    View body() => Build(this, _actions);

    public static View Build(View insetOwner, IReadOnlyList<BottomAction> actions)
    {
        var columns = new object[actions.Count];
        Array.Fill(columns, "*");
        var row = new Grid(
            columns: columns,
            rows: new object[] { "Auto" },
            columnSpacing: CoffeeSpacing.Divider)
            .Background(CoffeeTheme.OutlineColor)
            .AutomationId("bottom_action_row");
        var padding = BaristaSafeAreaLayout.ActionPadding(insetOwner, CoffeeSpacing.M);

        for (var i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            var background = action.Inverted
                ? CoffeeTheme.TextPrimary
                : CoffeeTheme.SurfaceColor;
            var foreground = action.Inverted
                ? CoffeeTheme.SurfaceColor
                : CoffeeTheme.TextPrimary;
            var tile = new Grid(rows: new object[] { "Auto", "Auto" })
            {
                new Image(CoffeeIcons.Source(action.Icon, foreground, 32))
                    .Center()
                    .Cell(row: 0),
            };
            if (!string.IsNullOrWhiteSpace(action.Label))
                tile.Add(new Text(action.Label!.ToUpperInvariant())
                    .SectionLabel()
                    .Color(foreground)
                    .Center()
                    .Cell(row: 1));

            row.Add(tile
                .Padding(padding)
                .MinimumHeight(CoffeeSpacing.ActionRowHeight)
                .Background(background)
                .AutomationId(action.AutomationId)
                .OnTap(_ => action.Invoke())
                .Cell(row: 0, column: i));
        }

        return row;
    }
}

public static class BaristaEdgeFrame
{
    public const int NewDrinkDataRowCount = 6;
    public const int NewDrinkContentRowCount = NewDrinkDataRowCount + 1;

    public static object[] CreateRows() => new object[] { "Auto", "*", "Auto" };

    public static Grid BuildTopInset(bool showDivider)
    {
        var strip = new Grid(
            columns: new object[] { "*", CoffeeSpacing.Divider, "*" },
            rows: new object[] { "*" })
            .Background(CoffeeTheme.SurfaceColor);
        if (showDivider)
        {
            strip.Add(new Grid()
                .Background(CoffeeTheme.OutlineColor)
                .AutomationId("new_drink_top_divider")
                .Cell(column: 1));
        }
        return strip;
    }

    public static object[] CreateEqualDataRows(int count)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        var rows = new object[count];
        Array.Fill(rows, "*");
        return rows;
    }

    public static View Build(
        View header,
        View body,
        View footer,
        string automationId) =>
        new Grid(
            columns: new object[] { "*" },
            rows: CreateRows(),
            rowSpacing: CoffeeSpacing.Divider)
        {
            header.Cell(row: 0),
            body.Cell(row: 1),
            footer.Cell(row: 2),
        }
        .Background(CoffeeTheme.OutlineColor)
        .IgnoreSafeArea()
        .AutomationId(automationId);
}

public sealed class FormInputRow : View
{
    readonly string _label;
    readonly View _input;
    readonly string _automationId;
    readonly string? _unit;

    public FormInputRow(string label, View input, string automationId, string? unit = null)
    {
        _label = label;
        _input = input;
        _automationId = automationId;
        _unit = unit;
    }

    [Body]
    View body() => Build(_label, _input, _automationId, _unit);

    public static View Build(string label, View input, string automationId, string? unit = null)
    {
        var row = new Grid(
            columns: unit is null ? new object[] { "*" } : new object[] { "*", "Auto" },
            rows: new object[] { "Auto", 52 })
        {
            new Text(label.ToUpperInvariant()).SectionLabel().Cell(row: 0),
            input.SourceText().Cell(row: 1),
        };
        if (unit is not null)
            row.Add(new Text(unit).SecondaryText().Center().Cell(row: 1, column: 1));
        return row.Padding(new Thickness(CoffeeSpacing.M, CoffeeSpacing.S))
            .Background(CoffeeTheme.SurfaceColor)
            .AutomationId(automationId);
    }
}

public sealed class RatingDisplayView : View
{
    readonly Func<int?> _rating;
    readonly string _automationId;

    public RatingDisplayView(Func<int?> rating, string automationId)
    {
        _rating = rating;
        _automationId = automationId;
    }

    [Body]
    View body()
    {
        var rating = _rating();
        var row = new Grid(columns: new object[] { "Auto", "*" }, rows: new object[] { "*" })
        {
            new Text(rating.HasValue ? CoffeeIcons.RatingIcon(rating.Value) : CoffeeIcons.SentimentNeutral)
                .FontFamily(CoffeeIcons.FontFamily).FontSize(28)
                .Color(rating.HasValue ? CoffeeTheme.PrimaryColor : CoffeeTheme.TextMuted)
                .Cell(column: 0),
            new Text(rating.HasValue ? RatingName(rating.Value) : "Not rated")
                .FontFamily("ManropeSemibold").FontSize(16).Color(CoffeeTheme.TextPrimary)
                .Center().Cell(column: 1),
        };
        return row.AutomationId(_automationId);
    }

    public static string RatingName(int rating) => rating switch
    {
        0 => "Terrible",
        1 => "Bad",
        2 => "Average",
        3 => "Good",
        _ => "Excellent",
    };
}

public sealed class RatingInputView : View
{
    readonly Signal<int> _rating;
    readonly Action<int>? _onSelected;

    public RatingInputView(Signal<int> rating, Action<int>? onSelected = null)
    {
        _rating = rating;
        _onSelected = onSelected;
    }

    [Body]
    View body()
    {
        var row = new Grid(columns: new object[] { "*", "*", "*", "*", "*" }, rows: new object[] { 72 });
        for (var i = 0; i < 5; i++)
        {
            var rating = i;
            row.Add(new Grid(rows: new object[] { "*", "Auto" })
            {
                new Text(CoffeeIcons.RatingIcon(rating))
                    .FontFamily(CoffeeIcons.FontFamily).FontSize(30)
                    .Color(_rating.Value == rating ? CoffeeTheme.PrimaryColor : CoffeeTheme.TextMuted)
                    .Center().Cell(row: 0),
                new Text(RatingDisplayView.RatingName(rating)).FontFamily("Manrope").FontSize(10)
                    .Color(CoffeeTheme.TextSecondary).Center().Cell(row: 1),
            }
            .AutomationId($"rating_{rating}")
            .OnTap(_ =>
            {
                _rating.Value = rating;
                _onSelected?.Invoke(rating);
            })
            .Cell(column: i));
        }
        return row;
    }
}

public enum ContentStateKind
{
    Loading,
    Empty,
    Error,
}

public sealed class ContentStateView : View
{
    readonly ContentStateKind _kind;
    readonly string _title;
    readonly string? _message;
    readonly Action? _retry;

    public ContentStateView(ContentStateKind kind, string title, string? message = null, Action? retry = null)
    {
        _kind = kind;
        _title = title;
        _message = message;
        _retry = retry;
    }

    [Body]
    View body()
    {
        var icon = _kind switch
        {
            ContentStateKind.Loading => "…",
            ContentStateKind.Error => "!",
            _ => CoffeeIcons.Coffee,
        };
        var stack = new VStack(spacing: CoffeeSpacing.S)
        {
            new Text(icon)
                .FontFamily(_kind == ContentStateKind.Empty ? CoffeeIcons.FontFamily : "ManropeSemibold")
                .FontSize(48).Color(_kind == ContentStateKind.Error ? CoffeeTheme.Error : CoffeeTheme.TextMuted),
            new Text(_title).FontFamily("ManropeSemibold").FontSize(18).Color(CoffeeTheme.TextPrimary),
        };
        if (!string.IsNullOrWhiteSpace(_message))
            stack.Add(new Text(_message!).SecondaryText());
        if (_retry is not null)
            stack.Add(new Button("TRY AGAIN", _retry).TextButton().Color(CoffeeTheme.PrimaryColor).CornerRadius(0));
        return stack.Center().AutomationId($"state_{_kind.ToString().ToLowerInvariant()}");
    }
}

public sealed class ActionModalOverlay : View
{
    readonly Signal<bool> _isOpen;
    readonly string _title;
    readonly Func<View> _content;
    readonly Action _dismiss;
    readonly string _automationId;
    readonly bool _useFixedDarkPalette;

    public ActionModalOverlay(
        Signal<bool> isOpen,
        string title,
        Func<View> content,
        Action dismiss,
        string automationId,
        bool useFixedDarkPalette = false)
    {
        _isOpen = isOpen;
        _title = title;
        _content = content;
        _dismiss = dismiss;
        _automationId = automationId;
        _useFixedDarkPalette = useFixedDarkPalette;
    }

    [Body]
    View body()
    {
        if (!_isOpen.Value)
            return new Grid().Frame(width: 0, height: 0);

        var surface = _useFixedDarkPalette ? CoffeeTheme.Dark.Surface : CoffeeTheme.SurfaceColor;
        var text = _useFixedDarkPalette ? CoffeeTheme.Dark.TextPrimary : CoffeeTheme.TextPrimary;

        var panel = new Grid(rows: new object[] { 64, "Auto" })
        {
            new Grid(columns: new object[] { "*", 56 })
            {
                new Text(_title.ToUpperInvariant()).SectionLabel()
                    .Color(text).Center().Cell(column: 0),
                new Text(CoffeeIcons.Close).FontFamily(CoffeeIcons.FontFamily).FontSize(24)
                    .Color(text).Center()
                    .AutomationId($"{_automationId}_close")
                    .OnTap(_ => _dismiss()).Cell(column: 1),
            }.Cell(row: 0),
            _content().Cell(row: 1),
        }
        .Background(surface)
        .Bottom()
        .FillHorizontal();

        return new Grid
        {
            new Grid().Background(CoffeeTheme.Dark.Background.WithAlpha(.72f))
                .OnTap(_ => _dismiss()),
            panel,
        }
        .AutomationId(_automationId);
    }
}

public sealed class PhotoWorkflowFeedbackView : View
{
    readonly Signal<bool> _isBusy;
    readonly Signal<string?> _status;
    readonly Signal<string?> _error;

    public PhotoWorkflowFeedbackView(
        Signal<bool> isBusy,
        Signal<string?> status,
        Signal<string?> error)
    {
        _isBusy = isBusy;
        _status = status;
        _error = error;
    }

    [Body]
    View body()
    {
        string? message;
        string automationId;
        Color background;
        if (_error.Value is { } error)
        {
            message = error;
            automationId = "photo_workflow_error";
            background = CoffeeTheme.Error;
        }
        else if (_status.Value is { } status)
        {
            message = status;
            automationId = "photo_workflow_status";
            background = CoffeeTheme.TextPrimary;
        }
        else if (_isBusy.Value)
        {
            message = "Reviewing photo…";
            automationId = "photo_workflow_busy";
            background = CoffeeTheme.TextPrimary;
        }
        else
        {
            return new Grid().Frame(width: 0, height: 0);
        }

        return new Grid
        {
            new Text(message)
                .FontFamily("ManropeSemibold")
                .FontSize(14)
                .Color(CoffeeTheme.SurfaceColor)
                .Center(),
        }
        .Padding(new Thickness(CoffeeSpacing.M, CoffeeSpacing.S))
        .Margin(new Thickness(CoffeeSpacing.M, CoffeeSpacing.HeaderHeight + CoffeeSpacing.S, CoffeeSpacing.M, 0))
        .Background(background)
        .Top()
        .AutomationId(automationId);
    }
}

public sealed class VoiceOverlayView : View
{
    readonly Signal<bool> _visible;
    readonly Signal<bool> _collapsed;
    readonly Signal<string> _stateText = new("Ready");
    readonly Signal<string> _transcript = new(string.Empty);
    readonly Signal<string> _response = new(string.Empty);
    readonly Signal<string?> _errorMessage = new(null);
    readonly Signal<bool> _isListening = new(false);
    readonly Signal<bool> _isProcessing = new(false);

    public VoiceOverlayView(Signal<bool> visible, Signal<bool> collapsed)
    {
        _visible = visible;
        _collapsed = collapsed;
    }

    /// <summary>
    /// Update overlay from the live <see cref="CometBaristaNotes.Services.Voice.VoiceOverlayState"/>.
    /// Called by the voice callbacks on the main thread.
    /// </summary>
    public void ApplyState(CometBaristaNotes.Services.Voice.VoiceOverlayState state)
    {
        _stateText.Value = state.StateText;
        _transcript.Value = state.Transcript;
        _response.Value = state.Response ?? string.Empty;
        _errorMessage.Value = state.ErrorMessage;
        _isListening.Value = state.IsListening;
        _isProcessing.Value = state.IsProcessing;
    }

    [Body]
    View body()
    {
        var isVisible = _visible.Value;
        if (!isVisible)
            return new Grid().Frame(width: 0, height: 0);

        var isCollapsed = _collapsed.Value;
        var collapsedSurface = new Grid
        {
            new Text(CoffeeIcons.Mic)
                .FontFamily(CoffeeIcons.FontFamily).FontSize(28)
                .Color(CoffeeTheme.OnPrimaryColor).Center(),
        }
        .Frame(width: 56, height: 56)
        .Background(CoffeeTheme.PrimaryColor)
        .AutomationId("voice_expand")
        .OnTap(_ => _collapsed.Value = false)
        .Margin(right: CoffeeSpacing.M, bottom: CoffeeSpacing.ActionRowHeight + CoffeeSpacing.M)
        .Alignment(Comet.Alignment.BottomTrailing)
        .Opacity(isVisible && isCollapsed ? 1 : 0)
        .IsEnabled(isVisible && isCollapsed);

        var statusColor = _isListening.Value || _isProcessing.Value
            ? BaristaSourceVisualContract.VoiceActive
            : BaristaSourceVisualContract.VoicePrimaryText;

        var contentStack = new VStack(spacing: CoffeeSpacing.S)
        {
            new Text(() => _stateText.Value)
                .FontFamily("ManropeSemibold").FontSize(22).Color(statusColor),
        };

        if (!string.IsNullOrWhiteSpace(_transcript.Value))
        {
            contentStack.Add(new Text("TRANSCRIPT").SectionLabel()
                .Color(BaristaSourceVisualContract.VoiceSecondaryText));
            contentStack.Add(new Text(() => _transcript.Value)
                .FontFamily("Manrope").FontSize(14)
                .Color(BaristaSourceVisualContract.VoiceSecondaryText));
        }

        if (!string.IsNullOrWhiteSpace(_response.Value))
        {
            contentStack.Add(new Text("RESPONSE").SectionLabel()
                .Color(BaristaSourceVisualContract.VoiceSecondaryText));
            contentStack.Add(new Text(() => _response.Value)
                .FontFamily("Manrope").FontSize(14)
                .Color(BaristaSourceVisualContract.VoiceResponse));
        }

        if (_errorMessage.Value is { } error)
        {
            contentStack.Add(new Text(error)
                .FontFamily("Manrope").FontSize(13).Color(CoffeeTheme.Error));
        }

        if (!_isListening.Value && !_isProcessing.Value)
        {
            contentStack.Add(new Text("Hold the microphone to speak.")
                .FontFamily("Manrope").FontSize(13)
                .Color(BaristaSourceVisualContract.VoiceMicIdleOutline));
        }

        var micActive = _isListening.Value || _isProcessing.Value;
        var micColor = micActive
            ? BaristaSourceVisualContract.VoiceActive
            : BaristaSourceVisualContract.VoiceMicIdle;

        var panel = new Grid(rows: new object[]
        {
            56,
            "*",
            96 + CoffeeSpacing.VoiceControlBottomClearance,
        })
        {
            new Grid(columns: new object[] { 56, "*", 56 })
            {
                new Text(CoffeeIcons.Collapse).FontFamily(CoffeeIcons.FontFamily).FontSize(24)
                    .Color(BaristaSourceVisualContract.VoiceSecondaryText).Center()
                    .AutomationId("voice_collapse")
                    .OnTap(_ => _collapsed.Value = true).Cell(column: 0),
                new Text("VOICE").SectionLabel()
                    .Color(BaristaSourceVisualContract.VoicePrimaryText).Center().Cell(column: 1),
                new Text(CoffeeIcons.Close).FontFamily(CoffeeIcons.FontFamily).FontSize(24)
                    .Color(BaristaSourceVisualContract.VoicePrimaryText).Center()
                    .AutomationId("voice_close")
                    .OnTap(sender =>
                    {
                        _visible.Value = false;
                        _ = CometBaristaNotes.Services.Voice.BaristaVoiceIntegration.OnPageDeactivatedAsync();
                    }).Cell(column: 2),
            }.Cell(row: 0),
            new ScrollView { contentStack.Padding(new Thickness(CoffeeSpacing.L)) }.Cell(row: 1),
            new Grid(
                rows: new object[]
                {
                    CoffeeSpacing.M,
                    80,
                    CoffeeSpacing.VoiceControlBottomClearance,
                })
            {
                new Grid
                {
                    new Text(CoffeeIcons.Mic).FontFamily(CoffeeIcons.FontFamily).FontSize(42)
                        .Color(BaristaSourceVisualContract.VoicePrimaryText).Center(),
                }
                .Background(micColor)
                .Border(
                    micActive ? 0 : 2.5f,
                    BaristaSourceVisualContract.VoiceMicIdleOutline)
                .ClipShape(new Ellipse())
                .Frame(
                    width: BaristaSourceVisualContract.VoiceMicSize,
                    height: BaristaSourceVisualContract.VoiceMicSize)
                .Center()
                .AutomationId("voice_push_to_talk")
                .OnRecord(gesture =>
                {
                    var session = CometBaristaNotes.Services.Voice.BaristaVoiceIntegration.Session;
                    if (session is null)
                        return;
                    var phase = gesture.Status switch
                    {
                        Comet.GestureStatus.Started => CometBaristaNotes.Services.Voice.PushToTalkPhase.Started,
                        Comet.GestureStatus.Completed => CometBaristaNotes.Services.Voice.PushToTalkPhase.Completed,
                        Comet.GestureStatus.Canceled => CometBaristaNotes.Services.Voice.PushToTalkPhase.Cancelled,
                        _ => (CometBaristaNotes.Services.Voice.PushToTalkPhase?)null,
                    };
                    if (phase.HasValue)
                        _ = session.HandlePushToTalkAsync(phase.Value);
                })
                .Cell(row: 1),
            }
            .Cell(row: 2),
        }
        .Frame(height: BaristaSourceVisualContract.VoicePanelHeight + CoffeeSpacing.VoiceOverlayBottomSafeArea)
        .Background(BaristaSourceVisualContract.VoicePanel)
        .Bottom()
        .FillHorizontal();

        var expandedSurface = new Grid
        {
            new Grid().Background(CoffeeTheme.Dark.Background.WithAlpha(.72f))
                .OnTap(_ => _collapsed.Value = true),
            panel,
        }
        .FillHorizontal()
        .FillVertical()
        .Opacity(isVisible && !isCollapsed ? 1 : 0)
        .IsEnabled(isVisible && !isCollapsed);

        return new Grid
        {
            expandedSurface,
            collapsedSurface,
        }
        .FillHorizontal()
        .FillVertical()
        .Opacity(isVisible ? 1 : 0)
        .IsEnabled(isVisible)
        .AutomationId(isCollapsed
            ? "voice_overlay_collapsed"
            : "voice_overlay_expanded");
    }
}
