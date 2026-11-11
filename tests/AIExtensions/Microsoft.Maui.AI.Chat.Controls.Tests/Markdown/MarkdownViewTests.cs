using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.Controls.Shapes;
using static Microsoft.Maui.AI.Chat.Controls.Tests.Markdown.MarkdownTestHelpers;

namespace Microsoft.Maui.AI.Chat.Controls.Tests.Markdown;

public class MarkdownViewTests
{
    [Fact]
    public void SetMarkdown_RendersANativeViewForEveryBlock()
    {
        var view = Themed();

        view.SetMarkdown("""
            ## Heading

            Plain **bold** *italic* ~~gone~~ `code`

            ```csharp
            var answer = 42;
            ```

            > quoted

            1. one
            2. two

            | a | b |
            |---|--:|
            | 1 | 2 |

            ---

            ![A cat](https://example.com/cat.png)
            """);

        Assert.Equal(view.Document.Count, view.BlockViews.Count);
        AssertLayoutMatchesBlocks(view);
        Assert.Collection(
            view.BlockViews,
            block => Assert.Equal(SemanticHeadingLevel.Level2, SemanticProperties.GetHeadingLevel(Assert.IsAssignableFrom<Label>(block))),
            block => Assert.NotNull(Assert.IsAssignableFrom<Label>(block).FormattedText),
            block => Assert.IsAssignableFrom<Border>(block),
            block => Assert.IsAssignableFrom<BoxView>(Assert.IsAssignableFrom<Grid>(block).Children[0]),
            block => Assert.IsAssignableFrom<Grid>(block),
            block => Assert.IsAssignableFrom<Border>(Assert.IsAssignableFrom<ScrollView>(block).Content),
            block => Assert.IsAssignableFrom<BoxView>(block),
            block => Assert.Equal("A cat, Image \u00B7 example.com", SemanticProperties.GetDescription(Assert.IsAssignableFrom<Border>(block))));

        var spans = ((Label)view.BlockViews[1]!).FormattedText.Spans;
        Assert.Contains(spans, span => span.Text == "bold" && span.FontAttributes == FontAttributes.Bold);
        Assert.Contains(spans, span => span.Text == "italic" && span.FontAttributes == FontAttributes.Italic);
        Assert.Contains(spans, span => span.Text == "gone" && span.TextDecorations == TextDecorations.Strikethrough);
        Assert.Contains(spans, span => span.Text == "code" && span.FontFamily == "monospace");
    }

    [Fact]
    public void Headings_ScaleFromTheBodyFontSizeAndExposeTheirLevel()
    {
        var view = Themed();
        view.SetMarkdown("# 1\n## 2\n### 3\n#### 4\n##### 5\n###### 6\n\nbody");

        var headings = view.BlockViews.Take(6).Cast<Label>().ToArray();
        var body = (Label)view.BlockViews[6]!;
        for (var level = 1; level <= 6; level++)
        {
            Assert.Equal((SemanticHeadingLevel)level, SemanticProperties.GetHeadingLevel(headings[level - 1]));
            Assert.Equal(FontAttributes.Bold, headings[level - 1].FontAttributes);
        }

        Assert.True(headings[0].FontSize > headings[1].FontSize);
        Assert.True(headings[1].FontSize > headings[2].FontSize);
        Assert.True(headings[2].FontSize > body.FontSize);
        Assert.Equal(view.FontSize, body.FontSize);
        Assert.Equal(SemanticHeadingLevel.None, SemanticProperties.GetHeadingLevel(body));
    }

