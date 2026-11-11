using System.Globalization;
using System.Reflection;
using Microsoft.Maui.Controls.Shapes;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

/// <summary>
/// The view's own parts render the same whatever implicit styles the app defines (the default MAUI template's, or any
/// others), in light and dark, enabled and disabled. Content the app supplies keeps the app's styles.
/// </summary>
public sealed class StyleIsolationTests : IDisposable
{
    private const string AppFont = "OpenSansRegular";

    // The types MergedStyle stops at: implicit styles for these never apply.
    private static readonly Type[] StyleRoots = [typeof(View), typeof(VisualElement), typeof(NavigableElement), typeof(Element), typeof(object)];

    private static readonly Scenario[] Scenarios =
    [
        new("Welcome", WelcomeAsync),
        new("Composing", ComposingAsync),
        new("History", HistoryAsync),
        new("AwaitingInput", AwaitingInputAsync),
        new("Streaming", StreamingAsync),
        new("RichText", RichTextAsync),
        new("Markdown", MarkdownAsync, ShowLostImage),
    ];

    public StyleIsolationTests() => Application.Current = null;

    public enum Placement
    {
        /// <summary>In the application resources before the view is created.</summary>
        Application,

        /// <summary>Added to the application resources once the view and its rows are shown.</summary>
        ApplicationLater,

        /// <summary>Added to the page resources once the view and its rows are shown.</summary>
        PageLater,

        /// <summary>The view is created before there is an application, then shown in one that has the styles.</summary>
        NoApplicationYet,
    }

    public static TheoryData<bool, Placement> AnyStyles => new()
    {
        { false, Placement.Application },
        { true, Placement.Application },
        { false, Placement.ApplicationLater },
        { true, Placement.ApplicationLater },
        { false, Placement.PageLater },
        { true, Placement.PageLater },
        { false, Placement.NoApplicationYet },
        { true, Placement.NoApplicationYet },
    };

    public void Dispose() => Application.Current = null;

    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public void DefaultTemplateStyles_DoNotChangeTheBuiltInParts(AppTheme theme) => UIThread.Run(async () =>
    {
        foreach (var scenario in Scenarios)
        {
            var expected = await LookAsync(scenario, theme, Placement.Application, styles: null);
            var actual = await LookAsync(scenario, theme, Placement.Application, AddTemplateStyles);
            AssertSameLook($"{scenario} ({theme})", expected, actual);
        }
    });

    [Theory]
    [MemberData(nameof(AnyStyles))]
    public void AnyImplicitStyles_DoNotChangeTheBuiltInParts(bool applyToDerivedTypes, Placement placement) => UIThread.Run(async () =>
    {
        foreach (var scenario in Scenarios)
        {
            var expected = await LookAsync(scenario, AppTheme.Light, placement, styles: null);
            var garish = GarishStyles(expected.SelectMany(look => StyleTargets(look.Type)), applyToDerivedTypes);
            var actual = await LookAsync(scenario, AppTheme.Light, placement, resources => Add(resources, garish));
            AssertSameLook($"{scenario} ({placement}, ApplyToDerivedTypes={applyToDerivedTypes})", expected, actual);
        }
    });

