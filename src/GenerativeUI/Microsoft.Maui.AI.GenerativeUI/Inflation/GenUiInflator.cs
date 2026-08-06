using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.AI.GenerativeUI.Binding;
using Microsoft.Maui.AI.GenerativeUI.Dsl;
using Microsoft.Maui.AI.GenerativeUI.Registry;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;
using DslBinding = Microsoft.Maui.AI.GenerativeUI.Binding.UiBindingCompiler;

namespace Microsoft.Maui.AI.GenerativeUI.Inflation;

/// <summary>
/// Turns a parsed <see cref="UiDocument"/> into a data-bound MAUI view tree. Deterministic and
/// forgiving: unknown types and invalid props degrade to visible placeholders and are logged; the
/// inflator never throws into the UI. See <c>docs/GenerativeUI/spec/appendix-ui-dsl.md §8</c>.
/// </summary>
public sealed class GenUiInflator(GenerativeUiRegistry registry, IServiceProvider services)
{
    private const int MaxNodes = 300;
    private const int MaxDepth = 24;

    private readonly StyleApplier _styles = new(registry);

    /// <summary>
    /// Inflates a document. Display <c>bind</c> paths resolve against <paramref name="dataRoot"/>
    /// (one-way); editable <c>key</c> paths against <paramref name="formRoot"/> (two-way).
    /// </summary>
    public View Inflate(UiDocument document, UiObject dataRoot, UiObject formRoot)
    {
        if (document.Ui is null)
            return Placeholder("Empty document (no 'ui' node).");

        var ctx = new Context(dataRoot, formRoot);
        return InflateNode(document.Ui, ctx);
    }

    private sealed class Context(UiObject data, UiObject form)
    {
        public UiObject Data { get; } = data;
        public UiObject Form { get; } = form;
        public int Depth;
        public int Count;
    }

