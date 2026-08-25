using System.Collections.ObjectModel;
using AIExtensions.Sample.Garden.Messages;
using AIExtensions.Sample.Garden.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Attributes;
using Microsoft.Maui.AI.Wayfinding;

namespace AIExtensions.Sample.Garden.ViewModels;

/// <summary>
/// Owns the AI chat loop, message history, tool invocation, and approval flow.
/// Designed to be reusable — any page can host a ChatView bound to this VM.
/// </summary>
public sealed partial class ChatViewModel : ObservableObject, IRecipient<StartNewChatSessionMessage>
{
    /// <summary>
    /// Source-generated tool context that merges all tool sources into one.
    /// Demonstrates several distinct attribute patterns:
    /// <list type="bullet">
    ///   <item><b>Static class</b> — ProductCatalog: tools on a plain static class.</item>
    ///   <item><b>Instance class</b> — CurrentCart: tools on a DI-registered instance.</item>
    ///   <item><b>Interface</b> — IOrderArchive: tools declared on the interface.</item>
    ///   <item><b>Transient view-model</b> — CatalogViewModel: stateless action tools that write through to singleton services.</item>
    /// </list>
    /// </summary>
    [AIToolSource(typeof(ProductCatalog))]
    [AIToolSource(typeof(CurrentCart))]
    [AIToolSource(typeof(IOrderArchive))]
    [AIToolSource(typeof(CartViewModel))]
    [AIToolSource(typeof(CatalogViewModel))]
    [AIToolSource(typeof(ReviewStore))]
    private partial class GardenShopTools : AIToolContext { }

    private readonly IChatClient _chatClient;
    private readonly MauiWayfindingOptions _wayfindingOptions;
    private List<ChatMessage> _history = [];
    private ToolApprovalRequestContent? _pendingApproval;
    private CancellationTokenSource _cts = new();

    public ChatViewModel(
        IServiceProvider rootProvider,
        IChatClient innerChatClient,
        MauiWayfindingOptions wayfindingOptions)
    {
        _chatClient = new ChatClientBuilder(innerChatClient)
            .UseMauiWayfinding()
            .UseFunctionInvocation()
            .Build(rootProvider);
        _wayfindingOptions = wayfindingOptions;

        WeakReferenceMessenger.Default.Register(this);

        RefreshAvailableTools();
    }

    void IRecipient<StartNewChatSessionMessage>.Receive(StartNewChatSessionMessage message)
        => StartNewSession();

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ObservableCollection<ToolInfoViewModel> AvailableTools { get; } = [];

    public IReadOnlyList<string> SuggestionPrompts { get; } =
    [
        "Where can I write a review?",
        "Take me to the page where I can review basil seeds",
        "Where are my past orders?",
        "What is this field for?",
        "How do I get back to the catalog?",
        "What do these charts show?",
        "Add 5 packs of tomato seeds and a trowel",
        "Build me a starter bundle",
        "Checkout my shopping list",
    ];

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

