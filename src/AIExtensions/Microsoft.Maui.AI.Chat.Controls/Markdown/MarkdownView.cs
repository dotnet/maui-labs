// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Renders Markdown, or an already-parsed rich-text document, as native MAUI views.</summary>
/// <remarks>
/// Built for streaming: each update keeps the views of unchanged blocks and changes the views of a
/// block that grew in place (see <see cref="MarkdownRenderer"/>), so a growing answer costs work in
/// proportion to what changed. Nodes passed to <see cref="SetDocument"/> must not be mutated
/// afterwards (the top-level list itself is copied). Members are UI-thread affine. The look comes
/// from the chat theme (<see cref="AIChatThemeKeys"/>); the first and last blocks have no outer
/// margin, so the text lines up with the cards and rows around it.
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
    private readonly List<MarkdownRenderer.Rendering> _renderings = new();
    private readonly List<View?> _blockViews = new();
    private readonly List<Thickness> _blockMargins = new();
    private readonly List<View> _shownBlocks = new();
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
        // A changed reference definition can restyle any block, so start over then.
        if (!_renderer.UpdateDefinitions(document))
            Clear();

        for (var i = 0; i < document.Count; i++)
        {
            if (i == _renderings.Count)
            {
                var rendering = _renderer.Render(document[i]);
                _renderings.Add(rendering);
                _blockViews.Add(rendering.View);
                _blockMargins.Add(rendering.View?.Margin ?? default);
                continue;
            }

            _renderings[i] = _renderings[i].Update(document[i]);
            var view = _renderings[i].View;
            if (ReferenceEquals(view, _blockViews[i]))
                continue;

            // The old view can live on inside the new one (a paragraph that gains an image stacks its text above
            // it), so it gets back the margin it had before the outer margins were trimmed.
            if (_blockViews[i] is { } previous)
                previous.Margin = _blockMargins[i];
            _blockViews[i] = view;
            _blockMargins[i] = view?.Margin ?? default;
        }

        if (_renderings.Count > document.Count)
        {
            var extra = _renderings.Count - document.Count;
            _renderings.RemoveRange(document.Count, extra);
            _blockViews.RemoveRange(document.Count, extra);
            _blockMargins.RemoveRange(document.Count, extra);
        }

        _shownBlocks.Clear();
        foreach (var view in _blockViews)
        {
            if (view is not null)
                _shownBlocks.Add(view);
        }

        MarkdownRenderer.SyncChildren(_blocks, _shownBlocks);
        _document = document;
        TrimOuterMargins();
    }

    private void Clear()
    {
        _renderings.Clear();
        _blockViews.Clear();
        _blockMargins.Clear();
        _blocks.Clear();
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
        Clear();
        Render(_document);
    }
}