    private View InflateNode(UiNode node, Context ctx)
    {
        if (++ctx.Count > MaxNodes)
            return Placeholder("UI truncated (too many nodes).");
        if (ctx.Depth > MaxDepth)
            return Placeholder("UI truncated (nested too deep).");

        ctx.Depth++;
        try
        {
            var view = Build(node, ctx);
            if (node.Style.Count > 0 && view is VisualElement ve)
                _styles.Apply(ve, node.Type, node.Style);
            return view;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GenerativeUI] Failed to inflate '{node.Type}': {ex.Message}");
            return Placeholder($"Failed to render '{node.Type}'.");
        }
        finally
        {
            ctx.Depth--;
        }
    }

    private View Build(UiNode node, Context ctx) => node.Type switch
    {
        "Stack" => BuildStack(node, ctx),
        "Card" => BuildCard(node, ctx),
        "Scroll" => BuildScroll(node, ctx),
        "Separator" => BuildSeparator(node),
        "Spacer" => BuildSpacer(node),
        "Label" => BuildLabel(node, ctx),
        "Image" => BuildImage(node, ctx),
        "Badge" => BuildBadge(node, ctx),
        "Icon" => BuildIcon(node),
        "Button" => BuildButton(node),
        "Field" => BuildField(node, ctx),
        "Entry" => BuildEntry(node, ctx),
        "List" => BuildList(node, ctx),
        "Screen" => BuildScreen(node),
        _ => BuildRegisteredOrUnknown(node, ctx),
    };

    // ── Layout ──────────────────────────────────────────────────────────────────────────────────

    private View BuildStack(UiNode node, Context ctx)
    {
        var horizontal = string.Equals(node.GetString("orientation"), "horizontal", StringComparison.OrdinalIgnoreCase);
        Layout layout = horizontal ? new HorizontalStackLayout() : new VerticalStackLayout();
        if (node.GetNumber("spacing") is { } spacing)
            ((StackBase)layout).Spacing = spacing;
        if (node.GetNumber("padding") is { } padding)
            layout.Padding = padding;
        foreach (var child in node.Children)
            layout.Add(InflateNode(child, ctx));
        return layout;
    }

    private View BuildCard(UiNode node, Context ctx)
    {
        var content = new VerticalStackLayout { Spacing = 6 };
        foreach (var child in node.Children)
            content.Add(InflateNode(child, ctx));

        return new Border
        {
            Padding = node.GetNumber("padding") ?? 12,
            StrokeThickness = 1,
            Stroke = new SolidColorBrush(Color.FromArgb("#E0E0E0")),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Content = content,
        };
    }

    private View BuildScroll(UiNode node, Context ctx)
    {
        var child = node.Children.Count > 0 ? InflateNode(node.Children[0], ctx) : new ContentView();
        return new ScrollView { Content = child };
    }

    private static View BuildSeparator(UiNode node)
    {
        var horizontal = !string.Equals(node.GetString("orientation"), "vertical", StringComparison.OrdinalIgnoreCase);
        return new BoxView
        {
            Color = Color.FromArgb("#E0E0E0"),
            HeightRequest = horizontal ? 1 : -1,
            WidthRequest = horizontal ? -1 : 1,
            HorizontalOptions = horizontal ? LayoutOptions.Fill : LayoutOptions.Center,
        };
    }

    private static View BuildSpacer(UiNode node)
    {
        var size = node.GetNumber("size") ?? 8;
        return new BoxView { Color = Colors.Transparent, HeightRequest = size, WidthRequest = size };
    }

    // ── Content ─────────────────────────────────────────────────────────────────────────────────

    private static View BuildLabel(UiNode node, Context ctx)
    {
        var label = new Label { LineBreakMode = node.GetBool("wrap") == false ? LineBreakMode.TailTruncation : LineBreakMode.WordWrap };
        if (node.Bind is { } path)
            label.SetBinding(Label.TextProperty, DslBinding.Compile(path, source: ctx.Data));
        else
            label.Text = node.GetString("text") ?? "";
        return label;
    }

    private static View BuildImage(UiNode node, Context ctx)
    {
        // Emoji-as-image (no URL): render a large glyph label.
        if (node.GetString("emoji") is { Length: > 0 } emoji)
            return new Label { Text = emoji, FontSize = node.GetNumber("size") ?? 32 };

        var image = new Image { Aspect = Aspect.AspectFit };
        if (node.GetNumber("size") is { } size)
        {
            image.WidthRequest = size;
            image.HeightRequest = size;
        }
        if (node.Bind is { } path)
            image.SetBinding(Image.SourceProperty, DslBinding.Compile(path, source: ctx.Data));
        else if (node.GetString("source") is { Length: > 0 } src)
            image.Source = src;
        return image;
    }

    private static View BuildBadge(UiNode node, Context ctx)
    {
        var (bg, fg) = (node.GetString("tone") ?? "neutral") switch
        {
            "positive" => (Color.FromArgb("#DFF6DD"), Color.FromArgb("#0E700E")),
            "warning" => (Color.FromArgb("#FFF4CE"), Color.FromArgb("#7A5B00")),
            "danger" => (Color.FromArgb("#FDE7E9"), Color.FromArgb("#A4262C")),
            _ => (Color.FromArgb("#EDEBE9"), Color.FromArgb("#323130")),
        };

        var label = new Label { FontSize = 12, TextColor = fg, VerticalOptions = LayoutOptions.Center };
        if (node.Bind is { } path)
            label.SetBinding(Label.TextProperty, DslBinding.Compile(path, source: ctx.Data));
        else
            label.Text = node.GetString("text") ?? "";

        return new Border
        {
            Padding = new Thickness(8, 3),
            BackgroundColor = bg,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            HorizontalOptions = LayoutOptions.Start,
            Content = label,
        };
    }

    private static View BuildIcon(UiNode node)
        => new Label { Text = node.GetString("glyph") ?? "", FontSize = node.GetNumber("size") ?? 20 };

    // ── Interactive ─────────────────────────────────────────────────────────────────────────────

    private View BuildButton(UiNode node)
    {
        var button = new Button { Text = node.GetString("text") ?? "" };
        var intentName = node.GetString("intent");
        var payload = node.GetString("payload");
        if (!string.IsNullOrEmpty(intentName))
        {
            button.Clicked += (_, _) =>
            {
                var bridge = services.GetService(typeof(IChatBridge)) as IChatBridge;
                _ = bridge?.RaiseIntentAsync(new UiIntent(intentName!, payload));
            };
        }
        return button;
    }

    private static View BuildField(UiNode node, Context ctx)
    {
        var key = node.GetString("key");
        if (string.IsNullOrEmpty(key))
            return Placeholder("Field is missing 'key'.");

        var stack = new VerticalStackLayout { Spacing = 2 };
        if (node.GetString("label") is { Length: > 0 } labelText)
            stack.Add(new Label { Text = labelText, FontSize = 12, TextColor = Colors.Gray });

        View input = MakeInput(node.GetString("kind"), key!, node.GetString("placeholder"), ctx.Form);
        stack.Add(input);
        return stack;
    }

    private static View BuildEntry(UiNode node, Context ctx)
    {
        var key = node.GetString("key");
        if (string.IsNullOrEmpty(key))
            return Placeholder("Entry is missing 'key'.");
        return MakeInput(node.GetString("kind"), key!, node.GetString("placeholder"), ctx.Form);
    }

    private static View MakeInput(string? kind, string key, string? placeholder, UiObject formRoot)
    {
        var binding = DslBinding.Compile(key, BindingMode.TwoWay, source: formRoot);
        switch (kind)
        {
            case "multiline":
                var editor = new Editor { Placeholder = placeholder, AutoSize = EditorAutoSizeOption.TextChanges };
                editor.SetBinding(Editor.TextProperty, binding);
                return editor;

            case "bool":
                var sw = new Microsoft.Maui.Controls.Switch();
                sw.SetBinding(Microsoft.Maui.Controls.Switch.IsToggledProperty, DslBinding.Compile(key, BindingMode.TwoWay, BoolConverter.Instance, formRoot));
                return sw;

            case "number":
                var numeric = new Entry { Placeholder = placeholder, Keyboard = Keyboard.Numeric };
                numeric.SetBinding(Entry.TextProperty, binding);
                return numeric;

            default:
                var entry = new Entry { Placeholder = placeholder };
                entry.SetBinding(Entry.TextProperty, binding);
                return entry;
        }
    }

    // ── Collections ─────────────────────────────────────────────────────────────────────────────

    private View BuildList(UiNode node, Context ctx)
    {
        var stack = new VerticalStackLayout { Spacing = 8 };
        foreach (var row in node.Children)
            stack.Add(InflateNode(row, ctx));
        return stack;
    }

    // ── Registered controls & screens ───────────────────────────────────────────────────────────

    private View BuildScreen(UiNode node)
    {
        var name = node.GetString("screen");
        if (string.IsNullOrEmpty(name))
            return Placeholder("Screen node is missing 'screen'.");
        var reg = registry.GetScreen(name!);
        if (reg is null)
            return Placeholder($"Unknown screen: {name}");
        return CreateScreenView(reg) ?? Placeholder($"Screen '{name}' is not a View.");
    }

    private View BuildRegisteredOrUnknown(UiNode node, Context ctx)
    {
        var reg = registry.GetControl(node.Type);
        if (reg is null)
            return Placeholder($"Unsupported: {node.Type}");

        object instance;
        try
        {
            instance = ActivatorUtilities.CreateInstance(services, reg.ControlType);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GenerativeUI] Could not create control '{reg.Name}': {ex.Message}");
            return Placeholder($"Could not create '{reg.Name}'.");
        }

        if (instance is not View controlView)
            return Placeholder($"Control '{reg.Name}' is not a View.");

        ApplyControlProps(controlView, node, reg, ctx);
        return controlView;
    }

    private static void ApplyControlProps(View control, UiNode node, UiControlRegistration reg, Context ctx)
    {
        var props = node.Props;
        if (props is null)
            return;

        foreach (var prop in reg.Props)
        {
            if (!props.Value.TryGetProperty(prop.Name, out var value))
                continue;

            var bindable = FindBindableProperty(control, prop.Name);
            if (bindable is null)
            {
                Debug.WriteLine($"[GenerativeUI] Control '{reg.Name}' has no bindable property '{prop.Name}'.");
                continue;
            }

            // { "bind": "path" } → one-way into data; { "key": "formKey" } → two-way into form; else literal.
            if (value.ValueKind == System.Text.Json.JsonValueKind.Object && value.TryGetProperty("bind", out var b) && b.ValueKind == System.Text.Json.JsonValueKind.String)
                control.SetBinding(bindable, DslBinding.Compile(b.GetString()!, source: ctx.Data));
            else if (value.ValueKind == System.Text.Json.JsonValueKind.Object && value.TryGetProperty("key", out var k) && k.ValueKind == System.Text.Json.JsonValueKind.String)
                control.SetBinding(bindable, DslBinding.Compile(k.GetString()!, BindingMode.TwoWay, source: ctx.Form));
            else
                control.SetValue(bindable, LiteralValue(value, bindable.ReturnType));
        }
    }

    private static BindableProperty? FindBindableProperty(View control, string propName)
    {
        var field = control.GetType().GetField($"{propName}Property",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy | System.Reflection.BindingFlags.IgnoreCase);
        return field?.GetValue(null) as BindableProperty;
    }

    private static object? LiteralValue(System.Text.Json.JsonElement value, Type targetType)
    {
        var text = value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : value.GetRawText();
        if (targetType == typeof(string)) return text;
        if (targetType == typeof(double) && double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
        if (targetType == typeof(int) && int.TryParse(text, out var i)) return i;
        if (targetType == typeof(bool) && bool.TryParse(text, out var b)) return b;
        return text;
    }

    private View? CreateScreenView(UiScreenRegistration reg)
    {
        try
        {
            return ActivatorUtilities.CreateInstance(services, reg.ScreenType) as View;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GenerativeUI] Could not create screen '{reg.Name}': {ex.Message}");
            return null;
        }
    }

    // ── Placeholders ────────────────────────────────────────────────────────────────────────────

    internal static View Placeholder(string message) => new Border
    {
        Padding = new Thickness(10, 6),
        BackgroundColor = Color.FromArgb("#FDE7E9"),
        StrokeThickness = 0,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
        HorizontalOptions = LayoutOptions.Start,
        Content = new Label { Text = message, FontSize = 12, TextColor = Color.FromArgb("#A4262C") },
    };

    private sealed class BoolConverter : IValueConverter
    {
        public static readonly BoolConverter Instance = new();
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var r) => r,
            _ => false,
        };
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b && b;
    }
}