    [Fact]
    public void IncrementalUpdate_KeepsUnchangedBlocksAndUpdatesChangedOnesInPlace()
    {
        var view = new MarkdownView();
        view.SetMarkdown("# Title\n\nFirst paragraph.\n\nSecond");
        var first = view.BlockViews.ToArray();

        view.SetMarkdown("# Title\n\nFirst paragraph.\n\nSecond paragraph grows");

        Assert.Equal(first, view.BlockViews);
        Assert.Equal("Second paragraph grows", ((Label)view.BlockViews[2]!).Text);
        AssertLayoutMatchesBlocks(view);

        view.SetMarkdown("# Title\n\nFirst paragraph.\n\nSecond paragraph grows\n\n- new block");

        Assert.Equal(4, view.BlockViews.Count);
        Assert.Equal(first, view.BlockViews.Take(3));
        Assert.IsAssignableFrom<Grid>(view.BlockViews[3]);
        AssertLayoutMatchesBlocks(view);

        // A paragraph changed anywhere keeps its label; a block that becomes another kind of block gets new views.
        var third = view.BlockViews.ToArray();
        view.SetMarkdown("# Title\n\nChanged paragraph.\n\n## Second paragraph grows\n\n- new block");

        Assert.Same(third[0], view.BlockViews[0]);
        Assert.Same(third[1], view.BlockViews[1]);
        Assert.Equal("Changed paragraph.", ((Label)view.BlockViews[1]!).Text);
        Assert.NotSame(third[2], view.BlockViews[2]);
        Assert.Same(third[3], view.BlockViews[3]);
        AssertLayoutMatchesBlocks(view);

        var fourth = view.BlockViews.ToArray();
        view.SetMarkdown("# Title");

        Assert.Same(fourth[0], Assert.Single(view.BlockViews));
        AssertLayoutMatchesBlocks(view);
    }

    [Fact]
    public void SetMarkdown_UnchangedText_DoesNoWork()
    {
        var view = new MarkdownView();
        view.SetMarkdown("Hello **world**");
        var document = view.Document;
        var block = view.BlockViews[0];

        view.SetMarkdown("Hello **world**");

        Assert.Same(document, view.Document);
        Assert.Same(block, view.BlockViews[0]);
    }

    [Fact]
    public void SetMarkdown_Empty_ClearsTheView()
    {
        var view = new MarkdownView();
        view.SetMarkdown("one\n\ntwo");

        view.SetMarkdown(null);

        Assert.Empty(view.BlockViews);
        Assert.Empty(Blocks(view));
    }

    [Fact]
    public void SetDocument_EqualNodesKeepTheirViews_AndChangedOnesUpdateInPlace()
    {
        var view = new MarkdownView();
        view.SetDocument([Paragraph("one"), new CodeBlockNode("x", "cs"), Paragraph("three")]);
        var first = view.BlockViews.ToArray();

        view.SetDocument([Paragraph("one"), new CodeBlockNode("x", "cs"), Paragraph("three!")]);

        Assert.Equal(first, view.BlockViews);
        Assert.Equal("three!", ((Label)view.BlockViews[2]!).Text);
        Assert.Null(view.Markdown);

        // The language caption follows the info string as it streams in.
        view.SetDocument([Paragraph("one"), new CodeBlockNode("x", "csharp"), Paragraph("three!")]);

        Assert.Equal(first, view.BlockViews);
        Assert.Equal(["csharp", "x"], Descendants(view.BlockViews[1]!).OfType<Label>().Select(label => label.Text));

        // A code block that gains or loses its caption, or a node of another kind, gets new views.
        view.SetDocument([Paragraph("one"), new CodeBlockNode("x"), new CodeBlockNode("three!")]);

        Assert.Same(first[0], view.BlockViews[0]);
        Assert.NotSame(first[1], view.BlockViews[1]);
        Assert.NotSame(first[2], view.BlockViews[2]);
        AssertLayoutMatchesBlocks(view);
    }

    [Fact]
    public void SetDocument_SameListAppendedTo_RendersTheNewNodes()
    {
        var nodes = new List<RichTextNode> { Paragraph("one") };
        var view = new MarkdownView();
        view.SetDocument(nodes);
        var first = view.BlockViews[0];

        nodes.Add(Paragraph("two"));
        view.SetDocument(nodes);

        Assert.Equal(2, view.BlockViews.Count);
        Assert.Same(first, view.BlockViews[0]);
        AssertLayoutMatchesBlocks(view);
    }

