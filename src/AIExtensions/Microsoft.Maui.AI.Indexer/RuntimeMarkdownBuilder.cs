using System.Collections;
using System.Globalization;
using System.Text;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.AI.Indexer;

internal sealed class RuntimeMarkdownBuilder
{
    private readonly CurrentPageSnapshotOptions _options;
    private readonly StringBuilder _markdown = new();
    private readonly HashSet<IVisualTreeElement> _visited = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _automationIds = new(StringComparer.Ordinal);
    private readonly List<IndexedElement> _elements = [];

    private RuntimeMarkdownBuilder(CurrentPageSnapshotOptions options)
    {
        _options = options;
    }

    public static RuntimeSemanticSnapshot Render(
        Page page,
        string pageName,
        CurrentPageSnapshotOptions options)
    {
        var builder = new RuntimeMarkdownBuilder(options);
        builder.AppendLine(0, $"# Current UI: {pageName}");
        builder.AppendLine(0);
        builder.AppendLine(0, "Runtime snapshot: currently visible, materialized controls and live state.");
        builder.AppendLine(0);
        if (IsVisible(page) && !IndexingProperties.GetExcludeWithChildren(page))
            builder.RenderChildren(page, 0);

        return builder.Build();
    }

    public static RuntimeSemanticSnapshot RenderShellFlyout(
        Shell shell,
        CurrentPageSnapshotOptions options)
    {
        var builder = new RuntimeMarkdownBuilder(options);
        builder.AppendLine(0, $"# Current UI: {shell.GetType().Name}");
        builder.AppendLine(0);
        builder.AppendLine(0, "Runtime snapshot: open flyout menu and current page.");
        builder.AppendLine(0);

        if (!IsVisible(shell) || IndexingProperties.GetExcludeWithChildren(shell))
            return builder.Build();

        builder.RenderFlyoutPart("Flyout header", shell.FlyoutHeader);

        var currentContent = shell.CurrentItem?.CurrentItem?.CurrentItem;
        var excludedMenuItemTitles = currentContent?.MenuItems
            .Where(IndexingProperties.GetExcludeWithChildren)
            .Select(static item => item.Text)
            .Where(static title => !string.IsNullOrWhiteSpace(title))
            .ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);

        var hasCustomContent = builder.RenderFlyoutPart("Flyout content", shell.FlyoutContent);
        var renderedMenuItemTitles = hasCustomContent
            ? null
            : builder.RenderDefaultFlyoutItems(shell, excludedMenuItemTitles);

        builder.RenderFlyoutPart("Flyout footer", shell.FlyoutFooter);

        var activePathExcluded = RuntimePageIndexer.IsActiveShellPathExcluded(shell);
        if (!hasCustomContent && !activePathExcluded && currentContent is not null)
        {
            foreach (var menuItem in currentContent.MenuItems)
            {
                if (IndexingProperties.GetExcludeWithChildren(menuItem)
                    || !Shell.GetFlyoutItemIsVisible(menuItem)
                    || string.IsNullOrWhiteSpace(menuItem.Text)
                    || excludedMenuItemTitles.Contains(menuItem.Text)
                    || renderedMenuItemTitles!.Contains(menuItem.Text))
                {
                    continue;
                }

                var disabled = menuItem.IsEnabled ? "" : " [disabled]";
                builder.AppendLine(1, $"- Item: \"{Escape(builder.Normalize(menuItem.Text))}\"{disabled}");
                builder._elements.Add(new IndexedElement(
                    IndexedElementKind.Action,
                    builder.Normalize(menuItem.Text),
                    null,
                    null,
                    null,
                    false,
                    true,
                    false,
                    1,
                    menuItem.IsEnabled ? [] : ["disabled"]));
            }
        }

        var currentPage = shell.CurrentPage;
        if (currentPage is not null
            && !activePathExcluded
            && IsVisible(currentPage)
            && !IndexingProperties.GetExcludeWithChildren(currentPage))
        {
            builder.AppendLine(0, $"- Current page: {currentPage.GetType().Name}");
            builder.RenderChildren(currentPage, 1);
        }

