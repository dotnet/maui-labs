// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ClientModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;
using Xunit.Abstractions;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// Runs the engine against provider streams recorded from Azure OpenAI. Set <c>MAUI_AI_CHAT_LIVE=1</c> to run
/// the same scenarios against the live deployment configured in the shared <c>ai-attributes-secrets</c> user
/// secrets (<c>AI:Endpoint</c>, <c>AI:ApiKey</c>, optional <c>AI:DeploymentName</c>) or the matching
/// <c>AI_ENDPOINT</c>/<c>AI_API_KEY</c>/<c>AI_DEPLOYMENT_NAME</c> environment variables. Set
/// <c>MAUI_AI_CHAT_RECORD=1</c> to run against the live deployment and write each test's baseline from what it
/// streams; filter the run to the tests whose baselines you mean to write.
/// </summary>
public class UIAgentRecordedTests
{
    private readonly ILoggerFactory _loggerFactory;

    public UIAgentRecordedTests(ITestOutputHelper output)
    {
        _loggerFactory = new XunitLoggerFactory(output);
    }

    private static IChatClient CreateClient(string baseline, [CallerFilePath] string testFile = "")
    {
        var record = IsSet("MAUI_AI_CHAT_RECORD");
        if (!record && !IsSet("MAUI_AI_CHAT_LIVE"))
            return RecordedChatClient.Load(baseline);

        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<UIAgentRecordedTests>(optional: true)
            .Build();
        var endpoint = Environment.GetEnvironmentVariable("AI_ENDPOINT") ?? configuration["AI:Endpoint"];
        var apiKey = Environment.GetEnvironmentVariable("AI_API_KEY") ?? configuration["AI:ApiKey"];
        var deployment = Environment.GetEnvironmentVariable("AI_DEPLOYMENT_NAME")
            ?? configuration["AI:DeploymentName"]
            ?? "gpt-5.4-mini";
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Live mode requires AI:Endpoint and AI:ApiKey (user secrets or environment).");

        var live = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey))
            .GetChatClient(deployment)
            .AsIChatClient();
        return record
            ? new RecordingChatClient(live, Path.Combine(Path.GetDirectoryName(testFile)!, "..", "Baselines", baseline))
            : live;

        static bool IsSet(string variable) =>
            string.Equals(Environment.GetEnvironmentVariable(variable), "1", StringComparison.Ordinal);
    }

    [Fact]
    public async Task TextStreaming_SingleTurn_ProducesTextContentBlock()
    {
        const string baseline = "TextStreaming_SingleTurn.recording.json";
        var client = CreateClient(baseline);
        var agent = new UIAgent(client, configure: null, _loggerFactory);

        var blocks = new List<ContentBlock>();
        await foreach (var block in agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "Say hello in one sentence.")))
        {
            blocks.Add(block);
        }

        var assistantBlocks = blocks.Where(b => b.Role == ChatRole.Assistant).ToList();
        Assert.NotEmpty(assistantBlocks);
        var textBlock = Assert.IsType<TextContentBlock>(assistantBlocks[0]);
        Assert.False(string.IsNullOrEmpty(textBlock.RawText));
        Assert.Equal(BlockLifecycleState.Inactive, textBlock.LifecycleState);
    }

    [Fact]
    public async Task TextStreaming_MultiTurn_ProducesBlocksPerTurn()
    {
        const string baseline = "TextStreaming_MultiTurn.recording.json";
        var client = CreateClient(baseline);
        var agent = new UIAgent(client, configure: null, _loggerFactory);

        // Turn 1
        var turn1Blocks = new List<ContentBlock>();
        await foreach (var block in agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "Say hello in one sentence.")))
        {
            turn1Blocks.Add(block);
        }

        var text1 = turn1Blocks.OfType<TextContentBlock>()
            .First(b => b.Role == ChatRole.Assistant);
        Assert.False(string.IsNullOrEmpty(text1.RawText));
        Assert.Equal(BlockLifecycleState.Inactive, text1.LifecycleState);

        // Turn 2
        var turn2Blocks = new List<ContentBlock>();
        await foreach (var block in agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "Now say goodbye in one sentence.")))
        {
            turn2Blocks.Add(block);
        }

        var text2 = turn2Blocks.OfType<TextContentBlock>()
            .First(b => b.Role == ChatRole.Assistant);
        Assert.False(string.IsNullOrEmpty(text2.RawText));
        Assert.Equal(BlockLifecycleState.Inactive, text2.LifecycleState);
    }

    [Fact]
    public async Task ToolCall_ProducesFunctionInvocationBlock()
    {
        const string baseline = "ToolCall_BackendTool.recording.json";
        var client = CreateClient(baseline);

        var tool = AIFunctionFactory.Create(GetWeather);
        var chatClient = client.AsBuilder()
            .UseFunctionInvocation(_loggerFactory)
            .Build();
        var agent = new UIAgent(chatClient, new ChatOptions { Tools = [tool] }, _loggerFactory);

        var blocks = new List<ContentBlock>();
        await foreach (var block in agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "What's the weather in Seattle?")))
        {
            blocks.Add(block);
        }

        var assistantBlocks = blocks.Where(b => b.Role == ChatRole.Assistant).ToList();

        // FunctionInvokingChatClient handles the tool call loop internally.
        // It yields the tool call updates, invokes the tool, then yields the text response.
        Assert.NotEmpty(assistantBlocks);

        // Should have a FunctionInvocationContentBlock for the tool call
        var toolBlock = assistantBlocks.OfType<FunctionInvocationContentBlock>().FirstOrDefault();
        Assert.NotNull(toolBlock);
        Assert.Equal("GetWeather", toolBlock!.ToolName);

        // Should have text content after the tool call completes
        var hasText = assistantBlocks.OfType<TextContentBlock>().Any(b => !string.IsNullOrEmpty(b.RawText));
        Assert.True(hasText, $"Expected text content. Block types: {string.Join(", ", assistantBlocks.Select(b => b.GetType().Name))}");
    }

    [Fact]
    public async Task ToolApproval_ProducesApprovalBlockAndResumes()
    {
        const string baseline = "ToolApproval_HumanInLoop.recording.json";
        var client = CreateClient(baseline);

        var tool = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(DeleteFile));
        var chatClient = client.AsBuilder()
            .UseFunctionInvocation(_loggerFactory)
            .Build();
        var agent = new UIAgent(chatClient, options => options.ChatOptions = new ChatOptions { Tools = [tool] }, _loggerFactory);

        var context = new AgentContext(agent);
        ToolApprovalBlock? approvalBlock = null;
        context.OnStatusChanged(s =>
        {
            if (s == ConversationStatus.AwaitingInput)
            {
                var turn = context.Turns[^1];
                approvalBlock = turn.ResponseBlocks.OfType<ToolApprovalBlock>().FirstOrDefault();
                approvalBlock?.Approve();
            }
        });

        await context.SendMessageAsync("Delete the file named test.txt");

        Assert.NotNull(approvalBlock);
        Assert.Equal(ApprovalStatus.Approved, approvalBlock!.Status);
        Assert.Equal("DeleteFile", Assert.IsAssignableFrom<FunctionInvocationContentBlock>(approvalBlock.InnerBlock).ToolName);

        // After approval, should have continued to produce text
        var lastTurn = context.Turns[^1];
        var textBlock = lastTurn.ResponseBlocks.OfType<TextContentBlock>().LastOrDefault();
        Assert.NotNull(textBlock);
        Assert.False(string.IsNullOrEmpty(textBlock!.RawText));
    }

    [Theory]
    [InlineData(true, "ToolApproval_BatchedWithPlainCall_Approved.recording.json")]
    [InlineData(false, "ToolApproval_BatchedWithPlainCall_Rejected.recording.json")]
    public async Task ToolApproval_BatchedWithACallThatNeedsNone_AsksOnlyAboutTheCallThatNeedsIt(bool approve, string baseline)
    {
        // The model calls both tools in one response. Function invocation then asks about both calls, and marks the
        // one whose tool needs no approval; the engine shows that call as a plain call and approves it itself, so the
        // user decides only about the deletion. Once they do, the weather call runs either way, with the deletion when
        // it is approved, and the model hears both outcomes.
        var provider = new ProviderCapture(CreateClient(baseline));
        var chatClient = provider.AsBuilder()
            .UseFunctionInvocation(_loggerFactory)
            .Build();
        AITool[] tools = [AIFunctionFactory.Create(GetWeather), new ApprovalRequiredAIFunction(AIFunctionFactory.Create(DeleteFile))];
        var agent = new UIAgent(chatClient, options => options.ChatOptions = new ChatOptions { Tools = tools }, _loggerFactory);
        var context = new AgentContext(agent);
        var waiting = new List<string>();
        using var decide = context.OnStatusChanged(status =>
        {
            if (status != ConversationStatus.AwaitingInput)
                return;

            var blocks = context.Turns[^1].ResponseBlocks;
            waiting.AddRange(blocks.Where(block => block.Role == ChatRole.Assistant).Select(Describe));
            var approval = blocks.OfType<ToolApprovalBlock>().Single();
            if (approve)
                approval.Approve();
            else
                approval.Reject("Keep the file.");
        });

        await context.SendMessageAsync(
            "Get the weather in Seattle and delete the file named test.txt. Call both tools in the same response.");

        var batch = provider.Responses[0].OfType<FunctionCallContent>().ToList();
        Assert.Equal(["DeleteFile", "GetWeather"], batch.Select(call => call.Name).Order());
        Assert.Equal(["approval of DeleteFile, pending", "call of GetWeather, no result"], waiting.Order());

        Assert.Equal(ConversationStatus.Idle, context.Status);
        var turn = context.Turns[^1];
        var approval = Assert.Single(turn.ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal(("DeleteFile", approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected), (approval.ToolName, approval.Status));
        var weather = Assert.Single(turn.ResponseBlocks.OfType<FunctionInvocationContentBlock>());
        Assert.Equal("GetWeather", weather.ToolName);
        Assert.StartsWith("The weather in Seattle", weather.Result?.Result?.ToString(), StringComparison.Ordinal);
        var deletion = approve ? "File 'test.txt' has been deleted." : RejectedCall.Result("Keep the file.");
        Assert.Equal(deletion, approval.Result?.Result?.ToString());
        Assert.False(string.IsNullOrWhiteSpace(turn.ResponseBlocks.OfType<TextContentBlock>().LastOrDefault()?.RawText));

        Assert.Equal(2, provider.Requests.Count);
        var results = provider.Requests[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>()
            .ToDictionary(result => result.CallId);
        Assert.Equal(2, results.Count);
        Assert.StartsWith("The weather in Seattle", results[batch.Single(call => call.Name == "GetWeather").CallId].Result?.ToString(), StringComparison.Ordinal);
        Assert.Equal(deletion, results[batch.Single(call => call.Name == "DeleteFile").CallId].Result?.ToString());

        static string Describe(ContentBlock block) => block switch
        {
            ToolApprovalBlock approval => $"approval of {approval.ToolName}, {approval.Status.ToString().ToLowerInvariant()}",
            FunctionInvocationContentBlock call => $"call of {call.ToolName}, {(call.HasResult ? "result" : "no result")}",
            _ => block.GetType().Name,
        };
    }

    [Description("Get the current weather for a city.")]
    private static string GetWeather([Description("The city name")] string city)
    {
        return $"The weather in {city} is 72°F and sunny.";
    }

    [Description("Delete a file by name.")]
    private static string DeleteFile([Description("The file name to delete")] string fileName)
    {
        return $"File '{fileName}' has been deleted.";
    }

    /// <summary>
    /// Keeps what function invocation sends the provider, and the contents the provider streams back as they arrive:
    /// function invocation changes the updates it receives in place, such as replacing calls with approval requests.
    /// </summary>
    private sealed class ProviderCapture(IChatClient innerClient) : DelegatingChatClient(innerClient)
    {
        public List<List<ChatMessage>> Requests { get; } = [];

        public List<List<AIContent>> Responses { get; } = [];

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add([.. messages]);
            var contents = new List<AIContent>();
            Responses.Add(contents);
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
            {
                contents.AddRange(update.Contents);
                yield return update;
            }
        }
    }
}
