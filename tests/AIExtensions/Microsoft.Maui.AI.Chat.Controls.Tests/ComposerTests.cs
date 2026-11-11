using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public class ComposerTests
{
    [Fact]
    public void SendAsync_ClearsTheDraftAsSoonAsTheSendStarts() => UIThread.Run(async () =>
    {
        var gate = Waiters.Signal();
        var client = new ScriptedChatClient().ReplyAfter(gate.Task, "Thinking about it", " - done");
        var view = ChatHost.Create(ChatHost.Session(client));
        var editor = view.ByAutomationId<Editor>("AIChatInput");

        view.Text = "  Hello there ";
        view.AddAttachment(Blocks.Pdf());
        var send = view.SendAsync();
        await UIThread.Until(() => view.IsBusy);

        Assert.Equal(string.Empty, view.Text);
        Assert.Equal(string.Empty, editor.Text);
        Assert.Empty(view.Attachments);
        Assert.False(view.HasAttachments);
        Assert.Equal("Hello there", client.LastUserText);
        Assert.IsType<UserMessageRow>(view.Rows()[0]);

        // The user can draft the next message while the response streams; finishing must not touch it.
        editor.Text = "next question";
        gate.SetResult();
        await send;

        Assert.Equal("next question", view.Text);
        Assert.False(view.HasError);
        Assert.True(view.CanSend);
    });

    [Fact]
    public void SendAsync_SendsTextFirstThenAttachments() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("ok").Reply("ok");
        var view = ChatHost.Create(ChatHost.Session(client));
        var image = Blocks.Png();
        var pdf = Blocks.Pdf();

        view.Text = "Look";
        view.AddAttachment(image);
        view.AddAttachment(pdf);
        await view.SendAsync();

        var message = client.Requests[0].Last(m => m.Role == ChatRole.User);
        Assert.Collection(
            message.Contents,
            text => Assert.Equal("Look", Assert.IsType<TextContent>(text).Text),
            first => Assert.Same(image, first),
            second => Assert.Same(pdf, second));

        view.AddAttachment(Blocks.Png("only.png"));
        Assert.True(view.CanSend);
        await view.SendAsync();
        var attachmentOnly = client.Requests[1].Last(m => m.Role == ChatRole.User);
        Assert.IsType<DataContent>(Assert.Single(attachmentOnly.Contents));
    });

    [Fact]
    public void SendAsync_WithNothingToSend_DoesNothing() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient();
        var view = ChatHost.Create(ChatHost.Session(client));

        view.Text = "   ";
        Assert.False(view.CanSend);
        Assert.False(view.SendCommand.CanExecute(null));
        await view.SendAsync();

        Assert.Empty(client.Requests);
        Assert.Equal("   ", view.Text);
    });

    [Fact]
    public void SendAsync_WithoutASession_DoesNothing() => UIThread.Run(async () =>
    {
        var view = ChatHost.Create();

        view.Text = "Hi";
        Assert.False(view.CanSend);
        await view.SendAsync();

        Assert.Equal("Hi", view.Text);
    });

    [Fact]
    public void SendAsync_WhileTheResponseStreams_IsIgnored() => UIThread.Run(async () =>
    {
        var gate = Waiters.Signal();
        var client = new ScriptedChatClient().WaitThenReply(gate.Task, "Done");
        var view = ChatHost.Create(ChatHost.Session(client));

        view.Text = "first";
        var send = view.SendAsync();
        await UIThread.Until(() => view.IsBusy);

        view.Text = "second";
        await view.SendAsync();
        Assert.Single(client.Requests);
        Assert.Equal("second", view.Text);

        gate.SetResult();
        await send;
        Assert.Single(client.Requests);
    });

    [Fact]
    public void SendAsync_RejectedBeforeTheSessionAcceptsIt_RestoresTheDraftAndShowsOneError() => UIThread.Run(async () =>
    {
        var session = ChatHost.Session(new ScriptedChatClient());
        var view = ChatHost.Create(session);
        var attachment = Blocks.Pdf();
        view.Text = "Keep me";
        view.AddAttachment(attachment);
        session.Dispose();

        await view.SendAsync();

        Assert.Equal("Keep me", view.Text);
        Assert.Same(attachment, Assert.Single(view.Attachments));
        Assert.True(view.HasError);
        Assert.Equal(ChatComposer.SendErrorMessage, view.ErrorMessage);

        // B2: exactly one visible surface reports the error.
        var errorLabels = view.Descendants<Label>().Where(label => label.Text == ChatComposer.SendErrorMessage).ToList();
        var errorLabel = Assert.Single(errorLabels);
        Assert.True(errorLabel.IsVisible);
        Assert.Equal("AIChatError", errorLabel.AutomationId);
    });

    [Fact]
    public void SendAsync_StreamFailure_KeepsTheMessageInTheConversationInsteadOfTheBox() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Fail(new HttpRequestException("offline"));
        var view = ChatHost.Create(ChatHost.Session(client));

        view.Text = "Hi";
        await view.SendAsync();

        Assert.Equal(string.Empty, view.Text);
        Assert.False(view.HasError);
        Assert.IsType<UserMessageRow>(view.Rows()[0]);
        Assert.IsType<TurnErrorRow>(view.Rows()[^1]);
    });

    [Fact]
    public void SendAsync_AfterAnError_ClearsIt() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("ok");
        var view = ChatHost.Create(ChatHost.Session(client), v => v.MaximumAttachmentBytes = 10);

        Assert.False(view.AddAttachment(Blocks.Pdf(bytes: 100)));
        Assert.True(view.HasError);

        view.Text = "Hi";
        await view.SendAsync();

        Assert.False(view.HasError);
        Assert.Null(view.ErrorMessage);
    });

    [Fact]
    public void StopAsync_CancelsTheResponseAndKeepsTheUserMessage() => UIThread.Run(async () =>
    {
        var gate = Waiters.Signal();
        var client = new ScriptedChatClient().ReplyAfter(gate.Task, "Partial", " never");
        var view = ChatHost.Create(ChatHost.Session(client));

        view.Text = "Long question";
        var send = view.SendAsync();
        await UIThread.Until(() => view.IsBusy);

        view.StopCommand.Execute(null);
        await send;
        await UIThread.Until(() => !view.IsBusy);

        Assert.False(view.CanStop);
        Assert.Equal(string.Empty, view.Text);
        Assert.False(view.HasError);
        Assert.Equal("Response stopped.", view.Composer.LastAnnouncement);
        Assert.IsType<UserMessageRow>(view.Rows()[0]);
        Assert.DoesNotContain(view.Rows(), row => row is ThinkingRow or TurnErrorRow);
        Assert.Empty(view.Session!.Turns[0].ResponseBlocks);
        Assert.Single(view.Rows());
    });

    [Fact]
    public void StreamingContent_DoesNotNotifyTheComposer() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient();
        var stream = client.ReplyLive();
        var view = ChatHost.Create(ChatHost.Session(client));

        view.Text = "Hi";
        var send = view.SendAsync();
        stream.Text("a");
        await UIThread.Until(() => view.Rows()[^1] is BlockRow);

        var stateChanges = 0;
        view.SendCommand.CanExecuteChanged += (_, _) => stateChanges++;
        for (var i = 0; i < 20; i++)
            stream.Text("x");
        await UIThread.Until(() => ((TextContentBlock)((BlockRow)view.Rows()[^1]).Block).RawText.Length == 21);

        Assert.Equal(0, stateChanges);

        stream.Complete();
        await send;
        Assert.True(stateChanges > 0);
    });

    [Fact]
    public void IsInputEnabled_GatesSendingAndPicking() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient();
        var picker = new FakeAttachmentPicker();
        var view = ChatHost.Create(ChatHost.Session(client), v =>
        {
            v.AllowAttachments = true;
            v.AttachmentPicker = picker;
            v.IsInputEnabled = false;
        });

        view.Text = "Hi";
        Assert.False(view.CanSend);
        Assert.False(view.PickAttachmentsCommand.CanExecute(null));
        await view.SendAsync();
        await view.PickAttachmentsAsync();

        Assert.Empty(client.Requests);
        Assert.Equal(0, picker.CallCount);

        view.IsInputEnabled = true;
        Assert.True(view.CanSend);
        Assert.True(view.PickAttachmentsCommand.CanExecute(null));
    });

    [Fact]
    public void SendCommand_TracksWhetherThereIsSomethingToSend()
    {
        var view = ChatHost.Create(ChatHost.Session(new ScriptedChatClient()));
        var changes = 0;
        view.SendCommand.CanExecuteChanged += (_, _) => changes++;

        view.Text = "Hi";

        Assert.True(view.SendCommand.CanExecute(null));
        Assert.True(changes > 0);
    }

    [Fact]
    public void SwappingSessionsWhileSending_LeavesTheNewDraftAlone() => UIThread.Run(async () =>
    {
        var gate = Waiters.Signal();
        var first = ChatHost.Session(new ScriptedChatClient().WaitThenReply(gate.Task, "Late"));
        var second = ChatHost.Session(new ScriptedChatClient());
        var view = ChatHost.Create(first);

        view.Text = "to first";
        var send = view.SendAsync();
        await UIThread.Until(() => view.IsBusy);

        view.Session = second;
        Assert.False(view.IsBusy);
        view.Text = "draft for second";
        Assert.True(view.CanSend);

        gate.SetResult();
        await send;

        Assert.Equal("draft for second", view.Text);
        Assert.True(view.CanSend);
        Assert.False(view.HasError);
    });

    [Fact]
    public void SwappingSessions_DiscardsTheDraftAndAttachments()
    {
        var view = ChatHost.Create(ChatHost.Session(new ScriptedChatClient()));
        view.Text = "draft";
        view.AddAttachment(Blocks.Pdf());

        view.Session = ChatHost.Session(new ScriptedChatClient());

        Assert.Equal(string.Empty, view.Text);
        Assert.Empty(view.Attachments);
    }

    [Fact]
    public void Attachments_AreStagedAsRemovableChips()
    {
        var view = ChatHost.Create(ChatHost.Session(new ScriptedChatClient()), v => v.AllowAttachments = true);
        var pdf = Blocks.Pdf("plan.pdf");

        Assert.True(view.AddAttachment(pdf));

        Assert.True(view.HasAttachments);
        Assert.Same(pdf, Assert.Single(view.Attachments));
        Assert.True(view.CanSend);
        var remove = view.ByAutomationId<Button>("AIChatRemoveAttachment");
        Assert.Contains(view.Descendants<Label>(), label => label.Text == "plan.pdf");

        remove.Command.Execute(remove.CommandParameter);

        Assert.Empty(view.Attachments);
        Assert.False(view.HasAttachments);
        Assert.DoesNotContain(view.Descendants<Button>(), button => button.AutomationId == "AIChatRemoveAttachment");
        Assert.False(view.RemoveAttachment(pdf));
    }

    [Fact]
    public void Attachments_RespectThePerFileLimit()
    {
        var view = ChatHost.Create(ChatHost.Session(new ScriptedChatClient()), v => v.MaximumAttachmentBytes = 1024 * 1024);

        Assert.False(view.AddAttachment(Blocks.Pdf(bytes: 1024 * 1024 + 1)));

        Assert.Empty(view.Attachments);
        Assert.Equal("Attachments must be 1 MB or smaller.", view.ErrorMessage);
    }

    [Fact]
    public void Attachments_RespectTheCountAndTotalLimits()
    {
        var view = ChatHost.Create(ChatHost.Session(new ScriptedChatClient()), v =>
        {
            v.MaximumAttachmentCount = 2;
            v.MaximumAttachmentBytes = 30L * 1024 * 1024;
        });

        Assert.True(view.AddAttachment(Blocks.Pdf("a.pdf")));
        Assert.True(view.AddAttachment(Blocks.Pdf("b.pdf")));
        Assert.False(view.AddAttachment(Blocks.Pdf("c.pdf")));
        Assert.Equal(2, view.Attachments.Count);
        Assert.Equal("You can attach up to 2 files and 50 MB in total.", view.ErrorMessage);

        view.RemoveAttachment(view.Attachments[0]);
        view.RemoveAttachment(view.Attachments[0]);
        Assert.True(view.AddAttachment(Blocks.Pdf("big1.pdf", bytes: 26 * 1024 * 1024)));
        Assert.False(view.AddAttachment(Blocks.Pdf("big2.pdf", bytes: 26 * 1024 * 1024)));
        Assert.Single(view.Attachments);
    }

    [Fact]
    public void AddAttachment_WithNull_Throws()
    {
        var view = new AIChatView();

        Assert.Throws<ArgumentNullException>(() => view.AddAttachment(null!));
        Assert.Throws<ArgumentNullException>(() => view.RemoveAttachment(null!));
    }

    [Fact]
    public void PickAttachmentsAsync_StagesWhatThePickerReturns() => UIThread.Run(async () =>
    {
        var picker = new FakeAttachmentPicker();
        var fileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>> { [DevicePlatform.Unknown] = ["pdf"] });
        picker.Result.Add(Blocks.Pdf("one.pdf"));
        picker.Result.Add(Blocks.Png("two.png"));
        var view = ChatHost.Create(ChatHost.Session(new ScriptedChatClient()), v =>
        {
            v.AllowAttachments = true;
            v.AttachmentPicker = picker;
            v.AttachmentFileTypes = fileTypes;
            v.MaximumAttachmentBytes = 4096;
        });

        await view.PickAttachmentsAsync();

        Assert.Same(fileTypes, picker.SeenFileTypes);
        Assert.Equal(4096, picker.SeenLimit);
        Assert.Equal(["one.pdf", "two.png"], view.Attachments.Select(a => a.Name));
        Assert.False(view.HasError);
    });

    [Fact]
    public void PickAttachmentsAsync_PickerFailure_ShowsAGenericError() => UIThread.Run(async () =>
    {
        var picker = new FakeAttachmentPicker { Error = new IOException("disk on fire") };
        var view = ChatHost.Create(ChatHost.Session(new ScriptedChatClient()), v => v.AttachmentPicker = picker);

        await view.PickAttachmentsAsync();

        Assert.Equal(ChatComposer.AttachmentErrorMessage, view.ErrorMessage);
        Assert.DoesNotContain("disk on fire", view.ErrorMessage);
        var label = Assert.Single(view.Descendants<Label>(), label => label.Text == ChatComposer.AttachmentErrorMessage);
        Assert.True(label.IsVisible);
    });

    [Fact]
    public void PickAttachmentsAsync_TooLargeFile_NamesTheLimit() => UIThread.Run(async () =>
    {
        var picker = new FakeAttachmentPicker { Error = new AttachmentTooLargeException("huge.bin") };
        var view = ChatHost.Create(ChatHost.Session(new ScriptedChatClient()), v =>
        {
            v.AttachmentPicker = picker;
            v.MaximumAttachmentBytes = 2 * 1024 * 1024;
        });

        await view.PickAttachmentsAsync();

        Assert.Equal("Attachments must be 2 MB or smaller.", view.ErrorMessage);
    });

    [Fact]
    public void PickAttachmentsAsync_Cancelled_ShowsNoError() => UIThread.Run(async () =>
    {
        var picker = new FakeAttachmentPicker { Error = new OperationCanceledException() };
        var view = ChatHost.Create(ChatHost.Session(new ScriptedChatClient()), v => v.AttachmentPicker = picker);
        view.MaximumAttachmentBytes = 1;
        view.AddAttachment(Blocks.Pdf());
        Assert.True(view.HasError);

        await view.PickAttachmentsAsync();

        Assert.False(view.HasError);
        Assert.Empty(view.Attachments);
    });

    [Fact]
    public void PickAttachmentsAsync_WhilePicking_BlocksSendingAndIgnoresNullEntries() => UIThread.Run(async () =>
    {
        var picker = new FakeAttachmentPicker { Deferred = Waiters.Signal<IReadOnlyList<DataContent>>() };
        var view = ChatHost.Create(ChatHost.Session(new ScriptedChatClient()), v =>
        {
            v.AllowAttachments = true;
            v.AttachmentPicker = picker;
        });
        view.Text = "Hi";

        var pick = view.PickAttachmentsAsync();
        Assert.False(view.CanSend);
        Assert.False(view.PickAttachmentsCommand.CanExecute(null));

        picker.Deferred.SetResult([null!, Blocks.Pdf("kept.pdf")]);
        await pick;

        Assert.Equal("kept.pdf", Assert.Single(view.Attachments).Name);
        Assert.True(view.CanSend);
    });

    [Fact]
    public void AttachmentPicker_DefaultsToThePlatformPickerWithoutConfiguration()
    {
        var view = ChatHost.Create(ChatHost.Session(new ScriptedChatClient()), v => v.AllowAttachments = true);

        Assert.Same(FileChatAttachmentPicker.Default, view.GetAttachmentPicker());
        Assert.True(view.PickAttachmentsCommand.CanExecute(null));
        Assert.True(view.ByAutomationId<Button>("AIChatAttachButton").IsVisible);
    }

    [Fact]
    public void AttachmentPicker_PrefersThePropertyThenServices()
    {
        var registered = new FakeAttachmentPicker();
        var view = new AIChatView
        {
            ServiceProviderOverride = new ServiceCollection().AddSingleton<IChatAttachmentPicker>(registered).BuildServiceProvider(),
        };
        Assert.Same(registered, view.GetAttachmentPicker());

        var explicitPicker = new FakeAttachmentPicker();
        view.AttachmentPicker = explicitPicker;
        Assert.Same(explicitPicker, view.GetAttachmentPicker());
    }

    [Fact]
    public void AttachButton_UsesTheRegisteredPicker() => UIThread.Run(async () =>
    {
        var registered = new FakeAttachmentPicker();
        registered.Result.Add(Blocks.Pdf("from-di.pdf"));
        var view = ChatHost.Create(ChatHost.Session(new ScriptedChatClient()), v =>
        {
            v.ServiceProviderOverride = new ServiceCollection().AddSingleton<IChatAttachmentPicker>(registered).BuildServiceProvider();
            v.AllowAttachments = true;
        });

        view.ByAutomationId<Button>("AIChatAttachButton").Command.Execute(null);
        await UIThread.Until(() => view.HasAttachments);

        Assert.Equal(1, registered.CallCount);
        Assert.Equal("from-di.pdf", Assert.Single(view.Attachments).Name);
    });

    [Fact]
    public void AttachButton_IsHiddenUnlessAttachmentsAreAllowed()
    {
        var view = ChatHost.Create(ChatHost.Session(new ScriptedChatClient()));

        Assert.False(view.ByAutomationId<Button>("AIChatAttachButton").IsVisible);
        Assert.False(view.PickAttachmentsCommand.CanExecute(null));
    }

    [Fact]
    public void DefaultPicker_RejectsANonPositiveLimit() => UIThread.Run(async () =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => FileChatAttachmentPicker.Default.PickAsync(null, 0)));

    [Fact]
    public void StreamLimits_StopReadingOnceTheLimitIsExceeded() => UIThread.Run(async () =>
    {
        Assert.Equal(10, (await StreamLimits.ReadAsync(new MemoryStream(new byte[10]), 10, default))!.Length);
        Assert.Null(await StreamLimits.ReadAsync(new MemoryStream(new byte[11]), 10, default));
    });
}