    [Fact]
    public void SetDocument_ChangedDefinition_RebuildsReferencingBlocks()
    {
        var reference = With(new LinkReferenceNode { Label = "docs" }, new TextNode("docs"));
        var view = new MarkdownView();
        view.SetDocument([Paragraph(reference)]);
        var unresolved = view.BlockViews[0];
        Assert.Empty(Spans(view).SelectMany(span => span.GestureRecognizers));

        view.SetDocument([Paragraph(reference), new DefinitionNode { Label = "DOCS", Url = "https://example.com" }]);

        Assert.NotSame(unresolved, view.BlockViews[0]);
        Assert.Null(view.BlockViews[1]);
        Assert.Single(Blocks(view));
        Assert.Single(Spans(view).SelectMany(span => span.GestureRecognizers));
    }

    [Fact]
    public void StreamingAnAnswer_RebuildsOnlyTheTailAndMatchesAFreshRender()
    {
        var view = new MarkdownView();
        var answer = MarkdownStreamingTests.LlmAnswer;
        var rebuilt = 0;
        IReadOnlyList<View?> previous = [];
        for (var length = 1; length <= answer.Length; length++)
        {
            view.SetMarkdown(answer[..length]);

            var kept = 0;
            while (kept < previous.Count && kept < view.BlockViews.Count && ReferenceEquals(previous[kept], view.BlockViews[kept]))
                kept++;
            Assert.True(kept >= previous.Count - 2, $"Rebuilt too much at length {length}.");
            rebuilt += view.BlockViews.Count - kept;
            previous = view.BlockViews.ToArray();
        }

        // Without reuse every update would rebuild every block.
        Assert.True(rebuilt < answer.Length * 3, $"Rebuilt {rebuilt} block views.");

        var fresh = new MarkdownView();
        fresh.SetMarkdown(answer);
        Assert.Equal(
            fresh.BlockViews.Select(block => block?.GetType()),
            view.BlockViews.Select(block => block?.GetType()));
        Assert.Equal(Blocks(fresh).Count, Blocks(view).Count);
    }