        return builder.Build();
    }

    private HashSet<string> RenderDefaultFlyoutItems(
        Shell shell,
        HashSet<string> excludedMenuItemTitles)
    {
        AppendLine(0, "- Flyout menu:");
        var renderedMenuItemTitles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in shell.FlyoutItems)
        {
            if (group is not IEnumerable items)
                continue;

            foreach (var entry in items)
            {
                if (entry is not BaseShellItem item
                    || !item.IsVisible
                    || IsExcludedShellItem(item)
                    || string.Equals(item.GetType().Name, "MenuShellItem", StringComparison.Ordinal)
                        && excludedMenuItemTitles.Contains(item.Title)
                    || string.IsNullOrWhiteSpace(item.Title))
                {
                    continue;
                }

                var selected = IsCurrentShellItem(shell, item) ? " [selected]" : "";
                AppendLine(1, $"- Item: \"{Escape(Normalize(item.Title))}\"{selected}");
                if (string.Equals(item.GetType().Name, "MenuShellItem", StringComparison.Ordinal))
                    renderedMenuItemTitles.Add(item.Title);
            }
        }

        return renderedMenuItemTitles;
    }

    private bool RenderFlyoutPart(string label, object? content)
    {
        if (content is not IVisualTreeElement visual)
            return false;

        AppendLine(0, $"- {label}:");
        RenderNode(visual, 1);
        return true;
    }

    private static bool IsExcludedInShellHierarchy(Element element)
    {
        Element? current = element;
        while (current is not null && current is not Shell)
        {
            if (IndexingProperties.GetExcludeWithChildren(current))
                return true;

            current = current.Parent;
        }

        return false;
    }

    private static bool IsExcludedShellItem(BaseShellItem item)
    {
        if (IsExcludedInShellHierarchy(item))
            return true;

        return item switch
        {
            ShellItem shellItem
                => shellItem.CurrentItem is { } section
                    && (IndexingProperties.GetExcludeWithChildren(section)
                        || section.CurrentItem is { } content
                            && IndexingProperties.GetExcludeWithChildren(content)),
            ShellSection section
                => section.CurrentItem is { } content
                    && IndexingProperties.GetExcludeWithChildren(content),
            _ => false,
        };
    }

    private static bool IsCurrentShellItem(Shell shell, BaseShellItem item)
        => item switch
        {
            ShellContent content
                => ReferenceEquals(shell.CurrentItem?.CurrentItem?.CurrentItem, content),
            ShellSection section
                => ReferenceEquals(shell.CurrentItem?.CurrentItem, section),
            ShellItem shellItem
                => ReferenceEquals(shell.CurrentItem, shellItem),
            _ => false,
        };

    private RuntimeSemanticSnapshot Build()
    {
        if (_markdown.Length == 0)
            return new RuntimeSemanticSnapshot("", [.. _automationIds], _elements);

        return new RuntimeSemanticSnapshot(
            _markdown.ToString().TrimEnd() + "\n",
            [.. _automationIds],
            _elements);
    }

    private void RenderChildren(IVisualTreeElement parent, int indent)
    {
        foreach (var child in parent.GetVisualChildren())
            RenderNode(child, indent);
    }

    private void RenderNode(IVisualTreeElement node, int indent)
    {
        if (!_visited.Add(node)
            || node is BindableObject bindable && IndexingProperties.GetExcludeWithChildren(bindable)
            || !IsVisible(node))
            return;

        if (node is not VisualElement element)
        {
            RenderChildren(node, indent);
            return;
        }

        if (element is BoxView || IsDecorative(element))
            return;

        if (element is CollectionView collectionView)
        {
            RenderCollectionView(collectionView, indent);
            return;
        }

        if (IsSemanticElement(element)
            || HasSemanticDescription(element)
            || HasDirectTapGesture(element))
        {
            AppendLine(indent, RenderSemanticElement(element));
            _elements.Add(CreateIndexedElement(element, indent));
            RenderChildren(node, indent + 1);
            return;
        }

        if (IsCustomContainer(element))
        {
            AppendLine(indent, $"- [{element.GetType().Name}]:");
            _elements.Add(new IndexedElement(
                IndexedElementKind.Group,
                null,
                null,
                null,
                null,
                false,
                false,
                false,
                indent,
                []));
            RenderChildren(node, indent + 1);
            return;
        }

        RenderChildren(node, indent);
    }

    private void RenderCollectionView(
        CollectionView collectionView,
        int indent)
    {
        AppendLine(indent, RenderSemanticElement(collectionView));
        _elements.Add(CreateIndexedElement(collectionView, indent));

        var contexts = GetCollectionContexts(
            collectionView.ItemsSource,
            collectionView.IsGrouped);
        if (contexts.Items.Count == 0)
        {
            AppendLine(indent + 1, "- Empty view:");
            if (collectionView.EmptyView is IVisualTreeElement emptyView)
            {
                RenderNode(emptyView, indent + 2);
            }
            else
            {
                foreach (var child in ((IVisualTreeElement)collectionView).GetVisualChildren())
                    RenderNode(child, indent + 2);
            }
            return;
        }

        var itemIndex = 0;
        var groupIndex = 0;
        var renderedItems = new HashSet<object>();
        var renderedGroups = new HashSet<object>();
        RenderCollectionChildren(
            collectionView,
            collectionView,
            contexts,
            renderedItems,
            renderedGroups,
            indent + 1,
            ref itemIndex,
            ref groupIndex);
    }

    private void RenderCollectionChildren(
        CollectionView collectionView,
        IVisualTreeElement parent,
        CollectionContexts contexts,
        HashSet<object> renderedItems,
        HashSet<object> renderedGroups,
        int indent,
        ref int itemIndex,
        ref int groupIndex)
    {
        foreach (var child in parent.GetVisualChildren())
        {
            if (ReferenceEquals(child, collectionView.EmptyView)
                || !CanRenderCollectionRoot(child))
            {
                continue;
            }

            var bindingContext = (child as BindableObject)?.BindingContext;
            if (bindingContext is not null
                && contexts.Items.Contains(bindingContext))
            {
                if (!renderedItems.Add(bindingContext))
                    continue;

                AppendLine(indent, $"- Item {++itemIndex}:");
                RenderNode(child, indent + 1);
                continue;
            }

            if (bindingContext is not null
                && contexts.Groups.Contains(bindingContext))
            {
                if (!renderedGroups.Add(bindingContext))
                    continue;

                AppendLine(indent, $"- Group {++groupIndex}:");
                RenderNode(child, indent + 1);
                continue;
            }

            if (!_visited.Add(child))
                continue;

            RenderCollectionChildren(
                collectionView,
                child,
                contexts,
                renderedItems,
                renderedGroups,
                indent,
                ref itemIndex,
                ref groupIndex);
        }
    }

    private bool CanRenderCollectionRoot(IVisualTreeElement child)
        => !_visited.Contains(child)
            && IsVisible(child)
            && (child is not BindableObject bindable
                || !IndexingProperties.GetExcludeWithChildren(bindable)
                    && !IsDecorative(bindable));

    private static CollectionContexts GetCollectionContexts(
        object? itemsSource,
        bool isGrouped)
    {
        var groups = new HashSet<object>();
        var items = new HashSet<object>();
        if (itemsSource is not System.Collections.IEnumerable enumerable)
            return new CollectionContexts(groups, items);

        foreach (var entry in enumerable)
        {
            if (entry is null)
                continue;

            if (!isGrouped)
            {
                items.Add(entry);
                continue;
            }

            groups.Add(entry);
            if (entry is string
                || entry is not System.Collections.IEnumerable groupItems)
            {
                continue;
            }

            foreach (var item in groupItems)
            {
                if (item is not null)
                    items.Add(item);
            }
        }

        return new CollectionContexts(groups, items);
    }

    private sealed record CollectionContexts(
        HashSet<object> Groups,
        HashSet<object> Items);

    private static bool IsVisible(IVisualTreeElement node)
    {
        if (node is VisualElement visual)
            return visual.IsVisible && visual.Opacity > 0;

        if (node is IView view)
            return view.Visibility == Visibility.Visible && view.Opacity > 0;

        return true;
    }

    private static bool IsDecorative(BindableObject element)
        => SemanticProperties.GetDescription(element) is "";

    private static bool HasSemanticDescription(BindableObject element)
        => !string.IsNullOrWhiteSpace(SemanticProperties.GetDescription(element));

    private static bool IsSemanticElement(VisualElement element)
    {
        if (string.Equals(element.GetType().Name, "ListView", StringComparison.Ordinal))
            return true;

        return element is Label
            or Button
            or ImageButton
            or Entry
            or Editor
            or SearchBar
            or Slider
            or Stepper
            or Switch
            or CheckBox
            or RadioButton
            or Picker
            or DatePicker
            or TimePicker
            or Image
            or ActivityIndicator
            or ProgressBar
            or CollectionView
            or CarouselView
            or WebView;
    }

    private static bool IsCustomContainer(VisualElement element)
    {
        if (element is not ContentView and not Layout and not ScrollView and not Border)
            return false;

        return element.GetType().Assembly != typeof(ContentView).Assembly;
    }

    private string RenderSemanticElement(VisualElement element)
    {
        var headingLevel = GetHeadingLevel(element);
        var typeName = headingLevel is not null
            ? $"Heading (level {headingLevel}):"
            : $"{GetDisplayTypeName(element)}:";

        var displayText = GetDisplayText(element);
        var annotations = GetAnnotations(element);
        var display = displayText is null ? "" : $" \"{Escape(displayText)}\"";

        return $"- {typeName}{display}{annotations}";
    }

    private IndexedElement CreateIndexedElement(VisualElement element, int depth)
    {
        var automationId = !string.IsNullOrWhiteSpace(element.AutomationId)
            && !ContainsExcludedDescendant(element)
                ? element.AutomationId
                : null;
        var headingLevel = GetHeadingLevel(element);
        var kind = headingLevel is not null
            ? IndexedElementKind.Heading
            : HasDirectTapGesture(element)
                ? IndexedElementKind.Action
                : GetIndexedKind(element);
        var placeholder = element switch
        {
            Entry entry => NormalizeForElement(entry.Placeholder ?? "", element),
            Editor editor => NormalizeForElement(editor.Placeholder ?? "", element),
            SearchBar search => NormalizeForElement(search.Placeholder ?? "", element),
            _ => null,
        };
        var state = new List<string>();
        AddControlState(element, state);
        if (!element.IsEnabled)
            state.Add("disabled");
        if (element.IsFocused)
            state.Add("focused");

        return new IndexedElement(
            kind,
            GetDisplayText(element),
            NormalizeForElement(SemanticProperties.GetHint(element) ?? "", element),
            automationId,
            string.IsNullOrEmpty(placeholder) ? null : placeholder,
            false,
            kind == IndexedElementKind.Action || HasDirectTapGesture(element),
            false,
            depth,
            state);
    }

