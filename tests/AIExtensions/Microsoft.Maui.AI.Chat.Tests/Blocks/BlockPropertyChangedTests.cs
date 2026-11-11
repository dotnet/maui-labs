// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.Pipeline;

namespace Microsoft.Maui.AI.Chat.Tests.Blocks;

/// <summary>
/// Blocks raise <see cref="INotifyPropertyChanged.PropertyChanged"/>, so a XAML template can bind to them:
/// built-in blocks name the properties that changed, and a block of the app's own type reports that
/// everything may have changed when its handler updates it.
/// </summary>
public class BlockPropertyChangedTests
{
    [Fact]
    public void RichContentBlock_AppendText_RaisesRawTextAndContentAfterTheTextIsAppended()
    {
        var block = new TextContentBlock();
        var seen = new List<string>();
        block.PropertyChanged += (_, e) => seen.Add($"{e.PropertyName}={block.RawText}/{block.Content.Count}");

        block.AppendText("Hello");
        block.AppendText("");
        block.AppendText(" world\n\nBye");

        Assert.Equal(
            ["RawText=Hello/1", "Content=Hello/1", "RawText=Hello world\n\nBye/2", "Content=Hello world\n\nBye/2"],
            seen);
    }

    [Fact]
    public void RichContentBlock_ReplacedByAProviderSnapshot_RaisesRawTextAndContent()
    {
        var block = new RichContentBlock();
        var names = Record(block);

        block.ReplaceContent("Hi", [new ParagraphNode()]);

        Assert.Equal(["RawText", "Content"], names);
    }

    [Fact]
    public void ReasoningContentBlock_RaisesTextProtectedDataAndIsEncryptedOnlyWhenTheyChange()
    {
        var block = new ReasoningContentBlock();
        var seen = new List<string>();
        block.PropertyChanged += (_, e) => seen.Add($"{e.PropertyName}:{block.IsEncrypted}");

        block.ProtectedData = "opaque";
        block.ProtectedData = "opaque";
        block.AppendText("Thinking");
        block.AppendText("");
        block.AppendText(" more");

        Assert.Equal(
            ["ProtectedData:True", "IsEncrypted:True", "Text:False", "IsEncrypted:False", "Text:False"],
            seen);
    }

    [Fact]
    public void FunctionInvocationContentBlock_RaisesTheCallAndResultWithWhatDependsOnThem()
    {
        var block = new FunctionInvocationContentBlock();
        var names = Record(block);

        block.Call = new FunctionCallContent("call-1", "GetWeather", new Dictionary<string, object?> { ["city"] = "Paris" });
        block.Result = new FunctionResultContent("call-1", "Sunny");
        block.Result = new FunctionResultContent("call-1", "Rainy");

        Assert.Equal(["Id", "Call", "ToolName", "Arguments", "Result", "HasResult", "Result"], names);
    }

    [Fact]
    public void ToolApprovalBlock_Approve_RaisesStatusAndIsAwaitingInputOnce()
    {
        var approval = CreateApproval();
        var seen = new List<string>();
        approval.PropertyChanged += (_, e) => seen.Add($"{e.PropertyName}:{approval.Status}:{approval.IsAwaitingInput}");
        var changes = 0;
        approval.OnChanged(() => changes++);

        approval.Approve();
        approval.Reject();

        Assert.Equal(["Status:Approved:False", "IsAwaitingInput:Approved:False"], seen);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ToolApprovalBlock_RaisesTheResultOfItsCall()
    {
        var approval = CreateApproval();
        var seen = new List<string>();
        approval.PropertyChanged += (_, e) => seen.Add($"{e.PropertyName}:{approval.HasResult}");

        approval.InnerBlock.Result = new FunctionResultContent("delete-1", "Deleted");

        Assert.Equal(["Result:True", "HasResult:True"], seen);
    }

    [Fact]
    public void ToolApprovalBlock_Abandoned_RaisesIsAwaitingInputWhenReported()
    {
        var approval = CreateApproval();
        var names = Record(approval);

        Assert.True(approval.Abandon());
        Assert.Empty(names);
        approval.ReportAwaitingInputChanged();
        approval.ReportAwaitingInputChanged();

        Assert.Equal(["IsAwaitingInput"], names);
        Assert.False(approval.IsAwaitingInput);
    }

    [Fact]
    public async Task UIActionBlock_ManualInvocation_RaisesIsAwaitingInputThenTheResult()
    {
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var action = CreateAction(
            AIFunctionFactory.Create(() => release.Task, "PickPhoto"),
            UIActionInvocationMode.Manual);
        var seen = new List<string>();
        action.PropertyChanged += (_, e) => seen.Add($"{e.PropertyName}:{action.IsComplete}:{action.IsAwaitingInput}");

        var invocation = action.InvokeAsync();
        release.SetResult("photo.jpg");
        await invocation.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(
            [
                "IsAwaitingInput:False:False",
                "Result:True:False",
                "HasResult:True:False",
                "IsComplete:True:False",
            ],
            seen);
    }

    [Fact]
    public async Task UIActionBlock_InvocationCanceledByItsToken_RaisesIsAwaitingInputAgain()
    {
        var action = CreateAction(
            AIFunctionFactory.Create(
                async (CancellationToken cancellationToken) =>
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    return "photo.jpg";
                },
                "PickPhoto"),
            UIActionInvocationMode.Manual);
        var seen = new List<string>();
        action.PropertyChanged += (_, e) => seen.Add($"{e.PropertyName}:{action.IsAwaitingInput}");
        using var cancellation = new CancellationTokenSource();

        var invocation = action.InvokeAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invocation);

