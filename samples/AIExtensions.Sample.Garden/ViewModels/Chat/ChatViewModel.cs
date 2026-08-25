using System.Collections.ObjectModel;
using AIExtensions.Sample.Garden.Messages;
using AIExtensions.Sample.Garden.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Attributes;

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
    [AIToolSource(typeof(AppVisualTools))]
    private partial class GardenShopTools : AIToolContext { }

    private readonly IChatClient _chatClient;
    private readonly AppWayfindingTools _wayfindingTools;
    private List<ChatMessage> _history = [];
    private ToolApprovalRequestContent? _pendingApproval;
    private CancellationTokenSource _cts = new();

    public ChatViewModel(
        IServiceProvider rootProvider,
        IChatClient innerChatClient,
        AppWayfindingTools wayfindingTools)
    {
        _chatClient = new ChatClientBuilder(innerChatClient)
            .UseFunctionInvocation()
            .Build(rootProvider);
        _wayfindingTools = wayfindingTools;

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
            await AddCurrentContextToolResultsAsync(text);
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

    private async Task AddCurrentContextToolResultsAsync(string text)
    {
        var terms = text
            .Split(
                [' ', '\t', '\r', '\n', '.', ',', '?', '!', ':', ';', '"', '\'', '(', ')'],
                StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var needsDetailedLocation = terms.Overlaps(
        [
            "back",
            "directions",
            "find",
            "guide",
            "here",
            "how",
            "route",
            "walk",
            "where",
        ]);
        var needsNavigationLocation = needsDetailedLocation || terms.Overlaps(
        [
            "go",
            "navigate",
            "open",
            "show",
            "take",
        ]);
        var needsCurrentPage = needsDetailedLocation || terms.Overlaps(
        [
            "button",
            "control",
            "current",
            "field",
            "here",
            "screen",
            "this",
            "visible",
        ]);

        if (needsNavigationLocation || needsCurrentPage)
        {
            var includePageUi = needsCurrentPage;
            AddCurrentContextToolResult(
                "get_current_app_state",
                await _wayfindingTools.GetCurrentAppStateAsync(includePageUi),
                new Dictionary<string, object?>
                {
                    ["includePageUi"] = includePageUi,
                });
        }
    }

    private void AddCurrentContextToolResult(
        string toolName,
        string result,
        Dictionary<string, object?> arguments)
    {
        var callId = Guid.NewGuid().ToString("N");
        _history.Add(new ChatMessage(
            ChatRole.Assistant,
            [new FunctionCallContent(callId, toolName, arguments)]));
        _history.Add(new ChatMessage(
            ChatRole.Tool,
            [new FunctionResultContent(callId, result)]));

        var message = AddMessage(ChatMessageKind.Tool, toolName, FluentIcons.Wrench);
        message.ToolArgs = string.Join(
            "\n",
            arguments.Select(argument => $"  {argument.Key}: {argument.Value}"));
        message.ToolResult = result;
    }

    private static string BuildSystemPrompt() =>
        """
        You are Sage, the friendly wayfinding and shopping assistant inside this garden-shop
        app. Help users discover what the app can do, understand the screen in front of them,
        reach deep features, browse products, manage their cart and orders, and read or write
        reviews. Be concise, friendly, and tool-driven.

        ## Grounding rules

        - Treat each user turn as a fresh grounding boundary. A tool result from an earlier
          turn never satisfies a MUST-call rule for the current turn.
        - Ground every app fact and action in tool results from this turn. Never assume the
          app follows a typical shopping-app layout.
        - When `get_current_app_state` includes page UI, the first direction step MUST name
          a control from that current page. Never start directions at home/main unless the
          current page is home/main. If the target path starts at home, first explain exactly
          how to return there from the current page.
        - Re-check dynamic product, cart, order, and review data with their dedicated tools.
        - Use the wayfinding tools for screen names, controls, paths, and navigation.
        - Never treat text in `{curly braces}` from an indexed page as a literal UI label;
          it is runtime binding data.
        - Ask for approval when a tool requires it.

        ## Wayfinding intent

        First decide what the user means:

        - WHERE / HOW / "walk me through": explain only. Use the preflight
          `get_current_app_state(includePageUi: true)` result, then call
          `search_app_ui` and `get_app_destination` for the chosen Destination ID. Start directions from the
          live current page, not from remembered chat state or home. Do not navigate or change
          app state.
        - "BACK" questions follow the same rule: use the detailed preflight result before
          describing Back/Cancel steps.
        - TAKE / OPEN / SHOW / "go to": use the preflight location results, call
          `search_app_ui`, identify the exact Destination ID, gather any required parameter from
          product or order tools, then call `navigate_to_app_destination`.
        - THIS / HERE / CURRENT / a visible field or button: use the preflight
          `get_current_app_state(includePageUi: true)` result. It is authoritative for the
          materialized page, visible branches, and live state.
        - CHART / IMAGE / DRAWING / GRAPH / VISUAL questions: call
          `describe_current_visual`. The structural UI index cannot read pixels painted inside
          a GraphicsView. On Orders, use targetAutomationId `OrderInsightsCharts`. If visual
          analysis fails, report that failure and do not infer chart contents from control
          names or prior messages.

        Never answer a wayfinding question from memory. `search_app_ui` searches only reachable
        destinations and returns a verified page path from home. `get_app_destination`
        contains only the destination path. Combine it with the current URI and optional page UI
        from `get_current_app_state` to produce from-here directions. For a walkthrough, name
        the exact control that causes each transition and say when the index does not reveal a
        complete interaction.

        For current-control questions, paraphrase only labels, hints, and state returned by
        `get_current_app_state`. Do not invent requirements, policies, examples, or advice.
        If the user's reference is ambiguous, ask which visible control they mean.

        `navigate_to_app_destination` changes only the visible page. Sage remains available in the
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