    [Fact]
    public void AppContent_KeepsTheAppsImplicitStyles() => UIThread.Run(async () =>
    {
        var app = CreateApplication(AppTheme.Light);
        Add(app.Resources, GarishStyles([typeof(Border), typeof(VerticalStackLayout), typeof(Label), typeof(Button)], applyToDerivedTypes: false));
        var client = new ScriptedChatClient()
            .RequestApproval("get_weather", new Dictionary<string, object?> { ["city"] = "Rome" })
            .Reply("Sunny in Rome.");
        var view = ChatHost.Create(ChatHost.Session(client), v =>
        {
            v.HeaderTemplate = new DataTemplate(() => AppPart("header"));
            v.MessagesHeaderTemplate = new DataTemplate(() => AppPart("messages header"));
            v.ResponseFooterTemplate = new DataTemplate(() => AppPart("footer"));
            v.InputHeaderContent = AppPart("input header");
            v.InputLeadingContent = AppPart("leading");
            v.InputTrailingContent = AppPart("trailing");
            v.ContentTemplates.Add(new BlockTemplate { BlockType = typeof(TextContentBlock), Template = new DataTemplate(() => AppPart("text")) });
            v.ContentTemplates.Add(new BlockTemplate
            {
                ToolName = "get_weather",
                BlockType = typeof(FunctionInvocationContentBlock),
                Template = new DataTemplate(() => AppPart("weather")),
            });
        });
        view.Window!.Parent = app;

        view.Text = "Weather in Rome?";
        var send = view.SendAsync();
        await UIThread.Until(() => view.Session!.Status == ConversationStatus.AwaitingInput);
        var approval = view.Rows().OfType<BlockRow>().Single(row => row.Block is ToolApprovalBlock);
        ((ToolApprovalView)((BlockHost)view.Realize(approval)).HostedView!).ApproveCommand.Execute(null);
        await send;

        var rows = view.Rows().Select(row => view.Realize(row)).ToList();
        var parts = Tree(view).Concat(rows.SelectMany(Tree));
        if (view.MessageList.Header is Element header)
            parts = parts.Concat(Tree(header));
        var appElements = parts.OfType<VisualElement>().Where(element => element.StyleId == "App").Distinct().ToList();

        // The header, messages header, input slots, response footer, text template, and the tool's view inside the
        // approval card.
        Assert.Equal(
            ["footer", "header", "input header", "leading", "messages header", "text", "trailing", "weather"],
            appElements.OfType<Label>().Select(label => label.Text).Distinct().Order());

        // The tool's view is disabled once the approval is decided, so it shows the app's Disabled state.
        Assert.All(appElements, element => Assert.Equal(element.IsEnabled ? Colors.Magenta : Colors.DarkGray, element.BackgroundColor));
        Assert.Contains(appElements, element => !element.IsEnabled);
        Assert.All(appElements.OfType<Label>(), label => Assert.Equal("GarishSans", label.FontFamily));
        Assert.All(appElements.OfType<Button>(), button => Assert.Equal(61, button.MinimumWidthRequest));
        Assert.All(appElements.OfType<Border>(), border => Assert.Equal(6, border.StrokeThickness));
    });

    [Fact]
    public void AppControlTemplate_KeepsTheAppsImplicitStyles()
    {
        var app = CreateApplication(AppTheme.Light);
        Add(app.Resources, GarishStyles([typeof(Grid), typeof(Button), typeof(Editor)], applyToDerivedTypes: false));

        var view = ChatHost.Create(v => v.ControlTemplate = new ControlTemplate(() => new Grid
        {
            new Button { StyleId = "App" },
            new Editor { StyleId = "App" },
        }));
        view.Window!.Parent = app;

        var elements = Tree(view).OfType<VisualElement>().Where(element => element.StyleId == "App").ToList();
        Assert.Equal(2, elements.Count);
        Assert.All(elements, element => Assert.Equal(Colors.Magenta, element.BackgroundColor));
        Assert.Equal(Colors.Magenta, Assert.IsType<Grid>(Tree(view).ElementAt(1)).BackgroundColor);
    }

