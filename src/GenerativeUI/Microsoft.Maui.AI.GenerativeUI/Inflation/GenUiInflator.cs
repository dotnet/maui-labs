using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    /// Inflates a document against the persistent state graph <paramref name="stateRoot"/>: display
    /// <c>bind</c> paths resolve one-way, editable <c>key</c> paths two-way; both stay live as the
    /// state is patched.
    /// </summary>
    public View Inflate(UiDocument document, UiObject stateRoot)
    {
        if (document.Ui is null)
            return Placeholder("Empty document (no 'ui' node).");

        var ctx = new Context(stateRoot);
        return InflateNode(document.Ui, ctx);
    }

    private sealed class Context(UiObject root, bool rowMode = false)
    {
        /// <summary>The binding source. Null in row mode, where per-item BindingContext is used.</summary>
        public UiObject? Root { get; } = root;

        /// <summary>True inside an itemsBind row template — bindings resolve against BindingContext.</summary>
        public bool RowMode { get; } = rowMode;

        public int Depth;
        public int Count;

        /// <summary>A one-way display binding that respects row mode.</summary>
        public Microsoft.Maui.Controls.Binding OneWay(string path)
            => RowMode ? DslBinding.Compile(path) : DslBinding.Compile(path, source: Root);

        /// <summary>A two-way editable binding that respects row mode.</summary>
        public Microsoft.Maui.Controls.Binding TwoWay(string path, IValueConverter? converter = null)
            => RowMode ? DslBinding.Compile(path, BindingMode.TwoWay, converter)
                       : DslBinding.Compile(path, BindingMode.TwoWay, converter, Root);
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
            ApplyInlineProperties(view, node);
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
        "Grid" => BuildGrid(node, ctx),
        "Card" => BuildCard(node, ctx),
        "Scroll" => BuildScroll(node, ctx),
        "Separator" => BuildSeparator(node),
        "Spacer" => BuildSpacer(node),
        "Label" => BuildLabel(node, ctx),
        "Image" => BuildImage(node, ctx),
        "Badge" => BuildBadge(node, ctx),
        "Icon" => BuildIcon(node),
        "Button" => BuildButton(node, ctx),
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
        if (TryThickness(node, "padding") is { } padding)
            layout.Padding = padding;
        foreach (var child in node.Children)
            layout.Add(InflateNode(child, ctx));
        return layout;
    }

    private View BuildGrid(UiNode node, Context ctx)
    {
        var grid = new Grid
        {
            ColumnSpacing = Clamp(node.GetNumber("columnSpacing") ?? 0, 0, 64),
            RowSpacing = Clamp(node.GetNumber("rowSpacing") ?? 0, 0, 64),
        };

        if (TryThickness(node, "padding") is { } padding)
            grid.Padding = padding;

        foreach (var column in ParseGridLengths(node.GetString("columns") ?? "*"))
            grid.ColumnDefinitions.Add(new ColumnDefinition(column));
        foreach (var row in ParseGridLengths(node.GetString("rows") ?? "Auto"))
            grid.RowDefinitions.Add(new RowDefinition(row));

        foreach (var childNode in node.Children)
        {
            var child = InflateNode(childNode, ctx);
            Grid.SetColumn(child, Math.Max(0, (int)(childNode.GetNumber("column") ?? 0)));
            Grid.SetRow(child, Math.Max(0, (int)(childNode.GetNumber("row") ?? 0)));
            Grid.SetColumnSpan(child, Math.Max(1, (int)(childNode.GetNumber("columnSpan") ?? 1)));
            Grid.SetRowSpan(child, Math.Max(1, (int)(childNode.GetNumber("rowSpan") ?? 1)));
            grid.Add(child);
        }

        return grid;
    }

    private View BuildCard(UiNode node, Context ctx)
    {
        var content = new VerticalStackLayout
        {
            Spacing = Clamp(node.GetNumber("contentSpacing") ?? 6, 0, 64),
        };
        foreach (var child in node.Children)
            content.Add(InflateNode(child, ctx));

        var card = new Border
        {
            Padding = TryThickness(node, "padding") ?? new Thickness(12),
            StrokeThickness = 1,
            Stroke = new SolidColorBrush(Color.FromArgb("#E0E0E0")),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Content = content,
        };

        if (TryColor(node.GetString("stroke"), out var stroke))
            card.Stroke = new SolidColorBrush(stroke);
        if (node.GetNumber("strokeThickness") is { } strokeThickness)
            card.StrokeThickness = Clamp(strokeThickness, 0, 16);
        if (node.GetNumber("cornerRadius") is { } cornerRadius)
            card.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            {
                CornerRadius = Clamp(cornerRadius, 0, 64),
            };

        return card;
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
        var label = new Label
        {
            LineBreakMode = node.GetBool("wrap") == false ? LineBreakMode.TailTruncation : LineBreakMode.WordWrap,
        };
        if (node.Bind is { } path)
            label.SetBinding(Label.TextProperty, ctx.OneWay(path));
        else
            label.Text = node.GetString("text") ?? "";

        if (TryColor(node.GetString("textColor"), out var textColor))
            label.TextColor = textColor;
        if (node.GetNumber("fontSize") is { } fontSize)
            label.FontSize = Clamp(fontSize, 8, 72);
        if (string.Equals(node.GetString("fontWeight"), "bold", StringComparison.OrdinalIgnoreCase))
            label.FontAttributes = FontAttributes.Bold;
        if (node.GetString("fontFamily") is { Length: > 0 } fontFamily)
            label.FontFamily = fontFamily;
        if (node.GetNumber("maxLines") is { } maxLines)
            label.MaxLines = Math.Max(1, (int)Clamp(maxLines, 1, 20));
        if (node.GetNumber("lineHeight") is { } lineHeight)
            label.LineHeight = Clamp(lineHeight, 0.8, 3);
        if (ParseTextAlignment(node.GetString("textAlign")) is { } textAlignment)
            label.HorizontalTextAlignment = textAlignment;

        return label;
    }

    private static View BuildImage(UiNode node, Context ctx)
    {
        // Emoji-as-image (no URL): render a large glyph label.
        if (node.GetString("emoji") is { Length: > 0 } emoji)
            return new Label { Text = emoji, FontSize = node.GetNumber("size") ?? 32 };

        var image = new Image
        {
            Aspect = string.Equals(node.GetString("aspect"), "fill", StringComparison.OrdinalIgnoreCase)
                ? Aspect.AspectFill
                : Aspect.AspectFit,
        };
        if (node.GetNumber("size") is { } size)
        {
            image.WidthRequest = Clamp(size, 16, 1024);
            image.HeightRequest = Clamp(size, 16, 1024);
        }
        if (node.Bind is { } path)
            image.SetBinding(Image.SourceProperty, ctx.OneWay(path));
        else if (node.GetProperty("source") is { ValueKind: JsonValueKind.Object } source &&
                 source.TryGetProperty("bind", out var sourceBind) &&
                 sourceBind.ValueKind == JsonValueKind.String)
            image.SetBinding(Image.SourceProperty, ctx.OneWay(sourceBind.GetString()!));
        else if (node.GetString("source") is { Length: > 0 } src)
            image.Source = src;

        if (node.GetNumber("cornerRadius") is not { } cornerRadius)
            return image;

        var radius = Clamp(cornerRadius, 0, 64);
        var frame = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = radius },
            Content = image,
        };
        if (node.GetNumber("size") is { } frameSize)
        {
            frame.WidthRequest = Clamp(frameSize, 16, 1024);
            frame.HeightRequest = Clamp(frameSize, 16, 1024);
            image.WidthRequest = -1;
            image.HeightRequest = -1;
        }
        return frame;
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
            label.SetBinding(Label.TextProperty, ctx.OneWay(path));
        else
            label.Text = node.GetString("text") ?? "";

        if (TryColor(node.GetString("textColor"), out var textColor))
            label.TextColor = textColor;
        if (node.GetNumber("fontSize") is { } fontSize)
            label.FontSize = Clamp(fontSize, 8, 48);

        var badge = new Border
        {
            Padding = TryThickness(node, "padding") ?? new Thickness(8, 3),
            BackgroundColor = bg,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            HorizontalOptions = LayoutOptions.Start,
            Content = label,
        };

        if (TryColor(node.GetString("stroke"), out var stroke))
            badge.Stroke = new SolidColorBrush(stroke);
        if (node.GetNumber("strokeThickness") is { } strokeThickness)
            badge.StrokeThickness = Clamp(strokeThickness, 0, 8);
        if (node.GetNumber("cornerRadius") is { } cornerRadius)
            badge.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            {
                CornerRadius = Clamp(cornerRadius, 0, 64),
            };
        return badge;
    }

    private static View BuildIcon(UiNode node)
        => new Label { Text = node.GetString("glyph") ?? "", FontSize = node.GetNumber("size") ?? 20 };

    // ── Interactive ─────────────────────────────────────────────────────────────────────────────

    private View BuildButton(UiNode node, Context ctx)
    {
        var button = new Button { Text = node.GetString("text") ?? "" };
        if (TryColor(node.GetString("textColor"), out var textColor))
            button.TextColor = textColor;
        if (TryColor(node.GetString("borderColor"), out var borderColor))
            button.BorderColor = borderColor;
        if (node.GetNumber("borderWidth") is { } borderWidth)
            button.BorderWidth = Clamp(borderWidth, 0, 12);
        if (node.GetNumber("cornerRadius") is { } cornerRadius)
            button.CornerRadius = (int)Clamp(cornerRadius, 0, 64);
        if (node.GetNumber("fontSize") is { } fontSize)
            button.FontSize = Clamp(fontSize, 8, 48);
        if (string.Equals(node.GetString("fontWeight"), "bold", StringComparison.OrdinalIgnoreCase))
            button.FontAttributes = FontAttributes.Bold;
        if (TryThickness(node, "padding") is { } padding)
            button.Padding = padding;

        var intentName = node.GetString("intent");
        if (!string.IsNullOrEmpty(intentName))
        {
            button.Clicked += (_, _) =>
            {
                var source = ctx.RowMode ? button.BindingContext as UiObject : ctx.Root;
                JsonNode? payloadNode = null;
                if (node.GetProperty("payload") is { } payload)
                    payloadNode = ResolvePayload(payload, source);
                else if (ctx.RowMode && source is not null)
                    payloadNode = UiObjectBuilder.ToJson(source);

                var bridge = services.GetService(typeof(IChatBridge)) as IChatBridge;
                _ = bridge?.RaiseIntentAsync(new UiIntent(
                    intentName!,
                    payloadNode?.ToJsonString()));
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

        stack.Add(MakeInput(node.GetString("kind"), key!, node.GetString("placeholder"), ctx));
        return stack;
    }

    private static View BuildEntry(UiNode node, Context ctx)
    {
        var key = node.GetString("key");
        if (string.IsNullOrEmpty(key))
            return Placeholder("Entry is missing 'key'.");
        return MakeInput(node.GetString("kind"), key!, node.GetString("placeholder"), ctx);
    }

    private static View MakeInput(string? kind, string key, string? placeholder, Context ctx)
    {
        switch (kind)
        {
            case "multiline":
                var editor = new Editor { Placeholder = placeholder, AutoSize = EditorAutoSizeOption.TextChanges };
                editor.SetBinding(Editor.TextProperty, ctx.TwoWay(key));
                return editor;

            case "bool":
                var sw = new Microsoft.Maui.Controls.Switch();
                sw.SetBinding(Microsoft.Maui.Controls.Switch.IsToggledProperty, ctx.TwoWay(key, BoolConverter.Instance));
                return sw;

            case "number":
                var numeric = new Entry { Placeholder = placeholder, Keyboard = Keyboard.Numeric };
                numeric.SetBinding(Entry.TextProperty, ctx.TwoWay(key));
                return numeric;

            default:
                var entry = new Entry { Placeholder = placeholder };
                entry.SetBinding(Entry.TextProperty, ctx.TwoWay(key));
                return entry;
        }
    }

    // ── Collections ─────────────────────────────────────────────────────────────────────────────

    private View BuildList(UiNode node, Context ctx)
    {
        // Data-bound list: itemsBind points at a state collection; a single template child is repeated
        // per item (row BindingContext = the item), so add/remove reflects without re-inflation.
        if (node.GetString("itemsBind") is { Length: > 0 } itemsPath && node.Children.Count > 0)
            return BuildBoundList(itemsPath, node.Children[0], ctx);

        // Static list: children are pre-expanded rows.
        var stack = new VerticalStackLayout { Spacing = Clamp(node.GetNumber("spacing") ?? 8, 0, 64) };
        foreach (var row in node.Children)
            stack.Add(InflateNode(row, ctx));
        return stack;
    }

    private View BuildBoundList(string itemsPath, UiNode template, Context ctx)
    {
        var container = new VerticalStackLayout { Spacing = 12 };

        // Resolve the collection node in the state tree (auto-vivified, stable identity).
        var collectionNode = ResolvePath(ctx.Root, itemsPath);
        BindableLayout.SetItemsSource(container, collectionNode?.Children);
        BindableLayout.SetItemTemplate(container, new DataTemplate(() =>
        {
            // Row mode: the item UiObject is the BindingContext; template binds resolve against it.
            var rowCtx = new Context(root: null!, rowMode: true);
            return InflateNode(template, rowCtx);
        }));
        return container;
    }

    private static UiObject? ResolvePath(UiObject? root, string dottedPath)
    {
        if (root is null)
            return null;
        var node = root;
        foreach (var seg in dottedPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            node = node[seg];
        return node;
    }

    /// <summary>
    /// Resolves an intent payload at click time. Literal JSON is preserved; any nested
    /// <c>{ "bind": "path" }</c> descriptor is replaced with the current value from the button's
    /// binding source (the row item inside an itemsBind template).
    /// </summary>
    private static JsonNode? ResolvePayload(JsonElement value, UiObject? source)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                if (source is not null &&
                    value.TryGetProperty("bind", out var bind) &&
                    bind.ValueKind == JsonValueKind.String)
                {
                    var bound = ResolvePath(source, bind.GetString()!);
                    return bound is null ? null : UiObjectBuilder.ToJson(bound);
                }

                var obj = new JsonObject();
                foreach (var property in value.EnumerateObject())
                    obj[property.Name] = ResolvePayload(property.Value, source);
                return obj;

            case JsonValueKind.Array:
                var array = new JsonArray();
                foreach (var item in value.EnumerateArray())
                    array.Add(ResolvePayload(item, source));
                return array;

            case JsonValueKind.String:
                return JsonValue.Create(value.GetString());
            case JsonValueKind.Number:
                return value.TryGetInt64(out var integer)
                    ? JsonValue.Create(integer)
                    : JsonValue.Create(value.GetDouble());
            case JsonValueKind.True:
            case JsonValueKind.False:
                return JsonValue.Create(value.GetBoolean());
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                return null;
        }
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
                control.SetBinding(bindable, ctx.OneWay(b.GetString()!));
            else if (value.ValueKind == System.Text.Json.JsonValueKind.Object && value.TryGetProperty("key", out var k) && k.ValueKind == System.Text.Json.JsonValueKind.String)
                control.SetBinding(bindable, ctx.TwoWay(k.GetString()!));
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

    // ── Inline visual / layout properties ──────────────────────────────────────────────────────

    private static void ApplyInlineProperties(View view, UiNode node)
    {
        if (TryThickness(node, "margin") is { } margin)
            view.Margin = margin;

        if (TryBrush(node, "background") is { } background)
            view.Background = background;

        if (node.GetNumber("width") is { } width)
            view.WidthRequest = Clamp(width, 0, 2048);
        if (node.GetNumber("height") is { } height)
            view.HeightRequest = Clamp(height, 0, 2048);
        if (node.GetNumber("minWidth") is { } minWidth)
            view.MinimumWidthRequest = Clamp(minWidth, 0, 2048);
        if (node.GetNumber("minHeight") is { } minHeight)
            view.MinimumHeightRequest = Clamp(minHeight, 0, 2048);
        if (node.GetNumber("maxWidth") is { } maxWidth)
            view.MaximumWidthRequest = Clamp(maxWidth, 0, 4096);
        if (node.GetNumber("maxHeight") is { } maxHeight)
            view.MaximumHeightRequest = Clamp(maxHeight, 0, 4096);
        if (node.GetNumber("opacity") is { } opacity)
            view.Opacity = Clamp(opacity, 0, 1);

        if (ParseLayoutOptions(node.GetString("horizontal")) is { } horizontal)
            view.HorizontalOptions = horizontal;
        if (ParseLayoutOptions(node.GetString("vertical")) is { } vertical)
            view.VerticalOptions = vertical;

        if (node.GetProperty("shadow") is { ValueKind: JsonValueKind.Object } shadow)
        {
            var colorText = shadow.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : "#000000";
            if (TryColor(colorText, out var color))
            {
                view.Shadow = new Shadow
                {
                    Brush = new SolidColorBrush(color),
                    Opacity = (float)Clamp(GetNumber(shadow, "opacity") ?? 0.18, 0, 1),
                    Radius = (float)Clamp(GetNumber(shadow, "radius") ?? 12, 0, 64),
                    Offset = new Point(
                        Clamp(GetNumber(shadow, "offsetX") ?? 0, -64, 64),
                        Clamp(GetNumber(shadow, "offsetY") ?? 6, -64, 64)),
                };
            }
        }
    }

    private static Brush? TryBrush(UiNode node, string name)
    {
        var value = node.GetProperty(name);
        if (value is null)
            return null;

        if (value.Value.ValueKind == JsonValueKind.String &&
            TryColor(value.Value.GetString(), out var solid))
            return new SolidColorBrush(solid);

        if (value.Value.ValueKind != JsonValueKind.Object)
            return null;

        var type = value.Value.TryGetProperty("type", out var typeNode) &&
                   typeNode.ValueKind == JsonValueKind.String
            ? typeNode.GetString()
            : "linear";
        if (!string.Equals(type, "linear", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(type, "linearGradient", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!value.Value.TryGetProperty("colors", out var colorsNode) ||
            colorsNode.ValueKind != JsonValueKind.Array)
            return null;

        var colors = colorsNode.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString())
            .Where(x => TryColor(x, out _))
            .Select(x => { TryColor(x, out var color); return color; })
            .ToList();
        if (colors.Count < 2)
            return null;

        var angle = Clamp(GetNumber(value.Value, "angle") ?? 135, 0, 360);
        var radians = angle * Math.PI / 180d;
        var dx = Math.Cos(radians) * 0.5;
        var dy = Math.Sin(radians) * 0.5;
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5 - dx, 0.5 - dy),
            EndPoint = new Point(0.5 + dx, 0.5 + dy),
        };
        for (var i = 0; i < colors.Count; i++)
            brush.GradientStops.Add(new GradientStop(colors[i], (float)i / (colors.Count - 1)));
        return brush;
    }

    private static Thickness? TryThickness(UiNode node, string name)
    {
        var value = node.GetProperty(name);
        if (value is null)
            return null;

        if (value.Value.ValueKind == JsonValueKind.Number)
        {
            var uniform = Clamp(value.Value.GetDouble(), 0, 128);
            return new Thickness(uniform);
        }

        if (value.Value.ValueKind != JsonValueKind.Array)
            return null;

        var values = value.Value.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.Number)
            .Select(x => Clamp(x.GetDouble(), 0, 128))
            .ToArray();
        return values.Length switch
        {
            1 => new Thickness(values[0]),
            2 => new Thickness(values[0], values[1]),
            >= 4 => new Thickness(values[0], values[1], values[2], values[3]),
            _ => null,
        };
    }

    private static IEnumerable<GridLength> ParseGridLengths(string spec)
    {
        foreach (var raw in spec.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(raw, "auto", StringComparison.OrdinalIgnoreCase))
            {
                yield return GridLength.Auto;
                continue;
            }

            if (raw.EndsWith('*'))
            {
                var weightText = raw[..^1];
                var weight = string.IsNullOrEmpty(weightText) ? 1 :
                    double.TryParse(weightText, NumberStyles.Any, CultureInfo.InvariantCulture, out var w) ? w : 1;
                yield return new GridLength(Clamp(weight, 0.1, 20), GridUnitType.Star);
                continue;
            }

            yield return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var pixels)
                ? new GridLength(Clamp(pixels, 0, 2048), GridUnitType.Absolute)
                : GridLength.Auto;
        }
    }

    private static LayoutOptions? ParseLayoutOptions(string? value) => value?.ToLowerInvariant() switch
    {
        "start" => LayoutOptions.Start,
        "center" => LayoutOptions.Center,
        "end" => LayoutOptions.End,
        "fill" => LayoutOptions.Fill,
        _ => null,
    };

    private static TextAlignment? ParseTextAlignment(string? value) => value?.ToLowerInvariant() switch
    {
        "start" => TextAlignment.Start,
        "center" => TextAlignment.Center,
        "end" => TextAlignment.End,
        _ => null,
    };

    private static bool TryColor(string? value, out Color color)
    {
        color = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        try
        {
            color = Color.FromArgb(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static double? GetNumber(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static double Clamp(double value, double min, double max) => Math.Min(max, Math.Max(min, value));

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
