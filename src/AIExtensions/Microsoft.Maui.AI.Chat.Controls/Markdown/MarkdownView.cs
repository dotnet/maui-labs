// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Renders Markdown, or an already-parsed rich-text document, as native MAUI views.</summary>
/// <remarks>
/// Built for streaming: each update keeps the views of leading top-level blocks that are
/// structurally unchanged and rebuilds only from the first block that differs, so a growing
/// answer only re-renders its tail. Nodes passed to <see cref="SetDocument"/> must not be
/// mutated afterwards (the top-level list itself is copied). Members are UI-thread affine.
/// The look comes from the chat theme (<see cref="AIChatThemeKeys"/>); the first and last blocks
/// have no outer margin, so the text lines up with the cards and rows around it.
/// </remarks>
internal sealed class MarkdownView : ContentView
{
    internal static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
        nameof(FontSize),
        typeof(double),
        typeof(MarkdownView),
        15d,
        propertyChanged: static (bindable, _, _) => ((MarkdownView)bindable).OnFontSizeChanged());

    internal static readonly BindableProperty CodeFontSizeProperty = BindableProperty.Create(
        nameof(CodeFontSize),
        typeof(double),
        typeof(MarkdownView),
        13d,
        propertyChanged: static (bindable, _, _) => ((MarkdownView)bindable).OnFontSizeChanged());

    private readonly bool _themed = ChatElements.EnsureTheme();
    private readonly VerticalStackLayout _blocks;
    private readonly MarkdownRenderer _renderer;
    private readonly List<View?> _blockViews = new();
    private readonly List<Thickness> _blockMargins = new();
    private IReadOnlyList<RichTextNode> _document = Array.Empty<RichTextNode>();
    private string? _markdown;

    public MarkdownView()
    {
        _renderer = new MarkdownRenderer(OpenLinkAsync);
        _blocks = new ChatVerticalStackLayout();
        _blocks.SetDynamicResource(StackBase.SpacingProperty, AIChatThemeKeys.MarkdownBlockSpacing);
        Content = _blocks;

        SetDynamicResource(FontSizeProperty, AIChatThemeKeys.BodyFontSize);
        SetDynamicResource(CodeFontSizeProperty, AIChatThemeKeys.CodeFontSize);
        _renderer.FontSize = FontSize;
        _renderer.CodeFontSize = CodeFontSize;
    }

    /// <summary>The body font size (<see cref="AIChatThemeKeys.BodyFontSize"/>); headings scale from it.</summary>
    internal double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>The code font size (<see cref="AIChatThemeKeys.CodeFontSize"/>); inline code keeps its ratio to the text.</summary>
    internal double CodeFontSize
    {
        get => (double)GetValue(CodeFontSizeProperty);
        set => SetValue(CodeFontSizeProperty, value);
    }

    /// <summary>The Markdown last passed to <see cref="SetMarkdown"/>, or <see langword="null"/> after <see cref="SetDocument"/>.</summary>
    internal string? Markdown => _markdown;

    /// <summary>The top-level nodes currently rendered.</summary>
    internal IReadOnlyList<RichTextNode> Document => _document;

    /// <summary>The view built for each top-level node (<see langword="null"/> when a node renders nothing).</summary>
    internal IReadOnlyList<View?> BlockViews => _blockViews;

    /// <summary>Opens safe link targets. Defaults to <see cref="Launcher.Default"/>; replaceable for tests or hosts.</summary>
    internal Func<Uri, Task<bool>>? LinkLauncher { get; set; }

    /// <summary>Parses and renders Markdown. Unchanged text is ignored.</summary>
    internal void SetMarkdown(string? markdown)
    {
        markdown ??= string.Empty;
        if (_markdown is not null && string.Equals(_markdown, markdown, StringComparison.Ordinal))
            return;

        var document = MarkdownRichTextParser.Parse(markdown);
        _markdown = markdown;
        Render(document);
    }

    /// <summary>Renders an already-parsed document (for example from a <see cref="RichTextContent"/>).</summary>
    internal void SetDocument(IReadOnlyList<RichTextNode> nodes)
    {
        _markdown = null;
        // Snapshot the caller's list so a later call with the same, appended-to instance still
        // compares against what is on screen.
        Render(nodes is null ? Array.Empty<RichTextNode>() : nodes.ToArray());
    }

    /// <summary>Opens <paramref name="url"/> when it is an http, https or mailto link. Never throws.</summary>
    internal async Task<bool> OpenLinkAsync(string? url)
    {
        if (!MarkdownRenderer.TryCreateLinkUri(url, out var uri))
            return false;

        try
        {
            return LinkLauncher is { } launcher
                ? await launcher(uri)
                : await Launcher.Default.OpenAsync(uri);
        }
        catch (Exception)
        {
            // No handler for the scheme, no UI context, or a platform failure: ignore the tap.
            return false;
        }
    }

    private void Render(IReadOnlyList<RichTextNode> document)
    {
        // A changed reference definition can restyle any earlier block, so start over then.
        var keep = 0;
        if (_renderer.UpdateDefinitions(document))
        {
            var count = Math.Min(_document.Count, document.Count);
            while (keep < count && RichTextNodeComparer.AreEqual(_document[keep], document[keep]))
                keep++;
        }

        RemoveBlocksFrom(keep);
        for (var i = keep; i < document.Count; i++)
        {
            var view = _renderer.RenderBlock(document[i]);
            _blockViews.Add(view);
            _blockMargins.Add(view?.Margin ?? default);
            if (view is not null)
                _blocks.Children.Add(view);
        }

        _document = document;
        TrimOuterMargins();
    }

    private void RemoveBlocksFrom(int index)
    {
        for (var i = _blockViews.Count - 1; i >= index; i--)
        {
            // Block views are appended in document order, so the trailing ones come off the end.
            if (_blockViews[i] is not null)
                _blocks.Children.RemoveAt(_blocks.Children.Count - 1);
            _blockViews.RemoveAt(i);
            _blockMargins.RemoveAt(i);
        }
    }

    // Space above a heading or around a rule separates it from its neighbours, but on the first or last block it
    // would only push the text away from the edges of the row.
    private void TrimOuterMargins()
    {
        var first = _blockViews.FindIndex(static view => view is not null);
        var last = _blockViews.FindLastIndex(static view => view is not null);
        for (var i = 0; i < _blockViews.Count; i++)
        {
            if (_blockViews[i] is not { } view)
                continue;

            var margin = _blockMargins[i];
            if (i == first)
                margin.Top = 0;
            if (i == last)
                margin.Bottom = 0;
            if (view.Margin != margin)
                view.Margin = margin;
        }
    }

    private void OnFontSizeChanged()
    {
        _renderer.FontSize = FontSize;
        _renderer.CodeFontSize = CodeFontSize;
        var document = _document;
        RemoveBlocksFrom(0);
        _document = Array.Empty<RichTextNode>();
        Render(document);
    }
}