    [Fact]
    public void FontFamilyKey_SetsTheFontOfEveryBuiltInText() => UIThread.Run(async () =>
    {
        foreach (var scenario in Scenarios)
        {
            await using var rendering = await RenderAsync(
                scenario,
                AppTheme.Light,
                Placement.Application,
                resources => resources[AIChatThemeKeys.FontFamily] = AppFont);
            var codeStyle = Application.Current!.Resources[AIChatThemeKeys.CodeTextStyle];
            var codeFont = Application.Current.Resources[AIChatThemeKeys.CodeFontFamily];

            foreach (var element in rendering.Parts())
            {
                var font = element.GetType().GetField("FontFamilyProperty", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (font is null)
                    continue;

                var family = element.GetValue((BindableProperty)font.GetValue(null)!);
                var description = $"{scenario}: {Describe(element, 0).Element}";
                if (element is Span span)
                    Assert.True(family is null || Equals(family, codeFont), $"{description} uses '{family}'; spans inherit their label's font.");
                else if (element is Label { Style: { } style } && ReferenceEquals(style, codeStyle))
                    Assert.True(Equals(codeFont, family), $"{description} is code but uses '{family}'.");
                else
                    Assert.True(Equals(AppFont, family), $"{description} uses '{family}' instead of AIChat.FontFamily.");
            }
        }
    });

    [Fact]
    public void BuiltInParts_AreMadeOfTheIsolatedElementTypes() => UIThread.Run(async () =>
    {
        foreach (var scenario in Scenarios)
        {
            await using var rendering = await RenderAsync(scenario, AppTheme.Light, Placement.Application, styles: null);

            // Border outlines are RoundRectangles, which can't be derived from: the theme sets what shapes the outline.
            var stray = rendering.Parts()
                .Where(element => !IsOutline(element))
                .Select(element => element.GetType())
                .Where(type => !ChatElements.Types.Contains(type))
                .Distinct()
                .ToList();

            Assert.True(
                stray.Count == 0,
                $"{scenario}: {string.Join(", ", stray.Select(type => type.Name))} would take the app's implicit styles. " +
                "Create built-in parts from the types in ChatElements.");
        }
    });

    [Fact]
    public void EveryInternalElementType_HasAnEmptyImplicitStyleInTheTheme()
    {
        var theme = new AIChatTheme();
        var types = typeof(AIChatView).Assembly.GetTypes()
            .Where(type => !type.IsVisible && !type.IsAbstract && !type.ContainsGenericParameters)
            .Where(type => typeof(StyleableElement).IsAssignableFrom(type) || typeof(Span).IsAssignableFrom(type))
            .ToList();

        Assert.NotEmpty(types);
        Assert.All(types, type =>
        {
            Assert.Contains(type, ChatElements.Types);
            var style = Assert.IsType<Style>(theme[type.FullName!]);
            Assert.Equal(type, style.TargetType);
            Assert.Empty(style.Setters);
        });
    }

    [Fact]
    public void EveryInternalElementType_IgnoresTheAppsStylesFromConstruction()
    {
        // Elements look their implicit styles up while they are constructed, so the application must have the theme by
        // then, even when the view was created before it: MAUI leaves the visual-state setters of a style applied at
        // construction behind when the element's own style replaces it.
        var owner = new AIChatView();

        foreach (var type in ChatElements.Types)
        {
            CreateApplication(AppTheme.Light);
            var expected = Describe(Create(type, owner), 0);
            var app = CreateApplication(AppTheme.Light);
            Add(app.Resources, GarishStyles(StyleTargets(type), applyToDerivedTypes: true));
            var actual = Describe(Create(type, owner), 0);

            AssertSameLook(type.Name, [expected], [actual]);
        }

        static Element Create(Type type, AIChatView owner) =>
            type == typeof(ChatMessageList) ? new ChatMessageList(owner) :
            type == typeof(BlockHost) ? new BlockHost(new BlockTemplate()) :
            // Standard controls as arguments: they don't load the theme, so the view under test has to.
            type == typeof(LinkedImageView) ? new LinkedImageView(new Image(), static () => new ContentView()) :
            (Element)Activator.CreateInstance(type, nonPublic: true)!;
    }

    [Fact]
    public void ReasoningHeader_IsStyledByItsThemeKey()
    {
        var app = CreateApplication(AppTheme.Light);
        var view = ChatHost.Create();
        view.Window!.Parent = app;
        var reasoning = new ReasoningView();
        view.InputHeaderContent = reasoning;
        BlockView.Bind(reasoning, Blocks.Reasoning("Because."), owner: null, session: null);

        Assert.Same(app.Resources[AIChatThemeKeys.ReasoningHeaderStyle], reasoning.Header.Style);
    }

    private static void AddTemplateStyles(ResourceDictionary resources)
    {
        // The styles look their colors up in the application resources, as in App.xaml.
        resources.MergedDictionaries.Add(new TemplateColors());
        resources.MergedDictionaries.Add(new TemplateStyles());
    }

    private static Border AppPart(string text) =>
        new()
        {
            StyleId = "App",
            Content = new VerticalStackLayout
            {
                StyleId = "App",
                Children =
                {
                    new Label { Text = text, StyleId = "App" },
                    new Button { Text = text, StyleId = "App" },
                },
            },
        };

    private static Application CreateApplication(AppTheme theme)
    {
        var app = new Application { UserAppTheme = theme };
        Application.Current = app;
        return app;
    }

    private static void Add(ResourceDictionary resources, IEnumerable<Style> styles)
    {
        foreach (var style in styles)
            resources.Add(style);
    }

    // ── Rendering ───────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<Look>> LookAsync(
        Scenario scenario,
        AppTheme theme,
        Placement placement,
        Action<ResourceDictionary>? styles)
    {
        await using var rendering = await RenderAsync(scenario, theme, placement, styles);
        var enabled = Describe(rendering.Parts());
        rendering.View.IsEnabled = false;
        return [.. enabled, .. Describe(rendering.Parts())];
    }