#pragma warning disable CS0618 // ListView remains a supported indexed legacy control.
    private static IndexedElementKind GetIndexedKind(VisualElement element)
        => element switch
        {
            Label => IndexedElementKind.Text,
            Button or ImageButton => IndexedElementKind.Action,
            Entry or Editor or SearchBar => IndexedElementKind.Input,
            Picker or DatePicker or TimePicker => IndexedElementKind.Selection,
            Switch or CheckBox or RadioButton => IndexedElementKind.Toggle,
            Slider or Stepper => IndexedElementKind.Range,
            CollectionView or ListView or CarouselView => IndexedElementKind.Collection,
            Image => IndexedElementKind.Image,
            GraphicsView => IndexedElementKind.Visual,
            ProgressBar or ActivityIndicator => IndexedElementKind.Status,
            WebView => IndexedElementKind.WebContent,
            _ when string.Equals(
                element.GetType().Name,
                "HybridWebView",
                StringComparison.Ordinal) => IndexedElementKind.WebContent,
            ContentView or Layout or ScrollView or Border => IndexedElementKind.Group,
            _ => IndexedElementKind.Unknown,
        };
#pragma warning restore CS0618

    private static bool HasDirectTapGesture(VisualElement element)
        => element is View view
            && view.GestureRecognizers.OfType<TapGestureRecognizer>().Any();

    private string? GetDisplayText(VisualElement element)
    {
        var description = SemanticProperties.GetDescription(element);
        if (!string.IsNullOrWhiteSpace(description))
            return NormalizeForElement(description, element);

        var value = element switch
        {
            Label label => label.Text,
            Button button => button.Text,
            ImageButton imageButton => GetImageSourceDisplay(imageButton.Source),
            Image image => GetImageSourceDisplay(image.Source),
            RadioButton radio => radio.Content?.ToString(),
            Picker picker => picker.Title,
            WebView webView => GetWebViewSourceDisplay(webView.Source),
            _ => null,
        };

        return string.IsNullOrWhiteSpace(value) ? null : NormalizeForElement(value, element);
    }

    private string GetAnnotations(VisualElement element)
    {
        var annotations = new List<string>();

        var placeholder = element switch
        {
            Entry entry => entry.Placeholder,
            Editor editor => editor.Placeholder,
            SearchBar search => search.Placeholder,
            _ => null,
        };
        if (!string.IsNullOrWhiteSpace(placeholder))
            annotations.Add($"placeholder: \"{Escape(NormalizeForElement(placeholder, element))}\"");

        if (!string.IsNullOrWhiteSpace(element.AutomationId)
            && !ContainsExcludedDescendant(element))
        {
            annotations.Add($"automationId: \"{Escape(element.AutomationId)}\"");
            _automationIds.Add(element.AutomationId);
        }

        var hint = SemanticProperties.GetHint(element);
        if (!string.IsNullOrWhiteSpace(hint))
            annotations.Add($"hint: {NormalizeForElement(hint, element)}");

        AddControlState(element, annotations);

        if (!element.IsEnabled)
            annotations.Add("disabled");
        if (element.IsFocused)
            annotations.Add("focused");

        return annotations.Count == 0
            ? ""
            : $" [{string.Join(", ", annotations)}]";
    }

    private static bool ContainsExcludedDescendant(IVisualTreeElement root)
    {
        var pending = new Stack<IVisualTreeElement>(root.GetVisualChildren());
        var visited = new HashSet<IVisualTreeElement>(ReferenceEqualityComparer.Instance);
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current))
                continue;
            if (current is BindableObject bindable
                && IndexingProperties.GetExcludeWithChildren(bindable))
            {
                return true;
            }

            foreach (var child in current.GetVisualChildren())
                pending.Push(child);
        }

        return false;
    }

    internal sealed record RuntimeSemanticSnapshot(
        string Markdown,
        IReadOnlyList<string> AutomationIds,
        IReadOnlyList<IndexedElement> Elements);

    private void AddControlState(VisualElement element, List<string> annotations)
    {
        switch (element)
        {
            case Entry { IsPassword: true } password:
                annotations.Add("secure input");
                annotations.Add(string.IsNullOrEmpty(password.Text) ? "empty" : "has text; value omitted");
                break;
            case Entry entry:
                AddInputState(entry.Text, entry.IsReadOnly, annotations);
                break;
            case Editor editor:
                AddInputState(editor.Text, editor.IsReadOnly, annotations);
                break;
            case SearchBar search:
                AddInputState(search.Text, false, annotations);
                break;
            case Slider slider:
                annotations.Add($"value: {FormatNumber(slider.Value)}");
                annotations.Add($"range: {FormatNumber(slider.Minimum)}–{FormatNumber(slider.Maximum)}");
                break;
            case Stepper stepper:
                annotations.Add($"value: {FormatNumber(stepper.Value)}");
                annotations.Add($"range: {FormatNumber(stepper.Minimum)}–{FormatNumber(stepper.Maximum)}");
                break;
            case Switch toggle:
                annotations.Add(toggle.IsToggled ? "on" : "off");
                break;
            case CheckBox checkBox:
                annotations.Add(checkBox.IsChecked ? "checked" : "unchecked");
                break;
            case RadioButton radio:
                annotations.Add(radio.IsChecked ? "selected" : "not selected");
                break;
            case Picker picker when picker.SelectedItem is not null:
                annotations.Add($"selected: \"{Escape(Normalize(GetPickerDisplayText(picker)))}\"");
                break;
            case DatePicker { Date: DateTime dateValue } date:
                annotations.Add($"value: {dateValue.ToString(date.Format, CultureInfo.CurrentCulture)}");
                break;
            case TimePicker { Time: TimeSpan timeValue } time:
                annotations.Add(
                    $"value: {DateTime.Today.Add(timeValue).ToString(time.Format, CultureInfo.CurrentCulture)}");
                break;
            case ActivityIndicator activity:
                annotations.Add(activity.IsRunning ? "running" : "stopped");
                break;
            case ProgressBar progress:
                annotations.Add($"value: {progress.Progress.ToString("P0", CultureInfo.InvariantCulture)}");
                break;
        }
    }

    private void AddInputState(string? text, bool isReadOnly, List<string> annotations)
    {
        if (_options.IncludeInputText && !string.IsNullOrEmpty(text))
            annotations.Add($"value: \"{Escape(Normalize(text))}\"");
        else
            annotations.Add(string.IsNullOrEmpty(text) ? "empty" : "has text; value omitted");
        if (isReadOnly)
            annotations.Add("read-only");
    }

    private static string GetPickerDisplayText(Picker picker)
    {
        if (picker.SelectedIndex >= 0 && picker.SelectedIndex < picker.Items.Count)
            return picker.Items[picker.SelectedIndex];

        return picker.SelectedItem?.ToString() ?? "";
    }

    private static string? GetImageSourceDisplay(ImageSource? source)
        => source switch
        {
            FileImageSource => "local image",
            UriImageSource => "remote image",
            FontImageSource => "font image",
            StreamImageSource => "stream image",
            _ => null,
        };

    private static string? GetWebViewSourceDisplay(WebViewSource? source)
        => source switch
        {
            UrlWebViewSource => "web content",
            HtmlWebViewSource => "inline HTML",
            _ => null,
        };

    private static int? GetHeadingLevel(BindableObject element)
        => SemanticProperties.GetHeadingLevel(element) switch
        {
            SemanticHeadingLevel.Level1 => 1,
            SemanticHeadingLevel.Level2 => 2,
            SemanticHeadingLevel.Level3 => 3,
            SemanticHeadingLevel.Level4 => 4,
            SemanticHeadingLevel.Level5 => 5,
            SemanticHeadingLevel.Level6 => 6,
            SemanticHeadingLevel.Level7 => 7,
            SemanticHeadingLevel.Level8 => 8,
            SemanticHeadingLevel.Level9 => 9,
            _ => null,
        };

    private static string GetDisplayTypeName(VisualElement element)
        => element switch
        {
            Label => "Label",
            Button => "Button",
            ImageButton => "ImageButton",
            Entry => "Entry",
            Editor => "Editor",
            SearchBar => "SearchBar",
            Slider => "Slider",
            Stepper => "Stepper",
            Switch => "Switch",
            CheckBox => "CheckBox",
            RadioButton => "RadioButton",
            Picker => "Picker",
            DatePicker => "DatePicker",
            TimePicker => "TimePicker",
            Image => "Image",
            ActivityIndicator => "ActivityIndicator",
            ProgressBar => "ProgressBar",
            CollectionView => "CollectionView",
            CarouselView => "CarouselView",
            WebView => "WebView",
            _ => element.GetType().Name,
        };

    private string Normalize(string value)
    {
        var normalized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        if (normalized.Length <= _options.MaximumTextLength)
            return normalized;

        return normalized[..(_options.MaximumTextLength - 1)] + "…";
    }

    private string NormalizeForElement(string value, VisualElement element)
    {
        var inputText = element switch
        {
            Entry entry => entry.Text,
            Editor editor => editor.Text,
            SearchBar search => search.Text,
            _ => null,
        };

        var mustRedact = element is Entry { IsPassword: true } || !_options.IncludeInputText;
        if (mustRedact && !string.IsNullOrEmpty(inputText))
            value = value.Replace(inputText, "••••", StringComparison.Ordinal);

        return Normalize(value);
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string FormatNumber(double value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);

    private void AppendLine(int indent, string text = "")
    {
        if (text.Length > 0)
            _markdown.Append(' ', indent * 2);
        _markdown.AppendLine(text);
    }
}