        Assert.Equal(["IsAwaitingInput:False", "IsAwaitingInput:True"], seen);
    }

    [Fact]
    public async Task UIActionBlock_HandlerThatThrowsForTheResult_DoesNotKeepTheTurnWaiting()
    {
        var action = CreateAction(AIFunctionFactory.Create(() => "Map", "ShowMap"), UIActionInvocationMode.Manual);
        action.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UIActionBlock.Result))
                throw new InvalidOperationException("The view failed.");
        };

        var result = await action.InvokeAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Same(result, await action.GetResultAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Same(result, action.Result);
        Assert.True(action.IsComplete);
    }

    [Fact]
    public void UIActionBlock_WithACustomInnerBlockThatChanged_RaisesItsOwnPropertiesByName()
    {
        var inner = new CustomCallBlock { Call = new FunctionCallContent("map-1", "ShowMap") };
        var action = new UIActionBlock(
            AIFunctionFactory.Create(() => "Map", "ShowMap"),
            UIActionInvocationMode.Automatic,
            inner,
            services: null);
        var innerNames = Record(inner);
        var names = Record(action);

        inner.Changed();

        Assert.Equal([""], innerNames);
        Assert.Equal(["Call", "ToolName", "Arguments", "Result", "HasResult", "IsComplete"], names);
    }

    [Fact]
    public void ActivityContentBlock_RaisesActivityTypeAndContent()
    {
        var block = new ActivityContentBlock();
        var names = Record(block);

        block.ActivityType = "search";
        block.ActivityType = "search";
        block.Content = JsonDocument.Parse("""{"query":"maui"}""").RootElement;

        Assert.Equal(["ActivityType", "Content"], names);
    }

    [Fact]
    public void MediaContentBlock_RaisesContentWhenAnotherItemReplacesIt()
    {
        var first = new DataContent(new byte[] { 1 }, "image/png");
        var block = new MediaContentBlock(first);
        var names = Record(block);

        block.Content = first;
        block.Content = new UriContent("https://example.com/cat.png", "image/png");

        Assert.Equal(["Content"], names);
    }

    [Fact]
    public void ContentBlock_RaisesItsOwnPropertiesWhenTheyChange()
    {
        var block = new TextContentBlock();
        var names = Record(block);

        block.Id = "block-1";
        block.Id = "block-1";
        block.Role = ChatRole.Assistant;
        block.AuthorName = "Copilot";
        block.CreatedAt = DateTimeOffset.UnixEpoch;
        block.LifecycleState = BlockLifecycleState.Active;
        block.LifecycleState = BlockLifecycleState.Active;

        Assert.Equal(["Id", "Role", "AuthorName", "CreatedAt", "LifecycleState"], names);
    }

    [Fact]
    public void CustomBlock_SetProperty_RaisesTheNameOnlyWhenTheValueChanges()
    {
        var block = new ScoreBlock();
        var names = Record(block);

        block.Score = 1;
        block.Score = 1;
        Assert.True(block.SetScore(2));
        Assert.False(block.SetScore(2));
        block.Touch();

        Assert.Equal(["Score", "Score", "Score"], names);
    }

    [Fact]
    public void CustomBlock_NotifyChanged_RaisesAnEmptyPropertyNameBeforeItsCallbacks()
    {
        var block = new ScoreBlock();
        var seen = new List<string>();
        block.PropertyChanged += (_, e) => seen.Add($"PropertyChanged({e.PropertyName})");
        block.OnChanged(() => seen.Add("OnChanged"));

        block.Changed();

        Assert.Equal(["PropertyChanged()", "OnChanged"], seen);
    }

    [Fact]
    public async Task CustomBlockUpdatedByItsHandler_RaisesAnEmptyPropertyName()
    {
        var options = new UIAgentOptions();
        options.AddBlockHandler(CreateCitationHandler());
        var pipeline = new BlockMappingPipeline(options);
        var citation = Assert.IsType<CitationBlock>(Assert.Single(await ProcessAsync(pipeline, Citation("first"))));
        var names = Record(citation);
        var changes = 0;
        citation.OnChanged(() => changes++);

        Assert.Empty(await ProcessAsync(pipeline, Citation("second")));
        Assert.Equal([""], names);
        Assert.Equal("first; second", citation.Quote);

        pipeline.Finalize();

        Assert.Equal(["", "LifecycleState", ""], names);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task BuiltInBlockUpdatedByItsHandler_RaisesOnlyThePropertiesThatChanged()
    {
        var pipeline = new BlockMappingPipeline(new UIAgentOptions());
        var text = Assert.IsType<TextContentBlock>(Assert.Single(await ProcessAsync(pipeline, Text("Hel"))));
        var names = Record(text);
        var changes = 0;
        text.OnChanged(() => changes++);

        Assert.Empty(await ProcessAsync(pipeline, Text("lo")));
        pipeline.Finalize();

        Assert.Equal(["RawText", "Content", "LifecycleState"], names);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task ToolBlockOfTheAppsOwnType_RaisesAnEmptyPropertyNameWhenItsResultArrives()
    {
        var options = new UIAgentOptions();
        options.AddBlockHandler(new CustomCallHandler());
        var pipeline = new BlockMappingPipeline(options);
        var block = Assert.IsType<CustomCallBlock>(Assert.Single(await ProcessAsync(
            pipeline,
            Update(new FunctionCallContent("call-1", "GetWeather")))));
        var names = Record(block);

        Assert.Empty(await ProcessAsync(pipeline, Update(new FunctionResultContent("call-1", "Sunny"))));

        Assert.Equal(["Result", "HasResult", "LifecycleState", ""], names);
        Assert.Equal("Sunny", block.Forecast);
    }

    private static List<string?> Record(INotifyPropertyChanged source)
    {
        var names = new List<string?>();
        source.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        return names;
    }

    private static ToolApprovalBlock CreateApproval()
    {
        var call = new FunctionCallContent("delete-1", "DeleteFile");
        return new ToolApprovalBlock(
            new FunctionInvocationContentBlock { Call = call },
            new ToolApprovalRequestContent("approval-1", call));
    }

    private static UIActionBlock CreateAction(AIFunction function, UIActionInvocationMode mode)
        => new(
            function,
            mode,
            new FunctionInvocationContentBlock { Call = new FunctionCallContent("photo-1", function.Name) },
            services: null);

    private static async Task<List<ContentBlock>> ProcessAsync(BlockMappingPipeline pipeline, ChatResponseUpdate update)
    {
        var blocks = new List<ContentBlock>();
        await foreach (var block in pipeline.Process(update))
            blocks.Add(block);
        return blocks;
    }

    private static ChatResponseUpdate Text(string text)
        => new(ChatRole.Assistant, text) { MessageId = "message-1" };

    private static ChatResponseUpdate Update(AIContent content)
        => new(ChatRole.Assistant, [content]) { MessageId = "message-1" };

    private static ChatResponseUpdate Citation(string quote)
        => new()
        {
            Role = ChatRole.Assistant,
            MessageId = "message-1",
            RawRepresentation = quote,
        };

    private static DelegateBlockHandler<CitationBlock> CreateCitationHandler()
        => new((context, state) =>
        {
            if (context.Update.RawRepresentation is not string quote)
                return BlockMappingResult<CitationBlock>.Pass();

            context.MarkUpdateHandled();
            if (state.Quote.Length == 0)
            {
                state.Quote = quote;
                state.Id = "citation-1";
                return BlockMappingResult<CitationBlock>.Emit(state, state);
            }

            state.Quote += "; " + quote;
            return BlockMappingResult<CitationBlock>.Update(state);
        });

    private sealed class ScoreBlock : ContentBlock
    {
        private int _score;

        public int Score
        {
            get => _score;
            set => SetProperty(ref _score, value);
        }

        public bool SetScore(int value) => SetProperty(ref _score, value, nameof(Score));

        public void Touch() => OnPropertyChanged(nameof(Score));

        public void Changed() => NotifyChanged();
    }

    /// <summary>A typed tool card like a <c>[ToolBlock]</c>: its own properties change without notifying.</summary>
    private sealed class CustomCallBlock : FunctionInvocationContentBlock
    {
        public string? Forecast { get; set; }

        public void Changed() => NotifyChanged();
    }

    private sealed class CustomCallHandler : ContentBlockHandler<CustomCallBlock>
    {
        public override BlockMappingResult<CustomCallBlock> Handle(BlockMappingContext context, CustomCallBlock state)
        {
            foreach (var content in context.UnhandledContents)
            {
                if (state.Call is null && content is FunctionCallContent call)
                {
                    context.MarkHandled(call);
                    state.Call = call;
                    return BlockMappingResult<CustomCallBlock>.Emit(state, state);
                }

                if (state.Call is { } active && content is FunctionResultContent result && result.CallId == active.CallId)
                {
                    context.MarkHandled(result);
                    state.Result = result;
                    state.Forecast = result.Result?.ToString();
                    return BlockMappingResult<CustomCallBlock>.Complete();
                }
            }

            return BlockMappingResult<CustomCallBlock>.Pass();
        }
    }
}