    private static async Task<Rendering> RenderAsync(
        Scenario scenario,
        AppTheme theme,
        Placement placement,
        Action<ResourceDictionary>? styles)
    {
        Application.Current = null;
        Application? app = null;
        if (placement != Placement.NoApplicationYet)
        {
            app = CreateApplication(theme);
            if (placement == Placement.Application)
                styles?.Invoke(app.Resources);
        }

        var (view, cleanUp) = await scenario.Render();
        if (app is null)
        {
            app = CreateApplication(theme);
            styles?.Invoke(app.Resources);
        }

        view.Window!.Parent = app;
        var rows = view.Rows().Select(row => view.Realize(row)).ToList();
        scenario.Shown?.Invoke(rows.SelectMany(Tree).ToList());

        if (placement == Placement.ApplicationLater)
            styles?.Invoke(app.Resources);
        else if (placement == Placement.PageLater)
            styles?.Invoke(((Page)view.Parent).Resources);

        return new Rendering(view, rows, cleanUp);
    }

    private static IEnumerable<Element> Tree(Element root)
    {
        foreach (var element in root.GetVisualTreeDescendants().OfType<Element>())
        {
            yield return element;
            if (element is Label { FormattedText: { } text })
            {
                foreach (var span in text.Spans)
                    yield return span;
            }
        }
    }

    // ── Scenarios ───────────────────────────────────────────────────────────────────────────────────────────

    private const string Markdown =
        "# Paris\n\nTake the **9:15** train, read [the guide](https://example.com/guide) and keep `TGV 6201`.\n\n" +
        "- Louvre\n- Orsay\n\n> Book early.\n\n```\nseat: 12A\n```\n\n| Day | Plan |\n|---|---|\n| 1 | Louvre |";

    private const string MarkdownAnswer =
        "# Trip to Paris\n\n" +
        "## Day one\n\n" +
        "Take the **9:15** train, *read* [the guide](https://example.com/guide), keep `TGV 6201` and ~~rush~~ relax.\n\n" +
        "1. Louvre\n   - Mona Lisa\n   - Venus de Milo\n2. Orsay\n\n" +
        "- [x] Passport\n- [ ] Adapter\n\n" +
        "> Book early: trains fill up.\n\n" +
        "```csharp\nvar seat = \"12A\";\n```\n\n" +
        "| Day | Plan | Cost |\n|:---|:---:|---:|\n| 1 | **Louvre** | 17 € |\n| 2 | Orsay | 16 € |\n\n" +
        "---\n\n" +
        "![Eiffel Tower](https://example.com/eiffel.png)\n\n" +
        "![Lost photo](https://example.com/lost.png)\n\n" +
        "![Seat map](attachment://seat-map.png)";

    /// <summary>No messages yet: the welcome text, suggestions, and an empty message box with every button.</summary>
    private static Task<(AIChatView, Func<Task>)> WelcomeAsync()
    {
        var view = ChatHost.Create(ChatHost.Session(new ScriptedChatClient()), v =>
        {
            v.Suggestions = ["Plan a trip", "Summarize a document"];
            v.AllowAttachments = true;
            v.AllowAudioCapture = true;
            v.AudioRecorder = new FakeAudioRecorder();
            v.AllowLiveSpeech = true;
            v.SpeechRecognizer = new FakeSpeechRecognizer();
        });
        return Task.FromResult<(AIChatView, Func<Task>)>((view, static () => Task.CompletedTask));
    }

    /// <summary>A draft with staged attachments and an attachment error.</summary>
    private static Task<(AIChatView, Func<Task>)> ComposingAsync()
    {
        var view = ChatHost.Create(ChatHost.Session(new ScriptedChatClient()), v =>
        {
            v.AllowAttachments = true;
            v.MaximumAttachmentBytes = 4096;
        });
        view.AddAttachment(Blocks.Pdf("report.pdf"));
        view.AddAttachment(FakeAudioRecorder.Wav());
        view.AddAttachment(Blocks.Png("photo.png"));
        view.AddAttachment(Blocks.Pdf("huge.pdf", bytes: 8192));
        view.Text = "Summarize these";
        return Task.FromResult<(AIChatView, Func<Task>)>((view, static () => Task.CompletedTask));
    }