    [Theory]
    [InlineData("[docs](https://example.com/a)", "https://example.com/a")]
    [InlineData("[mail](mailto:team@example.com)", "mailto:team@example.com")]
    [InlineData("<https://example.com/auto>", "https://example.com/auto")]
    [InlineData("see https://example.com/bare.", "https://example.com/bare")]
    [InlineData("[**bold** link](http://example.com)", "http://example.com/")]
    public async Task SafeLinks_AreTappableAndOpen(string markdown, string expected)
    {
        var opened = new List<Uri>();
        var view = new MarkdownView
        {
            LinkLauncher = uri =>
            {
                opened.Add(uri);
                return Task.FromResult(true);
            },
        };
        view.SetMarkdown(markdown);

        var taps = Spans(view).SelectMany(span => span.GestureRecognizers).OfType<TapGestureRecognizer>().ToList();
        Assert.NotEmpty(taps);
        Assert.NotNull(taps[0].Command);
        taps[0].Command!.Execute(taps[0].CommandParameter);

        Assert.Equal(new Uri(expected), Assert.Single(opened));
        Assert.True(await view.OpenLinkAsync(expected));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/file")]
    [InlineData("/relative/path")]
    [InlineData("tel:+15555550123")]
    [InlineData("data:text/html,hi")]
    public async Task UnsafeLinkSchemes_AreNeitherTappableNorOpened(string url)
    {
        var opened = new List<Uri>();
        var view = new MarkdownView
        {
            LinkLauncher = uri =>
            {
                opened.Add(uri);
                return Task.FromResult(true);
            },
        };
        view.SetMarkdown($"[click me]({url})");

        Assert.Contains(Spans(view), span => span.Text == "click me");
        Assert.Empty(Spans(view).SelectMany(span => span.GestureRecognizers));
        Assert.False(await view.OpenLinkAsync(url));
        Assert.Empty(opened);
    }

    [Fact]
    public async Task OpenLink_LauncherFailures_AreSwallowed()
    {
        var view = new MarkdownView { LinkLauncher = _ => throw new InvalidOperationException("no browser") };
        Assert.False(await view.OpenLinkAsync("https://example.com"));

        view.LinkLauncher = _ => Task.FromException<bool>(new InvalidOperationException("async failure"));
        Assert.False(await view.OpenLinkAsync("https://example.com"));

        // The default launcher has no platform implementation in unit tests and must not throw.
        view.LinkLauncher = null;
        Assert.False(await view.OpenLinkAsync("https://example.com"));
        Assert.False(await view.OpenLinkAsync(null));
    }

    [Fact]
    public void CodeBlocks_UseAHorizontalScrollViewAndMonospaceText()
    {
        var view = Themed();
        view.SetMarkdown("```python\nprint('a very long line that must not wrap')\n```\n\n```\nno language\n```");

        var withLanguage = Assert.IsAssignableFrom<Border>(view.BlockViews[0]);
        var stack = Assert.IsAssignableFrom<VerticalStackLayout>(withLanguage.Content);
        Assert.Equal("python", Assert.IsAssignableFrom<Label>(stack.Children[0]).Text);
        var scroller = Assert.IsAssignableFrom<ScrollView>(stack.Children[1]);
        Assert.Equal(ScrollOrientation.Horizontal, scroller.Orientation);
        var code = Assert.IsAssignableFrom<Label>(scroller.Content);
        Assert.Equal("print('a very long line that must not wrap')", code.Text);
        Assert.Equal(LineBreakMode.NoWrap, code.LineBreakMode);
        Assert.Equal("monospace", code.FontFamily);

        var withoutLanguage = Assert.IsAssignableFrom<Border>(view.BlockViews[1]);
        var plainScroller = Assert.IsAssignableFrom<ScrollView>(withoutLanguage.Content);
        Assert.Equal(ScrollOrientation.Horizontal, plainScroller.Orientation);
        Assert.IsType<RoundRectangle>(withoutLanguage.StrokeShape);
        Assert.All([scroller, plainScroller], s => Assert.Equal(ScrollBarVisibility.Default, s.HorizontalScrollBarVisibility));
    }

    [Theory]
    [InlineData("Android", ScrollBarVisibility.Never)]
    [InlineData("iOS", ScrollBarVisibility.Default)]
    [InlineData("MacCatalyst", ScrollBarVisibility.Default)]
    [InlineData("WinUI", ScrollBarVisibility.Default)]
    [InlineData("Unknown", ScrollBarVisibility.Default)]
    public void SidewaysScrollBars_AreHiddenOnlyOnAndroid(string platform, ScrollBarVisibility expected)
    {
        // Android flashes a new scroller's bar across the last line of code; a mouse needs the bar elsewhere.
        Assert.Equal(expected, MarkdownRenderer.GetSideScrollBarVisibility(DevicePlatform.Create(platform)));
    }

    [Theory]
    [InlineData("iOS", "Menlo")]
    [InlineData("MacCatalyst", "Menlo")]
    [InlineData("macOS", "Menlo")]
    [InlineData("Android", "monospace")]
    [InlineData("WinUI", "Consolas")]
    [InlineData("Unknown", "monospace")]
    public void CodeFont_DependsOnThePlatform(string platform, string expected)
    {
        var devicePlatform = platform switch
        {
            "iOS" => DevicePlatform.iOS,
            "MacCatalyst" => DevicePlatform.MacCatalyst,
            "macOS" => DevicePlatform.macOS,
            "Android" => DevicePlatform.Android,
            "WinUI" => DevicePlatform.WinUI,
            _ => DevicePlatform.Unknown,
        };

        Assert.Equal(expected, AIChatTheme.GetCodeFontFamily(devicePlatform));
    }

    [Fact]
    public void Lists_RenderMarkersWithNesting()
    {
        var view = new MarkdownView();
        view.SetMarkdown("3. three\n4. four\n   - nested\n     - deeper\n\n- [x] done\n- [ ] todo");

        var ordered = Assert.IsAssignableFrom<Grid>(view.BlockViews[0]);
        var markers = ordered.Children.OfType<Label>().Where(label => Grid.GetColumn(label) == 0).Select(label => label.Text).ToArray();
        Assert.Equal(["3.", "4."], markers);

        var nestedMarkers = Labels(view.BlockViews[0]!).Where(label => label.Text is "•" or "◦" or "▪").Select(label => label.Text).ToArray();
        Assert.Equal(["◦", "▪"], nestedMarkers);

        var tasks = Assert.IsAssignableFrom<Grid>(view.BlockViews[1]);
        var taskMarkers = tasks.Children.OfType<Label>().Where(label => Grid.GetColumn(label) == 0).Select(label => label.Text).ToArray();
        Assert.Equal(["\u2611\uFE0E", "\u2610\uFE0E"], taskMarkers);
    }

    [Fact]
    public void Tables_UseAGridWithLinesAlignmentAndAHeaderStyle()
    {
        var view = Themed();
        view.SetMarkdown("| Left | Center | Right |\n|:--|:-:|--:|\n| a | **b** | c |\n| d | e |");

        var scroller = Assert.IsAssignableFrom<ScrollView>(view.BlockViews[0]);
        Assert.Equal(ScrollOrientation.Horizontal, scroller.Orientation);
        Assert.Equal(ScrollBarVisibility.Default, scroller.HorizontalScrollBarVisibility);
        var grid = Assert.IsAssignableFrom<Grid>(Assert.IsAssignableFrom<Border>(scroller.Content).Content);
        Assert.Equal(5, grid.ColumnDefinitions.Count);
        Assert.Equal(5, grid.RowDefinitions.Count);
        Assert.Equal(4, grid.Children.OfType<BoxView>().Count());

        var cells = grid.Children.OfType<Label>().ToArray();
        Assert.Equal(9, cells.Length);
        Assert.All(cells.Take(3), header => Assert.Equal(FontAttributes.Bold, header.FontAttributes));
        Assert.Equal(FontAttributes.None, cells[3].FontAttributes);
        Assert.Equal([TextAlignment.Start, TextAlignment.Center, TextAlignment.End], cells.Take(3).Select(cell => cell.HorizontalTextAlignment));
        Assert.Contains(cells[4].FormattedText.Spans, span => span.Text == "b" && span.FontAttributes == FontAttributes.Bold);
        Assert.Equal(string.Empty, cells[8].Text);
    }

    [Fact]
    public void QuoteBarsTableLinesAndRules_AskOnlyForTheirThickness()
    {
        var view = Themed();
        view.SetMarkdown("> quote\n\n---\n\n| a | b |\n|---|---|\n| 1 | 2 |");

        // No height request: the bar stretches to the quote's height instead of asking for BoxView's default 40.
        var bar = (BoxView)((Grid)view.BlockViews[0]!).Children[0];
        Assert.Equal(-1, bar.HeightRequest);
        Assert.Equal(LayoutOptions.Fill, bar.VerticalOptions);
        Assert.Equal(new Size(3, 0), ((IView)bar).Measure(100, 100));

        var rule = Assert.IsAssignableFrom<BoxView>(view.BlockViews[1]);
        Assert.Equal(new Size(0, 1 + 8), ((IView)rule).Measure(500, 500));

        // Grid lines fill their track across and do not widen the auto rows and columns they cross.
        var table = Assert.IsAssignableFrom<Border>(Assert.IsAssignableFrom<ScrollView>(view.BlockViews[2]).Content);
        var lines = Assert.IsAssignableFrom<Grid>(table.Content).Children.OfType<BoxView>().ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Equal(new Size(0, 1), ((IView)lines.Single(line => Grid.GetColumnSpan(line) > 1)).Measure(500, 500));
        Assert.Equal(new Size(1, 0), ((IView)lines.Single(line => Grid.GetRowSpan(line) > 1)).Measure(500, 500));
    }

    [Fact]
    public void Images_LoadOnlyHttpSourcesWhenAllowedAndDescribeThemselves()
    {
        var requests = new List<Uri>();
        var view = Themed();
        view.ImageLoader = TestImages.Loader(TestImages.Png(800, 600), requests);
        view.AllowRemoteImages = true;
        view.SetMarkdown("![Diagram](https://example.com/d.png \"Title\")\n\n![Local](file:///tmp/x.png)\n\nText ![inline](https://example.com/i.png) after");

        var image = Assert.IsType<ChatImage>(Assert.IsType<LinkedImageView>(view.BlockViews[0]).Image);
        Assert.Equal(new Size(800, 600), image.PixelSize);
        Assert.Equal("Diagram", SemanticProperties.GetDescription(image));
        Assert.Equal(320, image.MaximumHeightRequest);
        Assert.Equal([new Uri("https://example.com/d.png"), new Uri("https://example.com/i.png")], requests);

        // Anything else is a chip with the description, never an empty gap.
        Assert.Equal("Local, Image \u00B7 x.png", SemanticProperties.GetDescription(Assert.IsAssignableFrom<Border>(view.BlockViews[1])));

        var mixed = Assert.IsAssignableFrom<VerticalStackLayout>(view.BlockViews[2]);
        Assert.Collection(
            mixed.Children,
            child => Assert.Equal("Text", ((Label)child).Text),
            child => Assert.Equal("inline", SemanticProperties.GetDescription(((LinkedImageView)child).Image)),
            child => Assert.Equal("after", ((Label)child).Text));
    }

    [Fact]
    public void TextBesideAnImage_DropsTheLineBreaksAndSpacesNextToIt()
    {
        // An image on its own line splits the paragraph; the line breaks around it would be blank lines.
        var view = new MarkdownView();
        view.SetMarkdown(
            "Here is the chart:\n![chart](https://example.com/c.png)\nAs you can see, it **works**.\n\n" +
            "- Second ![pic](https://example.com/p.png)\n  continued\n\n" +
            "Text ![a](https://example.com/a.png)  ![b](https://example.com/b.png) after");

        Assert.Equal(["Here is the chart:", "chart", "As you can see, it works."], Parts(view.BlockViews[0]!));
        var item = Assert.IsAssignableFrom<Grid>(view.BlockViews[1]).Children.OfType<VerticalStackLayout>().Single();
        Assert.Equal(["Second", "pic", "continued"], Parts(item));
        Assert.Equal(["Text", "a", "b", "after"], Parts(view.BlockViews[2]!));

        static IEnumerable<string?> Parts(IView parts) =>
            ((VerticalStackLayout)parts).Children.Select(part => part switch
            {
                Label { FormattedText: { } formatted } => string.Concat(formatted.Spans.Select(span => span.Text)),
                Label label => label.Text,
                _ => SemanticProperties.GetDescription((BindableObject)part)?.Split(',')[0],
            });
    }

    [Fact]
    public void DeeplyNestedMarkdown_RendersABoundedViewTreeWithAllItsText()
    {
        var view = new MarkdownView();
        view.SetMarkdown(new string('>', 120) + " **deep *text*** " + string.Concat(Enumerable.Repeat("- ", 3)) + "end");

        // Within Markdig's nesting limit, so the structure is mapped rather than flattened.
        Assert.IsType<BlockQuoteNode>(Assert.Single(view.Document));
        Assert.InRange(MaxElementDepth(view), 1, 80);
        var text = string.Concat(Labels(view).Select(label =>
            label.Text + string.Concat(label.FormattedText?.Spans.Select(span => span.Text) ?? [])));
        Assert.Contains("deep text", text, StringComparison.Ordinal);
        Assert.Contains("end", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Accessibility_ContainersNeverHideTheirChildren()
    {
        // The answer's web image is allowed (a chip names itself instead, like any chip).
        var view = new MarkdownView { AllowRemoteImages = true, ImageLoader = TestImages.Pending() };
        view.SetMarkdown(MarkdownStreamingTests.LlmAnswer);

        foreach (var element in Descendants(view))
        {
            if (element is not Image)
                Assert.Null(SemanticProperties.GetDescription(element));
        }

        Assert.Equal(
            [SemanticHeadingLevel.Level1, SemanticHeadingLevel.Level2, SemanticHeadingLevel.Level3, SemanticHeadingLevel.Level2],
            Descendants(view).OfType<Label>()
                .Select(SemanticProperties.GetHeadingLevel)
                .Where(level => level != SemanticHeadingLevel.None));
    }

    [Fact]
    public void Styles_UseTheChatPaletteAndFollowIt()
    {
        var host = new ContentView();
        var theme = new AIChatTheme();
        host.Resources.MergedDictionaries.Add(theme);
        var view = new MarkdownView();
        host.Content = view;
        view.SetMarkdown("Text with [a link](https://example.com) and `code`.\n\n```\nblock\n```\n\n> quoted");

        var paragraph = (Label)view.BlockViews[0]!;
        Assert.Same(theme[AIChatThemeKeys.AssistantTextStyle], paragraph.Style);
        Assert.Equal(Color.FromArgb("#16181C"), paragraph.TextColor);

        var link = Spans(view).Single(span => span.Text == "a link");
        Assert.Equal(Color.FromArgb("#2563EB"), link.TextColor);
        Assert.Equal(TextDecorations.Underline, link.TextDecorations);

        var code = Spans(view).Single(span => span.Text == "code");
        Assert.Equal(Color.FromArgb("#F0F2F5"), code.BackgroundColor);
        Assert.Equal("monospace", code.FontFamily);

        var block = (Border)view.BlockViews[1]!;
        Assert.Same(theme[AIChatThemeKeys.CodeBlockStyle], block.Style);
        Assert.Equal(Color.FromArgb("#F0F2F5"), block.BackgroundColor);
        var quoteBar = (BoxView)((Grid)view.BlockViews[2]!).Children[0];
        Assert.Equal(Color.FromArgb("#DEE1E6"), quoteBar.Color);
        Assert.Equal(15d, view.FontSize);

        // Overriding a chat color recolors Markdown too.
        host.Resources[AIChatThemeKeys.AccentColor] = Colors.Red;
        Assert.Equal(Colors.Red, link.TextColor);
        host.Resources.Remove(AIChatThemeKeys.AccentColor);

        // And the dark palette applies to Markdown like to the rest of the chat.
        theme.ApplyTheme(AppTheme.Dark);
        Assert.Equal(Color.FromArgb("#ECEDEF"), paragraph.TextColor);
        Assert.Equal(Color.FromArgb("#4C8DF6"), link.TextColor);
        Assert.Equal(Color.FromArgb("#2A2D33"), code.BackgroundColor);
        Assert.Equal(Color.FromArgb("#3A3E45"), quoteBar.Color);
    }

    [Fact]
    public void Styles_ComeFromDynamicResourcesAndFollowChanges()
    {
        var host = new ContentView();
        var view = new MarkdownView();
        view.SetMarkdown("Text with [a link](https://example.com).\n\n```\nblock\n```\n\n> quote");
        host.Content = view;

        host.Resources[AIChatThemeKeys.LinkSpanStyle] = SpanStyle(Colors.Red);
        host.Resources[AIChatThemeKeys.CodeBlockStyle] = new Style(typeof(Border))
        {
            Setters = { new Setter { Property = VisualElement.BackgroundColorProperty, Value = Colors.Orange } },
        };
        host.Resources[AIChatThemeKeys.MarkdownBlockQuoteBarStyle] = new Style(typeof(BoxView))
        {
            Setters = { new Setter { Property = BoxView.ColorProperty, Value = Colors.Purple } },
        };

        var link = Spans(view).Single(span => span.Text == "a link");
        Assert.Equal(Colors.Red, link.TextColor);
        Assert.Equal(Colors.Orange, ((Border)view.BlockViews[1]!).BackgroundColor);
        Assert.Equal(Colors.Purple, ((BoxView)((Grid)view.BlockViews[2]!).Children[0]).Color);

        host.Resources[AIChatThemeKeys.LinkSpanStyle] = SpanStyle(Colors.Green);

        Assert.Equal(Colors.Green, link.TextColor);

        // Views created after the theme is in scope pick it up too.
        view.SetMarkdown("Text with [a link](https://example.com).\n\n```\nblock\n```\n\n> quote\n\n[another](https://example.com/2)");
        Assert.Equal(Colors.Green, Spans(view).Single(span => span.Text == "another").TextColor);
    }

    [Fact]
    public void FontSizeAndSpacing_ComeFromDynamicResources()
    {
        var host = new ContentView();
        var view = new MarkdownView();
        view.SetMarkdown("## Heading\n\nbody");
        host.Content = view;

        host.Resources[AIChatThemeKeys.BodyFontSize] = 20d;
        host.Resources[AIChatThemeKeys.MarkdownBlockSpacing] = 24d;

        Assert.Equal(20d, view.FontSize);
        Assert.Equal(20d, ((Label)view.BlockViews[1]!).FontSize);
        Assert.Equal(26d, ((Label)view.BlockViews[0]!).FontSize, 3);
        Assert.Equal(24d, ((VerticalStackLayout)view.Content).Spacing);
    }

    [Fact]
    public void MarkdownTheme_DefinesEveryMarkdownKeyWithTheRightType()
    {
        var theme = new AIChatTheme();
        var keys = typeof(AIChatThemeKeys)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && field.Name.StartsWith("Markdown", StringComparison.Ordinal))
            .Select(field => (string)field.GetValue(null)!)
            .ToArray();

        Assert.Equal(12, keys.Length);
        foreach (var key in keys)
        {
            Assert.StartsWith("AIChat.Markdown.", key, StringComparison.Ordinal);

            // Defined by the Markdown dictionary, and reachable through the chat theme that merges it.
            Assert.True(new MarkdownTheme().ContainsKey(key), key);
            Assert.True(theme.TryGetValue(key, out var value), key);
            if (key.EndsWith("Style", StringComparison.Ordinal))
                Assert.IsType<Style>(value);
            else
                Assert.IsType<double>(value);
        }

        Assert.Equal(typeof(Span), ((Style)theme[AIChatThemeKeys.MarkdownInlineCodeStyle]).TargetType);
        Assert.Equal(typeof(Border), ((Style)theme[AIChatThemeKeys.MarkdownTableBorderStyle]).TargetType);
        Assert.Equal(typeof(BoxView), ((Style)theme[AIChatThemeKeys.MarkdownThematicBreakStyle]).TargetType);
        Assert.Equal(typeof(Label), ((Style)theme[AIChatThemeKeys.MarkdownHeadingStyle]).TargetType);
    }

    private static MarkdownView Themed()
    {
        var view = new MarkdownView();
        view.Resources.MergedDictionaries.Add(new AIChatTheme());
        return view;
    }

    private static Style SpanStyle(Color color) => new(typeof(Span))
    {
        Setters = { new Setter { Property = Span.TextColorProperty, Value = color } },
    };

    private static IList<IView> Blocks(MarkdownView view) => ((VerticalStackLayout)view.Content).Children;

    private static void AssertLayoutMatchesBlocks(MarkdownView view) =>
        Assert.Equal(view.BlockViews.OfType<IView>(), Blocks(view));

    private static IEnumerable<Element> Descendants(Element root)
    {
        var pending = new Stack<Element>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var element = pending.Pop();
            yield return element;
            foreach (var child in ((IVisualTreeElement)element).GetVisualChildren().OfType<Element>().Reverse())
                pending.Push(child);
        }
    }

    private static IEnumerable<Label> Labels(Element root) => Descendants(root).OfType<Label>();

    private static int MaxElementDepth(Element root)
    {
        var max = 0;
        var pending = new Stack<(Element Element, int Depth)>();
        pending.Push((root, 1));
        while (pending.Count > 0)
        {
            var (element, depth) = pending.Pop();
            max = Math.Max(max, depth);
            foreach (var child in ((IVisualTreeElement)element).GetVisualChildren().OfType<Element>())
                pending.Push((child, depth + 1));
        }

        return max;
    }

    private static IEnumerable<Span> Spans(Element root) =>
        Labels(root).SelectMany(label => label.FormattedText?.Spans ?? []);
}
