using System.Text.Json;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public class BuiltInViewTests
{
    [Fact]
    public void TextBlock_RendersTheAnswerAsMarkdownFullWidth()
    {
        var view = Render<TextBlockView>(Blocks.Text("## Plan\n\nUse **bold**, `code` and [docs](https://example.com)."));

        Assert.Equal("AIChatAssistantText", view.AutomationId);
        Assert.Same(view.Markdown, view.Content);
        Assert.Collection(
            view.Markdown.BlockViews,
            heading => Assert.Equal(SemanticHeadingLevel.Level2, SemanticProperties.GetHeadingLevel(Assert.IsAssignableFrom<Label>(heading))),
            paragraph => Assert.IsAssignableFrom<Label>(paragraph));

        // The chat theme styles it: body text, the theme's code font, accent links.
        var paragraph = (Label)view.Markdown.BlockViews[1]!;
        Assert.Equal(Theme(view)[AIChatThemeKeys.AssistantTextStyle], paragraph.Style);
        var spans = paragraph.FormattedText!.Spans;
        Assert.Contains(spans, span => span.Text == "bold" && span.FontAttributes == FontAttributes.Bold);
        Assert.Equal("monospace", spans.Single(span => span.Text == "code").FontFamily);
        Assert.Equal(Theme(view)[AIChatThemeKeys.AccentColor], spans.Single(span => span.Text == "docs").TextColor);
    }

    [Fact]
    public void TextBlock_WhileStreaming_RebuildsOnlyTheGrowingBlock()
    {
        var block = Blocks.Text("# Title\n\nFirst paragraph.\n\nSec");
        var view = Render<TextBlockView>(block);
        var title = view.Markdown.BlockViews[0];
        var first = view.Markdown.BlockViews[1];

        block.AppendText("ond paragraph");
        Refresh(view);

        Assert.Same(title, view.Markdown.BlockViews[0]);
        Assert.Same(first, view.Markdown.BlockViews[1]);
        Assert.Equal("Second paragraph", Assert.IsAssignableFrom<Label>(view.Markdown.BlockViews[2]).Text);

        block.AppendText("\n\n- a list");
        Refresh(view);

        Assert.Equal(4, view.Markdown.BlockViews.Count);
        Assert.Same(first, view.Markdown.BlockViews[1]);
    }

    [Fact]
    public void TextBlock_TheFirstAndLastBlocksHaveNoOuterMargin()
    {
        var view = Render<TextBlockView>(Blocks.Text("# Top\n\nText\n\n## Middle\n\nMore\n\n---"));
        var blocks = view.Markdown.BlockViews;

        Assert.Equal(0, blocks[0]!.Margin.Top);
        Assert.True(blocks[2]!.Margin.Top > 0);
        Assert.Equal(0, blocks[^1]!.Margin.Bottom);

        // A block that stops being the last gets its margin back.
        var block = Blocks.Text("Text\n\n---");
        view = Render<TextBlockView>(block);
        Assert.Equal(0, view.Markdown.BlockViews[^1]!.Margin.Bottom);
        block.AppendText("\n\nAfter");
        Refresh(view);
        Assert.True(view.Markdown.BlockViews[1]!.Margin.Bottom > 0);
    }

    [Fact]
    public void Markdown_ImagesThatCannotLoadShowAChip()
    {
        var view = Render<TextBlockView>(Blocks.Text(
            "![edited](attachment://edited_image.png)\n\n![](attachment://chart.png)\n\n![cat](https://example.com/cat.png)"));
        var blocks = view.Markdown.BlockViews;

        // Not a web link: a chip with the description and the file, not an empty gap, and nothing to open.
        var edited = Assert.IsAssignableFrom<Border>(blocks[0]);
        Assert.Equal("edited, Image \u00B7 edited_image.png", SemanticProperties.GetDescription(edited));
        Assert.Empty(edited.GestureRecognizers);
        Assert.Equal("chart.png, Image", SemanticProperties.GetDescription(Assert.IsAssignableFrom<Border>(blocks[1])));

        // A web image loads inline; if it fails, the same chip replaces it and opens the link.
        var image = Assert.IsType<LinkedImageView>(blocks[2]);
        Assert.Equal(new Uri("https://example.com/cat.png"), Assert.IsType<UriImageSource>(image.Image.Source).Uri);
        Assert.Equal("cat", SemanticProperties.GetDescription(image.Image));
        image.ShowFallback();
        var chip = Assert.IsAssignableFrom<Border>(image.Content);
        Assert.Equal("cat, Image \u00B7 cat.png", SemanticProperties.GetDescription(chip));
        Assert.Single(chip.GestureRecognizers.OfType<TapGestureRecognizer>());
    }

    [Fact]
    public void RichText_RendersProviderNodesWithTheMarkdownRenderer()
    {
        var heading = new HeadingNode(2);
        heading.AddChild(new TextNode("Heading"));

        var paragraph = new ParagraphNode();
        paragraph.AddChild(new TextNode("plain "));
        paragraph.AddChild(Wrap(new StrongNode(), "bold"));
        paragraph.AddChild(Wrap(new EmphasisNode(), " italic"));
        paragraph.AddChild(Wrap(new StrikethroughNode(), " removed"));
        paragraph.AddChild(new InlineCodeNode("code"));
        paragraph.AddChild(Wrap(new LinkNode("https://example.com"), " link"));
        paragraph.AddChild(Wrap(new LinkNode("javascript:alert(1)"), " unsafe"));

        var quote = new BlockQuoteNode();
        quote.AddChild(Paragraph("quoted"));

        var list = new ListNode(ordered: true, start: 3);
        var item = new ListItemNode();
        item.AddChild(Paragraph("item"));
        list.AddChild(item);

        var table = new TableNode { Alignment = [TableColumnAlignment.Right] };
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
                new ImageNode("javascript:bad", "unsafe image"),
                new HtmlNode("<b>escaped</b>"),
                footnote,
            ]);

        var view = Render<RichTextBlockView>(block);

        Assert.Equal("AIChatRichText", view.AutomationId);
        var blocks = view.Markdown.BlockViews;
        Assert.Equal(10, blocks.Count);
        var headingLabel = Assert.IsAssignableFrom<Label>(blocks[0]);
        Assert.Equal(FontAttributes.Bold, headingLabel.FontAttributes);
        Assert.Equal(SemanticHeadingLevel.Level2, SemanticProperties.GetHeadingLevel(headingLabel));

        var spans = Assert.IsAssignableFrom<Label>(blocks[1]).FormattedText!.Spans;
        Assert.Contains(spans, span => span.Text == "bold" && span.FontAttributes.HasFlag(FontAttributes.Bold));
        Assert.Contains(spans, span => span.Text == " italic" && span.FontAttributes.HasFlag(FontAttributes.Italic));
        Assert.Contains(spans, span => span.Text == " removed" && span.TextDecorations.HasFlag(TextDecorations.Strikethrough));
        Assert.Contains(spans, span => span.Text == "code" && span.FontFamily == "monospace");
        Assert.Single(spans, span => span.Text == " link").GestureRecognizers.Single();
        Assert.Empty(Assert.Single(spans, span => span.Text == " unsafe").GestureRecognizers);

        var code = Assert.IsAssignableFrom<Border>(blocks[2]);
        Assert.Equal(Theme(view)[AIChatThemeKeys.CodeBlockStyle], code.Style);
        Assert.Contains(code.Descendants<Label>(), label => label.Text == "var answer = 42;");
        Assert.IsAssignableFrom<Grid>(blocks[3]);
        Assert.Contains(((Element)blocks[4]!).Descendants<Label>(), label => label.Text == "3.");
        Assert.IsAssignableFrom<ScrollView>(blocks[5]);
        Assert.IsAssignableFrom<BoxView>(blocks[6]);
        Assert.Equal("unsafe image, Image \u00B7 bad", SemanticProperties.GetDescription(Assert.IsAssignableFrom<Border>(blocks[7])));
        Assert.Equal("<b>escaped</b>", Assert.IsAssignableFrom<Label>(blocks[8]).Text);
        Assert.IsAssignableFrom<Grid>(blocks[9]);
    }

    [Fact]
    public void RichText_WithoutNodes_ShowsTheRawText()
    {
        var block = new RichContentBlock { Role = ChatRole.Assistant };
        block.ReplaceContent("raw fallback", []);

        var view = Render<RichTextBlockView>(block);

        Assert.Equal("raw fallback", Assert.IsAssignableFrom<Label>(Assert.Single(view.Markdown.BlockViews)).Text);
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com", true)]
    [InlineData("mailto:someone@example.com", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("relative/path", false)]
    [InlineData(null, false)]
    public void RichText_OnlyOpensSafeLinks(string? uri, bool safe) =>
        Assert.Equal(safe, ChatVisuals.IsSafeUri(uri));

    [Fact]
    public void ToolCall_ShowsNameAndStatusAndExpandsToDetails()
    {
        var block = Blocks.ToolCall("get_weather", new Dictionary<string, object?> { ["city"] = "Paris", ["days"] = 3 });
        var view = Render<ToolCallView>(block);

        Assert.Equal("get_weather", view.TitleLabel.Text);
        Assert.Equal("Running…", view.StatusLabel.Text);
        Assert.False(view.Details.IsVisible);

        view.Toggle();
        Assert.True(view.Details.IsVisible);
        Assert.Contains(view.Details.Descendants<Label>(), label => label.Text == "city: Paris\ndays: 3");

        block.Result = new FunctionResultContent("call-1", JsonSerializer.SerializeToElement(new { forecast = "sunny" }));
        Refresh(view);
        Assert.Equal("Completed", view.StatusLabel.Text);
        Assert.Contains(view.Details.Descendants<Label>(), label => label.Text?.Contains("\"forecast\": \"sunny\"", StringComparison.Ordinal) == true);

        view.Toggle();
        Assert.False(view.Details.IsVisible);
    }

    [Fact]
    public void ToolCall_Failure_ShowsTheError()
    {
        var view = Render<ToolCallView>(Blocks.ToolCall("search", exception: new InvalidOperationException("quota exceeded")));

        Assert.Equal("Failed", view.StatusLabel.Text);
        Assert.True(view.Resources.MergedDictionaries.Single().TryGetValue(AIChatThemeKeys.ErrorColor, out var error));
        Assert.Equal(error, view.StatusLabel.TextColor);
        view.Toggle();
        Assert.Contains(view.Details.Descendants<Label>(), label => label.Text == "quota exceeded");
    }

    [Fact]
    public void ToolCall_ToggleHasAnAccessibleName()
    {
        var view = Render<ToolCallView>(Blocks.ToolCall("search"));
        var toggle = view.ByAutomationId<Button>("AIChatToolCallDetails");

        Assert.Equal("Show details for search", SemanticProperties.GetDescription(toggle));
        toggle.Command.Execute(null);
        Assert.Equal("Hide details for search", SemanticProperties.GetDescription(toggle));
    }

    [Fact]
    public void ToolApproval_ApproveAndReject_ResolveTheBlock()
    {
        var approve = Blocks.Approval("delete_file", new Dictionary<string, object?> { ["path"] = "/tmp/a" });
        var approveView = Render<ToolApprovalView>(approve);

        Assert.Equal("Allow delete_file to run?", approveView.TitleLabel.Text);
        Assert.True(approveView.Buttons.IsVisible);
        Assert.Contains(approveView.Descendants<Label>(), label => label.Text == "path: /tmp/a");
        Assert.True(approveView.ApproveCommand.CanExecute(null));

        approveView.ApproveCommand.Execute(null);

        Assert.Equal(ApprovalStatus.Approved, approve.Status);
        Assert.False(approveView.Buttons.IsVisible);
        Assert.Equal("delete_file", approveView.TitleLabel.Text);
        Assert.Equal("Approved \u00B7 Running\u2026", approveView.StatusLabel.Text);
        Assert.False(approveView.ApproveCommand.CanExecute(null));
        Assert.False(approveView.RejectCommand.CanExecute(null));

        // Once decided, the arguments fold away like a tool call's details.
        Assert.True(approveView.DetailsToggle.IsVisible);
        Assert.False(approveView.Details.IsVisible);

        // The call's result lands in the approval's inner block: the same card shows it.
        approve.InnerBlock.Result = new FunctionResultContent("call-1", "deleted /tmp/a");
        Refresh(approveView);
        Assert.Equal("Approved", approveView.StatusLabel.Text);
        Assert.Equal("deleted /tmp/a", approveView.ResultLabel.Text);
        Assert.False(approveView.Details.IsVisible);

        approveView.DetailsToggle.Command.Execute(null);
        Assert.True(approveView.Details.IsVisible);
        Assert.True(approveView.ResultLabel.Parent is Border { IsVisible: true });
        Assert.Equal("Hide details for delete_file", SemanticProperties.GetDescription(approveView.DetailsToggle));

        var reject = Blocks.Approval("delete_file");
        var rejectView = Render<ToolApprovalView>(reject);
        rejectView.RejectCommand.Execute(null);

        Assert.Equal(ApprovalStatus.Rejected, reject.Status);
        Assert.Equal("Rejected", rejectView.StatusLabel.Text);
    }

    [Fact]
    public void ToolApproval_InAnEndedTurn_CanNoLongerBeAnswered()
    {
        var session = ChatHost.Session(new ScriptedChatClient());
        var block = Blocks.Approval("delete_file");

        // The engine abandons a pending approval when its turn ends.
        block.Abandon();
        var view = new ToolApprovalView();
        view.Resources.MergedDictionaries.Add(new AIChatTheme());

        BlockView.Bind(view, block, owner: null, session, new ConversationTurn { IsStopped = true });

        Assert.False(view.Buttons.IsVisible);
        Assert.False(view.ApproveCommand.CanExecute(null));
        Assert.Equal("delete_file", view.TitleLabel.Text);
        Assert.Equal("Stopped", view.StatusLabel.Text);

        BlockView.Bind(view, block, owner: null, session, new ConversationTurn());
        Assert.Equal("Not answered", view.StatusLabel.Text);

        var approved = Blocks.Approval("delete_file");
        approved.Approve();
        BlockView.Bind(view, approved, owner: null, session, new ConversationTurn { IsStopped = true });
        Assert.Equal("Approved \u00B7 Stopped", view.StatusLabel.Text);

        BlockView.Bind(view, approved, owner: null, session, new ConversationTurn());
        Assert.Equal("Approved \u00B7 Not completed", view.StatusLabel.Text);
        Assert.False(view.ResultLabel.Parent is Border { IsVisible: true });
    }

    [Theory]
    [InlineData(false, false, "Not completed")]
    [InlineData(false, true, "Stopped")]
    [InlineData(true, false, "")]
    public void ToolCall_WithoutAResultInAnEndedTurn_SaysWhy(bool informational, bool stopped, string expected)
    {
        var session = ChatHost.Session(new ScriptedChatClient());
        var block = Blocks.ToolCall("search");
        block.Call!.InformationalOnly = informational;
        var view = new ToolCallView();
        view.Resources.MergedDictionaries.Add(new AIChatTheme());

        BlockView.Bind(view, block, owner: null, session, new ConversationTurn { IsStopped = stopped });

        Assert.Equal(expected, view.StatusLabel.Text);
        Assert.Equal(expected.Length > 0, view.StatusLabel.IsVisible);
    }

    [Fact]
    public void ToolApproval_ResumesTheConversation() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient()
            .RequestApproval("delete_file", new Dictionary<string, object?> { ["path"] = "/tmp/a" })
            .Reply("File deleted.");
        var view = ChatHost.Create(ChatHost.Session(client));

        view.Text = "Delete it";
        var send = view.SendAsync();
        await UIThread.Until(() => view.Session!.Status == ConversationStatus.AwaitingInput);

        Assert.True(view.IsBusy);
        Assert.True(view.CanStop);
        var row = Assert.Single(view.Rows().OfType<BlockRow>(), row => row.Block is ToolApprovalBlock);
        var approval = Assert.IsType<ToolApprovalView>(((BlockHost)view.Realize(row)).HostedView);
        Assert.Same(view.Session, approval.Session);

        approval.ApproveCommand.Execute(null);
        await send;

        Assert.Equal(ConversationStatus.Idle, view.Session!.Status);
        Assert.Equal("File deleted.", ((TextContentBlock)((BlockRow)view.Rows()[^1]).Block).RawText);
        Assert.Equal(ApprovalStatus.Approved, ((ToolApprovalBlock)row.Block).Status);
    });

    [Fact]
    public void ToolApproval_RendersTheAppTemplateForTheTool() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().RequestApproval("get_weather", new Dictionary<string, object?> { ["city"] = "Rome" }).Reply("ok");
        var template = new BlockTemplate { ToolName = "get_weather", BlockType = typeof(FunctionInvocationContentBlock), ViewType = typeof(CountingBlockView) };
        var view = ChatHost.Create(ChatHost.Session(client), v => v.ContentTemplates.Add(template));

        view.Text = "Weather?";
        var send = view.SendAsync();
        await UIThread.Until(() => view.Session!.Status == ConversationStatus.AwaitingInput);

        var row = Assert.Single(view.Rows().OfType<BlockRow>(), row => row.Block is ToolApprovalBlock);
        var approval = Assert.IsType<ToolApprovalView>(((BlockHost)view.Realize(row)).HostedView);
        var inner = Assert.IsType<CountingBlockView>(approval.InnerView);
        var block = (ToolApprovalBlock)row.Block;
        Assert.Same(block.InnerBlock, inner.Block);
        Assert.Same(block.InnerBlock, inner.BindingContext);
        Assert.True(inner.IsEnabled);

        approval.RejectCommand.Execute(null);
        await send;
        Assert.False(inner.IsEnabled);
    });

    [Fact]
    public void ToolApproval_ThatCanNoLongerBeAnswered_ShowsNoButtons()
    {
        var block = Blocks.Approval("delete_file");
        block.Abandon();

        var view = Render<ToolApprovalView>(block);

        Assert.False(view.Buttons.IsVisible);
        Assert.False(view.ApproveCommand.CanExecute(null));
        Assert.False(view.RejectCommand.CanExecute(null));
        Assert.Equal("delete_file", view.TitleLabel.Text);
        Assert.Equal("Not answered", view.StatusLabel.Text);
    }

    [Fact]
    public void UIAction_RunningOrNoLongerRunnable_ShowsNoRunButton() => UIThread.Run(async () =>
    {
        // The app runs the action itself, not through the card.
        var gate = Waiters.Signal();
        var running = new UIActionBlock(
            AIFunctionFactory.Create(async () => { await gate.Task; return "done"; }, "slow_action"),
            UIActionInvocationMode.Manual,
            Blocks.ToolCall("slow_action"),
            services: null) { Role = ChatRole.Assistant };
        var invocation = running.InvokeAsync();
        var view = Render<UIActionView>(running);

        Assert.False(view.RunButton.IsVisible);
        Assert.False(view.RunCommand.CanExecute(null));
        Assert.Equal("Running\u2026", view.StatusLabel.Text);

        gate.SetResult();
        await invocation;
        Refresh(view);
        Assert.Equal("Done", view.StatusLabel.Text);

        // The engine abandons a manual action when its turn ends.
        var abandoned = Blocks.UIAction("open_map", UIActionInvocationMode.Manual);
        abandoned.Abandon();
        Rebind(view, abandoned);
        Assert.False(view.RunButton.IsVisible);
        Assert.False(view.RunCommand.CanExecute(null));
    });

    [Fact]
    public void UIAction_ManualActionRunsOnce() => UIThread.Run(async () =>
    {
        var runs = 0;
        var block = Blocks.UIAction("open_map", UIActionInvocationMode.Manual, () => { runs++; return "opened"; });
        var view = Render<UIActionView>(block);

        Assert.Equal("Waiting for you to run it", view.StatusLabel.Text);
        Assert.True(view.RunButton.IsVisible);
        Assert.True(view.RunCommand.CanExecute(null));

        await view.RunAsync();
        await UIThread.Until(() => block.IsComplete);
        Refresh(view);

        Assert.Equal(1, runs);
        Assert.Equal("Done", view.StatusLabel.Text);
        Assert.False(view.RunButton.IsVisible);
        Assert.False(view.RunCommand.CanExecute(null));
    });

    [Fact]
    public void RecycledViews_ShowEachBlocksOwnExpandedState()
    {
        // A list recycles a view for another block of the same template, and later for the first one again.
        var search = Blocks.ToolCall("search");
        var toolCall = Render<ToolCallView>(search);
        toolCall.Toggle();
        Rebind(toolCall, Blocks.ToolCall("fetch"));
        Assert.False(toolCall.Details.IsVisible);
        Rebind(toolCall, search);
        Assert.True(toolCall.Details.IsVisible);

        var thought = Blocks.Reasoning("First thought.");
        var reasoning = Render<ReasoningView>(thought);
        reasoning.Toggle();
        Rebind(reasoning, Blocks.Reasoning("Second thought."));
        Assert.False(reasoning.TextLabel.IsVisible);
        Assert.Equal(0, reasoning.Chevron.Rotation);
        Rebind(reasoning, thought);
        Assert.True(reasoning.TextLabel.IsVisible);

        var arguments = new Dictionary<string, object?> { ["path"] = "/tmp/a" };
        var deleted = Blocks.Approval("delete_file", arguments);
        deleted.Approve();
        var moved = Blocks.Approval("move_file", arguments);
        moved.Approve();
        var approval = Render<ToolApprovalView>(deleted);
        approval.Toggle();
        Rebind(approval, moved);
        Assert.False(approval.Details.IsVisible);
        Rebind(approval, deleted);
        Assert.True(approval.Details.IsVisible);
    }

    [Fact]
    public void RecycledView_ShowsOnlyTheRunningActionAsRunning() => UIThread.Run(async () =>
    {
        var gate = Waiters.Signal();
        var slow = new UIActionBlock(
            AIFunctionFactory.Create(async () => { await gate.Task; return "done"; }, "slow_action"),
            UIActionInvocationMode.Manual,
            Blocks.ToolCall("slow_action"),
            services: null) { Role = ChatRole.Assistant };
        var view = Render<UIActionView>(slow);

        var run = view.RunAsync();
        Assert.Equal("Running\u2026", view.StatusLabel.Text);

        Rebind(view, Blocks.UIAction("open_map", UIActionInvocationMode.Manual));
        Assert.Equal("Waiting for you to run it", view.StatusLabel.Text);
        Assert.True(view.RunCommand.CanExecute(null));

        Rebind(view, slow);
        Assert.Equal("Running\u2026", view.StatusLabel.Text);
        Assert.False(view.RunCommand.CanExecute(null));

        gate.SetResult();
        await run;
        Refresh(view);
        Assert.Equal("Done", view.StatusLabel.Text);
    });

    [Fact]
    public void ActionButtons_NameTheirTool()
    {
        var approval = Render<ToolApprovalView>(Blocks.Approval("delete_file"));
        var approve = approval.ByAutomationId<Button>("ApproveToolButton");
        var reject = approval.ByAutomationId<Button>("RejectToolButton");
        Assert.Equal("Approve delete_file", SemanticProperties.GetDescription(approve));
        Assert.Equal("Reject delete_file", SemanticProperties.GetDescription(reject));

        Rebind(approval, Blocks.Approval("move_file"));
        Assert.Equal("Approve move_file", SemanticProperties.GetDescription(approve));
        Assert.Equal("Reject move_file", SemanticProperties.GetDescription(reject));

        var action = Render<UIActionView>(Blocks.UIAction("open_map", UIActionInvocationMode.Manual));
        Assert.Equal("Run open_map", SemanticProperties.GetDescription(action.RunButton));
    }

    [Fact]
    public void UIAction_AutomaticActionShowsItsProgress()
    {
        var block = Blocks.UIAction("refresh", UIActionInvocationMode.Automatic);
        var view = Render<UIActionView>(block);

        Assert.Equal("Running…", view.StatusLabel.Text);
        Assert.False(view.RunButton.IsVisible);

        block.SetResult(new FunctionResultContent("call-1", "ok"));
        Refresh(view);
        Assert.Equal("Done", view.StatusLabel.Text);
    }

    [Fact]
    public void Reasoning_StartsCollapsedAndExpands()
    {
        var block = Blocks.Reasoning("Because the sky scatters blue light.");
        var view = Render<ReasoningView>(block);

        Assert.Equal("Reasoning", view.Header.Text);
        Assert.Equal(0, view.Chevron.Rotation);
        Assert.False(view.TextLabel.IsVisible);
        Assert.Equal("Show reasoning", SemanticProperties.GetDescription(view.Header));

        view.Header.Command.Execute(null);

        Assert.Equal(90, view.Chevron.Rotation);
        Assert.Equal("Hide reasoning", SemanticProperties.GetDescription(view.Header));
        Assert.True(view.TextLabel.IsVisible);
        Assert.Equal("Because the sky scatters blue light.", view.TextLabel.Text);

        block.AppendText(" More.");
        Refresh(view);
        Assert.EndsWith("More.", view.TextLabel.Text);
    }

    [Fact]
    public void Reasoning_ProtectedReasoningShowsNothing()
    {
        var view = Render<ReasoningView>(Blocks.Reasoning(text: null, protectedData: "opaque-bytes"));

        Assert.False(view.Header.IsVisible);
        Assert.False(view.Chevron.IsVisible);

        view.Toggle();

        Assert.False(view.TextLabel.IsVisible);
        Assert.DoesNotContain(view.Descendants<Label>(), label => label.Text?.Contains("opaque", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Media_ShowsImagesInlineAndFilesAsChips()
    {
        var image = Render<MediaBlockView>(Blocks.Media(Blocks.Png("photo.png"))).MediaView;
        Assert.IsType<ChatImage>(image);
        Assert.Equal("photo.png", SemanticProperties.GetDescription(image!));

        var pdf = Render<MediaBlockView>(Blocks.Media(Blocks.Pdf("report.pdf", bytes: 2048))).MediaView;
        Assert.Equal("report.pdf, PDF · 2 KB", SemanticProperties.GetDescription(pdf!));

        var audio = Render<MediaBlockView>(Blocks.Media(FakeAudioRecorder.Wav(name: "note.wav"))).MediaView;
        Assert.Equal("note.wav, WAV · 64 B", SemanticProperties.GetDescription(audio!));
    }

    [Fact]
    public void Media_ShowsImageLinksInlineAndOtherLinksAsChips()
    {
        var image = Assert.IsType<LinkedImageView>(Render<MediaBlockView>(Blocks.Media(new UriContent(new Uri("https://example.com/cat.png"), "image/png"))).MediaView);
        var source = Assert.IsType<UriImageSource>(image.Image.Source);
        Assert.Equal(new Uri("https://example.com/cat.png"), source.Uri);
        Assert.Same(image.Image, image.Content);

        // An image that cannot be loaded leaves its link.
        image.ShowFallback();
        Assert.StartsWith("cat.png, ", SemanticProperties.GetDescription(Assert.IsAssignableFrom<Border>(image.Content)));

        var link = Render<MediaBlockView>(Blocks.Media(new UriContent(new Uri("https://example.com/docs/q3%20report.pdf"), "application/pdf"))).MediaView;
        var chip = Assert.IsAssignableFrom<Border>(link);
        Assert.Equal("q3 report.pdf, PDF · example.com", SemanticProperties.GetDescription(chip));
        Assert.Single(chip.GestureRecognizers.OfType<TapGestureRecognizer>());

        // Only web links load as images; other schemes become a chip that does not open.
        var unsafeLink = Render<MediaBlockView>(Blocks.Media(new UriContent(new Uri("file:///etc/cat.png"), "image/png"))).MediaView;
        Assert.Empty(Assert.IsAssignableFrom<Border>(unsafeLink).GestureRecognizers);
    }

    [Fact]
    public void Media_AReplacedImageUpdatesInPlace()
    {
        var block = Blocks.Media(Blocks.Png("partial.png"));
        var view = Render<MediaBlockView>(block);
        var image = Assert.IsType<ChatImage>(view.MediaView);
        var first = image.Source;

        block.Content = Blocks.Png("final.png");
        Refresh(view);

        Assert.Same(image, view.MediaView);
        Assert.NotSame(first, image.Source);

        block.Content = Blocks.Pdf();
        Refresh(view);
        Assert.IsAssignableFrom<Border>(view.MediaView);
    }

    [Fact]
    public void Media_ImageSourceCanBeReadMoreThanOnce() => UIThread.Run(async () =>
    {
        var image = Assert.IsType<ChatImage>(ChatVisuals.CreateMediaView(Blocks.Png()));
        var source = Assert.IsType<StreamImageSource>(image.Source);

        for (var i = 0; i < 2; i++)
        {
            await using var stream = await source.Stream(CancellationToken.None);
            Assert.Equal(4, stream.Length);
        }
    });

    [Fact]
    public void UserMessage_ShowsAttachmentsAboveABubbleHoldingOnlyTheText()
    {
        var view = RenderUserMessage(Blocks.Media(Blocks.Png()), Blocks.Text("Look", ChatRole.User));

        // The attachments sit outside the bubble, right-aligned, inset like the bubble.
        Assert.Collection(
            Assert.IsAssignableFrom<VerticalStackLayout>(view.Content).Children,
            attachments => Assert.Same(view.Attachments, attachments),
            bubble => Assert.Same(view.Bubble, bubble));
        Assert.True(view.Attachments.IsVisible);
        Assert.Equal(LayoutOptions.End, view.Attachments.HorizontalOptions);
        Assert.Equal(LayoutOptions.End, Assert.IsType<ChatImage>(Assert.Single(view.Attachments.Children)).HorizontalOptions);
        Assert.Equal(new Thickness(view.Bubble.Margin.Left, view.Bubble.Margin.Top, view.Bubble.Margin.Right, 0), view.Attachments.Margin);
        Assert.True(view.Bubble.Margin.Left > 0);

        // So the bubble is sized to the text alone.
        Assert.Equal(LayoutOptions.End, view.Bubble.HorizontalOptions);
        Assert.Same(view.TextLabel, view.Bubble.Content);
        Assert.Equal("Look", view.TextLabel.Text);
    }

    [Fact]
    public void UserMessage_ShowsOnlyWhatWasSent()
    {
        var textOnly = RenderUserMessage(Blocks.Text("Hi", ChatRole.User));
        Assert.False(textOnly.Attachments.IsVisible);
        Assert.True(textOnly.Bubble.IsVisible);

        var attachmentOnly = RenderUserMessage(Blocks.Media(Blocks.Pdf("notes.pdf")));
        Assert.True(attachmentOnly.Attachments.IsVisible);
        Assert.False(attachmentOnly.Bubble.IsVisible);
        Assert.Contains("notes.pdf", attachmentOnly.Attachments.Descendants<Label>().Select(label => label.Text));
    }

    [Fact]
    public void UserMessage_RefreshingAnUnchangedRow_KeepsItsViews()
    {
        var row = new UserMessageRow(new ConversationTurn());
        row.SetBlocks([Blocks.Media(Blocks.Png()), Blocks.Text("Look", ChatRole.User)]);
        var view = new UserMessageView { BindingContext = row };
        var image = Assert.Single(view.Attachments.Children);
        var text = view.TextLabel;

        // The list refreshes the newest turn's rows whenever the conversation's status changes. Replacing the views
        // of a row that is off screen left them unmeasured (an empty bubble) when it scrolled back on iOS.
        row.NotifyChanged();
        row.NotifyChanged();

        Assert.Same(image, Assert.Single(view.Attachments.Children));
        Assert.Same(text, view.Bubble.Content);
        Assert.Equal("Look", text.Text);
    }

    [Fact]
    public void UserMessage_ShowsTheUsersMarkdownAsTyped()
    {
        var view = RenderUserMessage(Blocks.Text("Make **this** a `list`:\n- one", ChatRole.User));

        Assert.Equal("Make **this** a `list`:\n- one", view.TextLabel.Text);
        Assert.Null(view.TextLabel.FormattedText);
        Assert.Empty(view.Descendants<MarkdownView>());
    }

    private static UserMessageView RenderUserMessage(params ContentBlock[] blocks)
    {
        var row = new UserMessageRow(new ConversationTurn());
        row.SetBlocks([.. blocks]);
        var view = new UserMessageView();
        view.Resources.MergedDictionaries.Add(new AIChatTheme());
        view.BindingContext = row;
        return view;
    }

    [Fact]
    public void Thinking_IsAnnouncedToScreenReaders() =>
        Assert.Equal("The assistant is thinking", SemanticProperties.GetDescription(new ThinkingView()));

    [Fact]
    public void BlockView_OnBlockChanged_RunsWhenTheBlockIsAssigned()
    {
        var view = new CountingBlockView();
        var block = Blocks.Text("hi");

        view.Block = block;

        Assert.Equal(1, view.ChangeCount);
        Assert.Same(block, view.LastBlock);
    }

    private static RichTextNode Wrap(RichTextNode node, string text)
    {
        node.AddChild(new TextNode(text));
        return node;
    }

    private static ParagraphNode Paragraph(string text) => (ParagraphNode)Wrap(new ParagraphNode(), text);

    private static T Render<T>(ContentBlock block)
        where T : BlockView, new()
    {
        var view = new T();
        view.Resources.MergedDictionaries.Add(new AIChatTheme());
        BlockView.Bind(view, block, owner: null, session: null);
        return view;
    }

    private static void Refresh(BlockView view) => BlockView.Bind(view, view.Block!, owner: null, session: null);

    private static void Rebind(BlockView view, ContentBlock block) => BlockView.Bind(view, block, owner: null, session: null);

    private static AIChatTheme Theme(VisualElement view) => view.Resources.MergedDictionaries.OfType<AIChatTheme>().Single();
}