    /// <summary>
    /// A restored conversation (media, reasoning, completed and failed calls, approved and rejected approvals, an action
    /// that never ran), then a message that failed and another that failed too: "Not sent", then the error and Retry.
    /// </summary>
    private static async Task<(AIChatView, Func<Task>)> HistoryAsync()
    {
        var client = new ScriptedChatClient()
            .Fail(new InvalidOperationException("offline"))
            .Fail(new InvalidOperationException("offline"));
        var session = ChatHost.Session(client, options =>
            options.RegisterUIAction(AIFunctionFactory.Create((string city) => $"Map of {city}", "show_map"), UIActionInvocationMode.Manual));
        var delete = new FunctionCallContent("c3", "delete_file", new Dictionary<string, object?> { ["path"] = "a.txt" });
        var move = new FunctionCallContent("c4", "move_file", new Dictionary<string, object?> { ["path"] = "b.txt" });
        var approveDelete = new ToolApprovalRequestContent("a1", delete);
        var approveMove = new ToolApprovalRequestContent("a2", move);
        await session.RestoreAsync(
        [
            new ChatMessage(ChatRole.User, [new TextContent("Plan a trip to Paris"), Blocks.Png("map.png")]),
            new ChatMessage(
                ChatRole.Assistant,
                [
                    new TextReasoningContent("Comparing trains and flights."),
                    new TextReasoningContent(null) { ProtectedData = "opaque" },
                    new FunctionCallContent("c1", "search", new Dictionary<string, object?> { ["query"] = "trains" }),
                    new FunctionCallContent("c2", "book", new Dictionary<string, object?> { ["seat"] = "12A" }),
                ]),
            new ChatMessage(
                ChatRole.Tool,
                [
                    new FunctionResultContent("c1", "3 trains"),
                    new FunctionResultContent("c2", null) { Exception = new InvalidOperationException("Sold out") },
                ]),
            new ChatMessage(ChatRole.Assistant, [approveDelete, approveMove]),
            new ChatMessage(ChatRole.User, [approveDelete.CreateResponse(true), approveMove.CreateResponse(false, "Not now")]),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c3", "delete_file", delete.Arguments) { InformationalOnly = true }]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c3", "Deleted a.txt")]),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c5", "show_map", new Dictionary<string, object?> { ["city"] = "Paris" })]),
            new ChatMessage(
                ChatRole.Assistant,
                [
                    new TextContent(Markdown),
                    Blocks.Png("chart.png"),
                    Blocks.Pdf("itinerary.pdf"),
                    new UriContent("https://example.com/guide.pdf", "application/pdf"),
                    new UriContent("https://example.com/eiffel.png", "image/png"),
                ]),
        ]);

        var view = ChatHost.Create(session);
        await SendAsync(view, "Book the train");
        await SendAsync(view, "Try again");
        Assert.Equal(ConversationStatus.Error, session.Status);
        return (view, static () => Task.CompletedTask);
    }

    /// <summary>A response waiting for the user: a running call, a pending approval, and a manual action.</summary>
    private static async Task<(AIChatView, Func<Task>)> AwaitingInputAsync()
    {
        var client = new ScriptedChatClient().Reply(
            new TextContent("Working on it."),
            new FunctionCallContent("c1", "search", new Dictionary<string, object?> { ["query"] = "hotels" }),
            new ToolApprovalRequestContent("a1", new FunctionCallContent("c2", "delete_file", new Dictionary<string, object?> { ["path"] = "a.txt" })),
            new FunctionCallContent("c3", "show_map", new Dictionary<string, object?> { ["city"] = "Paris" }));
        var session = ChatHost.Session(client, options =>
            options.RegisterUIAction(AIFunctionFactory.Create((string city) => $"Map of {city}", "show_map"), UIActionInvocationMode.Manual));
        var view = ChatHost.Create(session);

        view.Text = "Find a hotel";
        var send = view.SendAsync();
        await UIThread.Until(() => session.Status == ConversationStatus.AwaitingInput);
        return (view, async () =>
        {
            await view.StopAsync();
            await send;
        });
    }

    /// <summary>A response that has not streamed anything yet: thinking, and the Stop button.</summary>
    private static async Task<(AIChatView, Func<Task>)> StreamingAsync()
    {
        var client = new ScriptedChatClient();
        var stream = client.ReplyLive();
        var view = ChatHost.Create(ChatHost.Session(client));

        view.Text = "Tell me a story";
        var send = view.SendAsync();
        await UIThread.Until(() => view.Rows().OfType<ThinkingRow>().Any() && view.CanStop);
        return (view, async () =>
        {
            stream.Complete();
            await send;
        });
    }

    /// <summary>The structured rich-text view, shown in a slot so it sits in a chat view.</summary>
    private static Task<(AIChatView, Func<Task>)> RichTextAsync()
    {
        var heading = new HeadingNode(2);
        heading.AddChild(new TextNode("Heading"));
        var paragraph = new ParagraphNode();
        paragraph.AddChild(new TextNode("plain "));
        paragraph.AddChild(Wrap(new StrongNode(), "bold"));
        paragraph.AddChild(new InlineCodeNode("code"));
        paragraph.AddChild(Wrap(new LinkNode("https://example.com"), " link"));
        var quote = new BlockQuoteNode();
        quote.AddChild(Wrap(new ParagraphNode(), "quoted"));
        var list = new ListNode(ordered: true, start: 1);
        var item = new ListItemNode();
        item.AddChild(Wrap(new ParagraphNode(), "item"));
        list.AddChild(item);
        var table = new TableNode();
        var row = new TableRowNode();
        row.AddChild(Wrap(new TableCellNode(), "cell"));
        table.AddChild(row);
        var footnote = new FootnoteDefinitionNode { Label = "1" };
        footnote.AddChild(new TextNode("note"));

        var block = new RichContentBlock { Role = ChatRole.Assistant };
        block.ReplaceContent(
            "fallback",
            [
                heading,
                paragraph,
                new CodeBlockNode("var answer = 42;", "csharp"),
                quote,
                list,
                table,
                new ThematicBreakNode(),
                new ImageNode("https://example.com/cat.png", "cat"),
                footnote,
                new ParagraphNode(),
            ]);

        var rich = new RichTextBlockView();
        var view = ChatHost.Create(ChatHost.Session(new ScriptedChatClient()), v => v.InputHeaderContent = rich);
        BlockView.Bind(rich, block, owner: null, session: null);
        return Task.FromResult<(AIChatView, Func<Task>)>((view, static () => Task.CompletedTask));

        static RichTextNode Wrap(RichTextNode node, string text)
        {
            node.AddChild(new TextNode(text));
            return node;
        }
    }

    /// <summary>
    /// An answer in Markdown with every construct: headings, a paragraph with emphasis, inline code and a link, nested
    /// and task lists, a quote, fenced code with its language, a table, a rule, an image, and images shown as chips
    /// (one that is not a web link, and one that fails to load, see <see cref="ShowLostImage"/>).
    /// </summary>
    private static async Task<(AIChatView, Func<Task>)> MarkdownAsync()
    {
        var view = ChatHost.Create(ChatHost.Session(new ScriptedChatClient().Reply(MarkdownAnswer)));
        await SendAsync(view, "Plan my trip");
        return (view, static () => Task.CompletedTask);
    }

    // Images load on the platform: fail one by hand.
    private static void ShowLostImage(IReadOnlyList<Element> parts) =>
        parts.OfType<LinkedImageView>().Single(view => view.Image.Source is UriImageSource { Uri.AbsolutePath: "/lost.png" }).ShowFallback();

    private static async Task SendAsync(AIChatView view, string text)
    {
        view.Text = text;
        await view.SendAsync();
    }

    // ── Comparing ───────────────────────────────────────────────────────────────────────────────────────────

    private static List<Look> Describe(IEnumerable<Element> elements) =>
        elements.Select((element, index) => Describe(element, index)).ToList();

    private static Look Describe(Element element, int index)
    {
        var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var outline = IsOutline(element);
        foreach (var property in BindableProperties(element.GetType()))
        {
            if (outline && !OutlineProperties.Contains(property.PropertyName))
                continue;

            if (Format(element.GetValue(property)) is { } value)
                properties[property.PropertyName] = value;
        }

        if (element is VisualElement visual && !outline)
            properties["VisualStateGroups"] = Format(VisualStateManager.GetVisualStateGroups(visual))!;

        var name = $"{index}:{element.GetType().Name}";
        if (element is VisualElement { AutomationId: { Length: > 0 } automationId })
            name += "#" + automationId;
        var text = element switch
        {
            Label label => label.Text,
            Button button => button.Text,
            Span span => span.Text,
            _ => null,
        };
        if (!string.IsNullOrEmpty(text))
            name += $" '{(text.Length > 24 ? text[..24] + "…" : text)}'";

        return new Look(name, element.GetType(), properties);
    }

    // A border's shape only gives it its outline (the border draws it with its own brushes and thickness), so only
    // what changes the outline counts.
    private static readonly string[] OutlineProperties = ["Aspect", "CornerRadius", "StrokeThickness"];

    private static bool IsOutline(Element element) =>
        element is Shape { Parent: Border border } shape && ReferenceEquals(border.StrokeShape, shape);

    private static void AssertSameLook(string scenario, IReadOnlyList<Look> expected, IReadOnlyList<Look> actual)
    {
        Assert.NotEmpty(expected);
        Assert.Equal(expected.Select(look => look.Element), actual.Select(look => look.Element));

        var differences = new List<string>();
        for (var i = 0; i < expected.Count; i++)
        {
            foreach (var (name, value) in expected[i].Properties)
            {
                var other = actual[i].Properties.GetValueOrDefault(name);
                if (other != value)
                    differences.Add($"  {expected[i].Element}.{name}: {value} -> {other ?? "(missing)"}");
            }
        }

        Assert.True(
            differences.Count == 0,
            $"{scenario}: the app's implicit styles changed {differences.Count} values of built-in parts:\n" +
            string.Join("\n", differences.Take(40)));
    }

    private static readonly Dictionary<Type, BindableProperty[]> PropertiesByType = [];

    private static BindableProperty[] BindableProperties(Type type)
    {
        if (!PropertiesByType.TryGetValue(type, out var properties))
        {
            properties = type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(field => field.FieldType == typeof(BindableProperty))
                .Select(field => (BindableProperty)field.GetValue(null)!)
                .Distinct()
                .GroupBy(property => property.PropertyName)
                .Select(group => group.First())
                .ToArray();
            PropertiesByType[type] = properties;
        }

        return properties;
    }

    private static string? Format(object? value) =>
        value switch
        {
            null => "null",
            string text => $"'{text}'",
            Color color => color.ToArgbHex(includeAlpha: true),
            SolidColorBrush brush => "Brush " + Format(brush.Color),
            Brush brush => brush.GetType().Name,
            Thickness thickness => FormattableString.Invariant($"{thickness.Left},{thickness.Top},{thickness.Right},{thickness.Bottom}"),
            CornerRadius radius => FormattableString.Invariant($"{radius.TopLeft},{radius.TopRight},{radius.BottomLeft},{radius.BottomRight}"),
            LayoutOptions options => options.Alignment.ToString(),
            RoundRectangle shape => "RoundRectangle " + Format(shape.CornerRadius),
            Shape shape => shape.GetType().Name,
            Geometry geometry => geometry.GetType().Name,
            Shadow shadow => $"Shadow {Format(shadow.Brush)} {shadow.Offset} {shadow.Radius} {shadow.Opacity}",
            DoubleCollection values => string.Join(",", values),
            VisualStateGroupList groups => "[" + string.Join(
                "; ",
                groups.Select(group => group.Name + ": " + string.Join(
                    ", ",
                    group.States.Select(state => state.Name + "(" + string.Join(
                        " ",
                        state.Setters.Select(setter => setter.Property?.PropertyName + "=" + Format(setter.Value))) + ")")))) + "]",
            IVisual visual => visual.GetType().Name,
            Keyboard keyboard => keyboard.GetType().Name,
            bool or Enum => value.ToString(),
            IFormattable formattable when value.GetType().IsValueType => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => null,
        };

    /// <summary>The public types whose implicit styles an element of <paramref name="type"/> looks up.</summary>
    private static IEnumerable<Type> StyleTargets(Type type)
    {
        for (var current = type; current is not null && !StyleRoots.Contains(current); current = current.BaseType)
        {
            if (current.IsVisible && !current.IsGenericType)
                yield return current;
        }
    }

    private static List<Style> GarishStyles(IEnumerable<Type> types, bool applyToDerivedTypes) =>
        types.Distinct().Select(type => GarishStyle(type, applyToDerivedTypes)).ToList();

    /// <summary>A style that changes everything it can: colors, fonts, sizes, spacing, shapes, and visual states.</summary>
    private static Style GarishStyle(Type type, bool applyToDerivedTypes)
    {
        var style = new Style(type) { ApplyToDerivedTypes = applyToDerivedTypes };
        var properties = BindableProperties(type).Where(property => !property.IsReadOnly).ToDictionary(property => property.PropertyName);
        foreach (var property in properties.Values)
        {
            if (GarishValue(property) is { } value)
                style.Setters.Add(new Setter { Property = property, Value = value });
        }

        if (typeof(VisualElement).IsAssignableFrom(type))
        {
            var disabled = new VisualState { Name = "Disabled" };
            disabled.Setters.Add(new Setter { Property = VisualElement.BackgroundColorProperty, Value = Colors.DarkGray });
            disabled.Setters.Add(new Setter { Property = VisualElement.OpacityProperty, Value = 0.15 });
            if (properties.TryGetValue("TextColor", out var textColor))
                disabled.Setters.Add(new Setter { Property = textColor, Value = Colors.Gray });

            var normal = new VisualState { Name = "Normal" };
            normal.Setters.Add(new Setter { Property = VisualElement.RotationProperty, Value = 1d });
            var focused = new VisualState { Name = "Focused" };
            focused.Setters.Add(new Setter { Property = VisualElement.BackgroundColorProperty, Value = Colors.Yellow });
            var pointerOver = new VisualState { Name = "PointerOver" };
            pointerOver.Setters.Add(new Setter { Property = VisualElement.ScaleProperty, Value = 1.3 });

            var common = new VisualStateGroup { Name = "CommonStates" };
            common.States.Add(normal);
            common.States.Add(disabled);
            common.States.Add(focused);
            common.States.Add(pointerOver);
            style.Setters.Add(new Setter { Property = VisualStateManager.VisualStateGroupsProperty, Value = new VisualStateGroupList { common } });
        }

        return style;
    }

    private static object? GarishValue(BindableProperty property) =>
        property.PropertyName switch
        {
            "BackgroundColor" => Colors.Magenta,
            "Background" => new SolidColorBrush(Colors.Lime),
            "TextColor" => Colors.Orange,
            "BorderColor" => Colors.Cyan,
            "PlaceholderColor" => Colors.Brown,
            "Color" when property.ReturnType == typeof(Color) => Colors.Red,
            "Stroke" => new SolidColorBrush(Colors.Teal),
            "StrokeThickness" => 6d,
            "StrokeShape" => new Ellipse(),
            "BorderWidth" => 5d,
            "CornerRadius" when property.ReturnType == typeof(int) => 3,
            "CornerRadius" when property.ReturnType == typeof(CornerRadius) => new CornerRadius(3),
            "Margin" => new Thickness(7),
            "Padding" => new Thickness(9),
            "MinimumWidthRequest" => 61d,
            "MinimumHeightRequest" => 62d,
            "WidthRequest" => 63d,
            "HeightRequest" => 64d,
            "Opacity" => 0.42,
            "Rotation" => 2d,
            "Scale" => 1.1,
            "Shadow" => new Shadow { Brush = new SolidColorBrush(Colors.Red), Radius = 9, Opacity = 0.7f, Offset = new Point(2, 2) },
            "FontFamily" => "GarishSans",
            "FontSize" => 31d,
            "FontAttributes" => FontAttributes.Italic,
            "CharacterSpacing" => 3d,
            "TextTransform" => TextTransform.Uppercase,
            "TextDecorations" => TextDecorations.Underline,
            "LineHeight" => 2.1,
            "HorizontalTextAlignment" => TextAlignment.End,
            "LineBreakMode" => LineBreakMode.CharacterWrap,
            "Spacing" when property.ReturnType == typeof(double) => 13d,
            "RowSpacing" => 11d,
            "ColumnSpacing" => 12d,
            "Aspect" when property.ReturnType == typeof(Aspect) => Aspect.Fill,
            "Aspect" when property.ReturnType == typeof(Stretch) => Stretch.Fill,
            "HorizontalOptions" => LayoutOptions.Center,
            "VerticalOptions" => LayoutOptions.Center,
            "FlowDirection" => FlowDirection.RightToLeft,
            _ => null,
        };

    private sealed record Scenario(
        string Name,
        Func<Task<(AIChatView View, Func<Task> CleanUp)>> Render,
        Action<IReadOnlyList<Element>>? Shown = null)
    {
        public override string ToString() => Name;
    }

    private sealed record Look(string Element, Type Type, IReadOnlyDictionary<string, string> Properties);

    /// <summary>A shown scenario: the view and its realized rows (a list without a platform realizes none itself).</summary>
    private sealed class Rendering(AIChatView view, IReadOnlyList<View> rows, Func<Task> cleanUp) : IAsyncDisposable
    {
        public AIChatView View => view;

        /// <summary>
        /// Gets the view's parts: everything inside it (the view itself is the app's to style), the rows, and the
        /// list's footer.
        /// </summary>
        public IEnumerable<Element> Parts()
        {
            var parts = Tree(view).Skip(1).Concat(rows.SelectMany(Tree));
            if (view.MessageList.Footer is Element footer)
                parts = parts.Concat(Tree(footer));
            return parts.Distinct();
        }

        public async ValueTask DisposeAsync() => await cleanUp();
    }
}