    public void StartNewSession()
    {
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();

        _history =
        [
            new(ChatRole.System, BuildSystemPrompt())
        ];

        Messages.Clear();
        _pendingApproval = null;
        IsApprovalPending = false;
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        var text = InputText?.Trim();
        if (string.IsNullOrWhiteSpace(text) || IsBusy)
            return;

        InputText = string.Empty;
        IsBusy = true;

        AddMessage(ChatMessageKind.User, text);
        _history.Add(new ChatMessage(ChatRole.User, text));

        try
        {
            var options = new ChatOptions { Tools = [.. GardenShopTools.Default.Tools] };
            await SendAndProcessResponseAsync(options);
        }
        catch (Exception ex)
        {
            AddMessage(ChatMessageKind.Error, $"Error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            WeakReferenceMessenger.Default.Send(new ChatTurnCompletedMessage());
        }
    }

    [RelayCommand]
    private async Task ApproveAsync() => await ResolveApprovalAsync(approved: true);

    [RelayCommand]
    private async Task RejectAsync() => await ResolveApprovalAsync(approved: false, reason: "User rejected");

    [RelayCommand]
    private async Task RunSuggestionAsync(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt) || IsBusy)
            return;
        InputText = prompt;
        await SendAsync();
    }

    private async Task SendAndProcessResponseAsync(ChatOptions options)
    {
        var responseText = string.Empty;
        ChatMessageViewModel? assistantMessage = null;
        var updates = new List<ChatResponseUpdate>();
        // Track tool call messages by CallId so we can attach results
        var toolCallMessages = new Dictionary<string, ChatMessageViewModel>();

        await foreach (var update in _chatClient.GetStreamingResponseAsync(_history, options, _cts.Token))
        {
            updates.Add(update);

            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case ToolApprovalRequestContent approval:
                        {
                            var toolName = approval.ToolCall is FunctionCallContent fcc ? fcc.Name : "unknown";
                            var args = approval.ToolCall is FunctionCallContent fc && fc.Arguments is not null
                                ? string.Join(", ", fc.Arguments.Select(kv => $"{kv.Key}: {kv.Value}"))
                                : "";
                            var msg = AddMessage(ChatMessageKind.Tool, $"Approval required: {toolName}({args})", FluentIcons.LockClosed);
                            msg.ToolArgs = args;
                            _pendingApproval = approval;
                            break;
                        }

                    case FunctionCallContent call:
                        {
                            var argsText = call.Arguments is not null
                                ? string.Join("\n", call.Arguments.Select(kv => $"  {kv.Key}: {kv.Value}"))
                                : "";
                            var msg = AddMessage(ChatMessageKind.Tool, call.Name, FluentIcons.Wrench);
                            msg.ToolArgs = argsText;
                            if (call.CallId is not null)
                                toolCallMessages[call.CallId] = msg;
                            break;
                        }

                    case FunctionResultContent result:
                        {
                            // Serialize result to JSON for display (ToString() gives type names for collections)
                            string resultText;
                            try
                            {
                                resultText = result.Result switch
                                {
                                    null => "(null)",
                                    string s => s,
                                    _ => System.Text.Json.JsonSerializer.Serialize(result.Result,
                                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true })
                                };
                            }
                            catch (NotSupportedException)
                            {
                                resultText = result.Result?.ToString() ?? "";
                            }
                            if (result.CallId is not null && toolCallMessages.TryGetValue(result.CallId, out var toolMsg))
                            {
                                toolMsg.ToolResult = resultText;
                            }
                            break;
                        }

                    case TextContent tc when tc.Text is not null:
                        responseText += tc.Text;
                        if (assistantMessage is null)
                            assistantMessage = AddMessage(ChatMessageKind.Assistant, responseText);
                        else
                            assistantMessage.Text = responseText;
                        break;
                }
            }
        }

        _history.AddMessages(updates);

        if (_pendingApproval is not null)
        {
            var name = _pendingApproval.ToolCall is FunctionCallContent fc2 ? fc2.Name?.TrimEnd('(', ')') : "tool";
            ApprovalText = $"{name} — approve?";
            IsApprovalPending = true;
            return;
        }

        if (assistantMessage is null && string.IsNullOrEmpty(responseText))
            AddMessage(ChatMessageKind.Assistant, "(no response)");
    }

    private async Task ResolveApprovalAsync(bool approved, string? reason = null)
    {
        if (_pendingApproval is null)
            return;

        var approval = _pendingApproval;
        _pendingApproval = null;
        IsApprovalPending = false;
        IsBusy = true;

        try
        {
            var response = approval.CreateResponse(approved, reason);
            _history.Add(new ChatMessage(ChatRole.User, [response]));
            AddMessage(ChatMessageKind.Tool, approved ? "Approved" : "Rejected", approved ? FluentIcons.Checkmark : FluentIcons.Dismiss);

            var options = new ChatOptions { Tools = [.. GardenShopTools.Default.Tools] };
            await SendAndProcessResponseAsync(options);
        }
        catch (Exception ex)
        {
            AddMessage(ChatMessageKind.Error, $"Error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            WeakReferenceMessenger.Default.Send(new ChatTurnCompletedMessage());
        }
    }

    private ChatMessageViewModel AddMessage(ChatMessageKind kind, string text, string? icon = null)
    {
        var vm = new ChatMessageViewModel(kind, text, icon);
        Messages.Add(vm);
        WeakReferenceMessenger.Default.Send(new ChatMessageAddedMessage(vm));
        return vm;
    }

    private void RefreshAvailableTools()
    {
        AvailableTools.Clear();
        var tools = GardenShopTools.Default.Tools
            .Concat(MauiWayfindingChatClientBuilderExtensions.GetTools(_wayfindingOptions))
            .GroupBy(tool => tool.Name, StringComparer.Ordinal)
            .Select(group => group.First());
        foreach (var tool in tools.OrderBy(t => t.Name))
            AvailableTools.Add(new ToolInfoViewModel(tool.Name, tool.Description ?? ""));
    }

    private static string BuildSystemPrompt() =>
        """
        You are Sage, the friendly wayfinding and shopping assistant inside this garden-shop
        app. Help users discover what the app can do, understand the screen in front of them,
        reach deep features, browse products, manage their cart and orders, and read or write
        reviews. Be concise, friendly, and tool-driven.

        ## Shopping actions

        - Catalog: `list_all_products`, `search_products`, `get_product`
        - Cart: `show_list`, `add_to_list`, `change_qty`, `remove_from_list`,
          `cancel_list`, `get_cart_mode`, `set_cart_mode`
        - Checkout/orders: `checkout_list`, `list_past_orders`, `find_order`, `reorder`,
          `clear_past_orders`
        - Recommendations: `recommend_bundle`
        - Reviews: `list_reviews`, `get_product_reviews`, `submit_review`

        Use action tools when the user asks Sage to perform a shopping operation. Call
        `show_list` before describing cart contents. `recommend_bundle` suggests items but
        does not add them. After `checkout_list`, the cart is empty. Ask for approval when a
        tool requires it.
        """;
}
