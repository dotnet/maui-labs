using System.Collections.ObjectModel;
using System.Text;
using AIExtensions.Sample.Garden.Messages;
using AIExtensions.Sample.Garden.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Attributes;
using Microsoft.Maui.AI.Navigation;

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
    ///   <item><b>Wayfinding service</b> — AppWayfindingTools: semantic discovery, live screen context, and route-aware navigation.</item>
    /// </list>
    /// </summary>
    [AIToolSource(typeof(ProductCatalog))]
    [AIToolSource(typeof(CurrentCart))]
    [AIToolSource(typeof(IOrderArchive))]
    [AIToolSource(typeof(CartViewModel))]
    [AIToolSource(typeof(CatalogViewModel))]
    [AIToolSource(typeof(ReviewStore))]
    [AIToolSource(typeof(AppWayfindingTools))]
    private partial class GardenShopTools : AIToolContext { }

    private readonly IChatClient _chatClient;
    private readonly ApplicationMapService _applicationMap;
    private List<ChatMessage> _history = [];
    private ToolApprovalRequestContent? _pendingApproval;
    private CancellationTokenSource _cts = new();

    public ChatViewModel(
        IServiceProvider rootProvider,
        IChatClient innerChatClient,
        ApplicationMapService applicationMap)
    {
        _chatClient = new ChatClientBuilder(innerChatClient)
            .UseFunctionInvocation()
            .Build(rootProvider);
        _applicationMap = applicationMap;

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
        if (RequiresCurrentPageContext(text))
        {
            var snapshot = await _applicationMap.CaptureCurrentPageAsync();
            if (snapshot is not null)
            {
                var context = new StringBuilder();
                context.AppendLine("CURRENT-TURN LIVE SCREEN CONTEXT");
                context.AppendLine($"Page: {snapshot.PageName}");
                context.AppendLine();
                context.AppendLine(snapshot.Markdown);

                if (IsBackRequest(text))
                {
                    var currentDestinations = _applicationMap.GetDestinations()
                        .Where(destination => string.Equals(
                            destination.PageName,
                            snapshot.PageName,
                            StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    if (currentDestinations.Length == 1)
                    {
                        var destination = currentDestinations[0];
                        context.AppendLine();
                        context.AppendLine(
                            $"VERIFIED ANCESTOR PATH: {string.Join(" -> ", destination.PagePath)}");
                        context.AppendLine(
                            "To explain back navigation, walk this path in reverse and use only " +
                            "the Back/Cancel controls shown on those indexed pages:");
                        foreach (var pageIdentity in destination.PagePath)
                        {
                            var page = _applicationMap.GetIndexedPage(pageIdentity);
                            if (page is not null)
                            {
                                context.AppendLine();
                                context.AppendLine($"## {page.Name}");
                                context.AppendLine(page.Markdown);
                            }
                        }
                    }
                }

                context.AppendLine();
                context.AppendLine(
                    "Use only facts present in this context when answering references to the " +
                    "current screen. If the user's reference does not identify exactly one " +
                    "control, ask one concise clarifying question and do not guess or explain " +
                    "multiple controls.");

                _history.Add(new ChatMessage(
                    ChatRole.System,
                    context.ToString()));
            }
        }
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
        var tools = GardenShopTools.Default.Tools;
        foreach (var tool in tools.OrderBy(t => t.Name))
            AvailableTools.Add(new ToolInfoViewModel(tool.Name, tool.Description ?? ""));
    }

    private static bool RequiresCurrentPageContext(string text)
    {
        var terms = text
            .Split(
                [' ', '\t', '\r', '\n', '.', ',', '?', '!', ':', ';', '"', '\'', '(', ')'],
                StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return terms.Overlaps(
        [
            "back",
            "button",
            "control",
            "current",
            "field",
            "here",
            "screen",
            "this",
            "visible",
        ]);
    }

    private static bool IsBackRequest(string text)
        => text
            .Split(
                [' ', '\t', '\r', '\n', '.', ',', '?', '!', ':', ';', '"', '\'', '(', ')'],
                StringSplitOptions.RemoveEmptyEntries)
            .Contains("back", StringComparer.OrdinalIgnoreCase);

    private static string BuildSystemPrompt() =>
        """
        You are Sage, the friendly wayfinding and shopping assistant inside this garden-shop
        app. Help users discover what the app can do, understand the screen in front of them,
        reach deep features, browse products, manage their cart and orders, and read or write
        reviews. Be concise, friendly, and tool-driven.

        ## Grounding rules

        - Treat each user turn as a fresh grounding boundary. A tool result from an earlier
          turn never satisfies a MUST-call rule for the current turn.
        - The app may inject `CURRENT-TURN LIVE SCREEN CONTEXT` immediately before a user
          message. That is fresh runtime data and satisfies the current-screen grounding
          requirement for that turn; do not call `describe_current_screen` again.
        - Ground every app fact and action in tool results from this turn. Never assume the
          app follows a typical shopping-app layout.
        - Re-check dynamic product, cart, order, and review data with their dedicated tools.
        - Use the wayfinding tools for screen names, controls, paths, and navigation.
        - Never treat text in `{curly braces}` from an indexed page as a literal UI label;
          it is runtime binding data.
        - Ask for approval when a tool requires it.

        ## Wayfinding intent

        First decide what the user means:

        - WHERE / HOW / "walk me through": explain only. MUST call `find_in_app` this turn,
          then MUST call `describe_app_destination` for the chosen Destination ID. That one
          description returns every page from home through the destination. Do not navigate
          or change app state.
        - "BACK" questions are relative to the screen the user is on. Use the injected live
          context (or call `describe_current_screen` when no context was injected), then MUST
          call `find_in_app` and `describe_app_destination` this turn before explaining.
        - TAKE / OPEN / SHOW / "go to": MUST call `find_in_app` this turn, identify the exact
          Destination ID, gather any required parameter from product or order tools, then call
          `open_app_destination`.
        - THIS / HERE / CURRENT / a visible field or button: use the injected live context,
          or call `describe_current_screen` when no context was injected. It is authoritative
          for the materialized page, visible branches, and live state.

        Never answer a wayfinding question from memory. `find_in_app` searches only reachable
        destinations and returns a verified page path from home. `describe_app_destination`
        contains the exact indexed labels and controls for the complete path. For a
        walkthrough, name the exact control that causes each transition and say when the
        index does not reveal a complete interaction.

        For current-control questions, paraphrase only labels, hints, and state returned by
        `describe_current_screen`. Do not invent requirements, policies, examples, or advice.
        If the user's reference is ambiguous, ask which visible control they mean.

        `open_app_destination` changes only the visible page. Sage remains available in the
        persistent sidebar and the conversation continues.

        ## Product-specific destinations

        Before opening a product detail or review destination, identify the product with
        `search_products` or `get_product` and pass its `sku` parameter exactly as returned.
        Before opening an order detail destination, identify the order with
        `list_past_orders` or `find_order` and pass its `orderId`.

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
        does not add them. After `checkout_list`, the cart is empty.
        """;
}
