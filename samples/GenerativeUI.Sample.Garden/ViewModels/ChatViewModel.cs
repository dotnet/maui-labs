using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Attributes;
using Microsoft.Maui.AI.GenerativeUI.OpenApi;

namespace GenerativeUI.Sample.Garden.ViewModels;

/// <summary>
/// The chat loop. Registers the generic OpenAPI server-API tools and lets the model discover and
/// call the Garden REST API to answer prompts. No generative UI yet — results are shown as text and
/// tool rows in the transcript.
/// </summary>
public sealed partial class ChatViewModel : ObservableObject
{
    /// <summary>
    /// Source-generated tool context. The generator scans <see cref="OpenApiExplorerTools"/> for
    /// <c>[ExportAIFunction]</c> methods and exposes them via <c>GardenApiTools.Default.Tools</c>. The
    /// instance is resolved from DI at invocation time.
    /// </summary>
    [AIToolSource(typeof(OpenApiExplorerTools))]
    private partial class GardenApiTools : AIToolContext { }

    private const string SystemPrompt =
        """
        You are a helpful assistant for an online garden shop. You have generic tools to explore and
        call the shop's REST API — you do NOT know its endpoints ahead of time.

        HOW TO WORK:
        - Discover the API first: call list_endpoints to see operations, and describe_endpoint /
          describe_model to learn parameters and shapes before calling.
        - Use read_api for GET operations (safe, e.g. listing products, viewing the cart).
        - Use write_api for changes (create/update/delete, checkout). These require user approval.
        - After a write, re-read the affected resource so you report the current server state
          (e.g. after adding to the cart, read the cart before summarizing it).
        - Pass path and query values as flat keys in args; put a request body under an explicit
          "body" key.

        Be concise. Summarize results in plain language; don't dump raw JSON at the user.
        """;

    private readonly IChatClient _chatClient;
    private readonly List<ChatMessage> _history = [];
    private ToolApprovalRequestContent? _pendingApproval;

    public ChatViewModel(IServiceProvider rootProvider, IChatClient innerChatClient)
    {
        _chatClient = new ChatClientBuilder(innerChatClient)
            .UseFunctionInvocation()
            .Build(rootProvider);

        _history.Add(new ChatMessage(ChatRole.System, SystemPrompt));

        foreach (var tool in GardenApiTools.Default.Tools.OrderBy(t => t.Name))
            AvailableTools.Add(tool.Name);
    }

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ObservableCollection<string> AvailableTools { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    public partial bool IsBusy { get; set; }

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    public partial string? InputText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInputVisible))]
    public partial bool IsApprovalPending { get; set; }

    public bool IsInputVisible => !IsApprovalPending;

    [ObservableProperty]
    public partial string ApprovalText { get; set; } = "";

    [RelayCommand]
    private async Task SendAsync()
    {
        var text = InputText?.Trim();
        if (string.IsNullOrWhiteSpace(text) || IsBusy)
            return;

        InputText = string.Empty;
        AddMessage(ChatMessageKind.User, text);
        _history.Add(new ChatMessage(ChatRole.User, text));

        await RunTurnAsync();
    }

    [RelayCommand]
    private Task ApproveAsync() => ResolveApprovalAsync(approved: true);

    [RelayCommand]
    private Task RejectAsync() => ResolveApprovalAsync(approved: false, reason: "User rejected");

    private async Task ResolveApprovalAsync(bool approved, string? reason = null)
    {
        if (_pendingApproval is null)
            return;

        var approval = _pendingApproval;
        _pendingApproval = null;
        IsApprovalPending = false;

        var response = approval.CreateResponse(approved, reason);
        _history.Add(new ChatMessage(ChatRole.User, [response]));
        AddMessage(ChatMessageKind.Tool, approved ? "✔ Approved" : "✘ Rejected");

        await RunTurnAsync();
    }

    private async Task RunTurnAsync()
    {
        IsBusy = true;
        try
        {
            var options = new ChatOptions { Tools = [.. GardenApiTools.Default.Tools] };
            await StreamResponseAsync(options);
        }
        catch (Exception ex)
        {
            AddMessage(ChatMessageKind.Error, $"Error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StreamResponseAsync(ChatOptions options)
    {
        var updates = new List<ChatResponseUpdate>();
        var toolRows = new Dictionary<string, ChatMessageViewModel>();
        ChatMessageViewModel? assistant = null;
        var assistantText = string.Empty;

        await foreach (var update in _chatClient.GetStreamingResponseAsync(_history, options))
        {
            updates.Add(update);

            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case ToolApprovalRequestContent approval:
                        _pendingApproval = approval;
                        break;

                    case FunctionCallContent call:
                        var row = AddMessage(ChatMessageKind.Tool, call.Name);
                        row.Detail = FormatArgs(call.Arguments);
                        if (call.CallId is not null)
                            toolRows[call.CallId] = row;
                        break;

                    case FunctionResultContent result:
                        if (result.CallId is not null && toolRows.TryGetValue(result.CallId, out var toolRow))
                            toolRow.Detail = Combine(toolRow.Detail, ResultText(result.Result));
                        break;

                    case TextContent tc when !string.IsNullOrEmpty(tc.Text):
                        assistantText += tc.Text;
                        if (assistant is null)
                            assistant = AddMessage(ChatMessageKind.Assistant, assistantText);
                        else
                            assistant.Text = assistantText;
                        break;
                }
            }
        }

        _history.AddMessages(updates);

        if (_pendingApproval is not null)
        {
            var name = _pendingApproval.ToolCall is FunctionCallContent fc ? fc.Name : "tool";
            ApprovalText = $"Allow {name}?";
            IsApprovalPending = true;
        }
        else if (assistant is null && string.IsNullOrEmpty(assistantText))
        {
            AddMessage(ChatMessageKind.Assistant, "(no response)");
        }
    }

    private ChatMessageViewModel AddMessage(ChatMessageKind kind, string text)
    {
        var vm = new ChatMessageViewModel(kind, text);
        Messages.Add(vm);
        return vm;
    }

    private static string FormatArgs(IDictionary<string, object?>? args)
        => args is null || args.Count == 0
            ? ""
            : string.Join("\n", args.Select(kv => $"{kv.Key}: {kv.Value}"));

    private static string Combine(string? args, string result)
        => string.IsNullOrEmpty(args) ? $"→ {result}" : $"{args}\n→ {result}";

    private static string ResultText(object? result) => result switch
    {
        null => "(null)",
        string s => s,
        _ => JsonSerializer.Serialize(result),
    };
}
