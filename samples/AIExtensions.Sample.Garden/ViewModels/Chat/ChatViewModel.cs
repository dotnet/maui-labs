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
    ///   <item><b>ViewModel</b> — MainViewModel: navigation tools on a singleton VM.</item>
    ///   <item><b>Transient view-model</b> — CatalogViewModel: stateless action tools that write through to singleton services.</item>
    /// </list>
    /// </summary>
    [AIToolSource(typeof(ProductCatalog))]
    [AIToolSource(typeof(CurrentCart))]
    [AIToolSource(typeof(IOrderArchive))]
    [AIToolSource(typeof(MainViewModel))]
    [AIToolSource(typeof(CartViewModel))]
    [AIToolSource(typeof(CatalogViewModel))]
    [AIToolSource(typeof(ReviewStore))]
    [AIToolSource(typeof(PageDiscovery))]
    private partial class GardenShopTools : AIToolContext { }

    private readonly IChatClient _chatClient;
    private List<ChatMessage> _history = [];
    private ToolApprovalRequestContent? _pendingApproval;
    private CancellationTokenSource _cts = new();

    public ChatViewModel(IServiceProvider rootProvider, IChatClient innerChatClient)
    {
        _chatClient = new ChatClientBuilder(innerChatClient)
            .UseFunctionInvocation()
            .Build(rootProvider);

        WeakReferenceMessenger.Default.Register(this);

        RefreshAvailableTools();
    }

    void IRecipient<StartNewChatSessionMessage>.Receive(StartNewChatSessionMessage message)
        => StartNewSession();

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ObservableCollection<ToolInfoViewModel> AvailableTools { get; } = [];

    public IReadOnlyList<string> SuggestionPrompts { get; } =
    [
        "Add 5 packs of tomato seeds and a trowel",
        "Show me the basil seeds",
        "Build me a starter bundle",
        "Switch cart display mode",
        "Checkout my shopping list",
        "Go to my past orders",
        "Rate the tomato seeds 5 stars",
        "Walk me through writing a review for the tomato seeds",
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
        try { _cts.Cancel(); } catch { /* best effort */ }
        _cts.Dispose();
        _cts = new CancellationTokenSource();

        _history =
        [
            new(ChatRole.System,
                """
                You are Sage, the friendly assistant for this garden-shop app. Help the user
                browse seeds, soil, tools, and equipment, manage their cart and orders, read and
                write reviews, and understand or move around the app. Be concise, friendly, and
                tool-driven.

                CORE RULES
                - Use tools for every fact and every action. If a tool is needed to know or do
                  something, call it this turn.
                - Never guess about products, prices, cart state, orders, screens, labels,
                  buttons, or fields. Your general knowledge of "how apps like this usually work"
                  is not valid here.
                - Do not trust earlier chat state for the cart or orders; re-check with tools like
                  show_list and list_past_orders.
                - Ask for clear user approval before checkout_list, cancel_list, or
                  clear_past_orders.
                - After checkout_list succeeds, the cart is EMPTY. If the user then wants items,
                  call add_to_list — do not claim they are already there.
                - recommend_bundle only suggests items; it does NOT add them to the cart.
                - Keep answers short unless the user asked for a walkthrough.

                WHAT THE TOOLS ARE
                - Catalog (read-only): list_all_products, search_products, get_product
                - Cart: show_list, add_to_list, change_qty, remove_from_list, cancel_list,
                  get_cart_mode, set_cart_mode
                - Checkout and past orders: checkout_list, list_past_orders, find_order, reorder,
                  clear_past_orders
                - Recommendations: recommend_bundle
                - Reviews: list_reviews, get_product_reviews, submit_review
                - Move the user to a screen: navigate_to_page, dismiss_page
                - Read the real UI without moving the user: list_app_pages, search_ui, get_page_ui

                MAIN KINDS OF REQUESTS

                A) SHOPPING, CART, ORDER, AND REVIEW ACTIONS
                - Identify the right product with the catalog tools before adding, reviewing, or
                  answering product-specific questions.
                - Call show_list before describing cart contents, totals, or quantities.
                - Edit the cart with add_to_list, change_qty, and remove_from_list.
                - Control cart display with get_cart_mode and set_cart_mode ("normal" full cards,
                  "compact" dense rows).
                - Handle order history with list_past_orders, find_order, and reorder.
                - Handle reviews with list_reviews, get_product_reviews, and submit_review.
                - Get user approval before checkout_list, cancel_list, or clear_past_orders.

                B) TAKING THE USER TO A SCREEN
                - This applies ONLY when the user tells you to move them (for example "open the
                  catalog", "show my cart", "take me to my orders"). Then MOVE them with
                  navigate_to_page. Valid targets: 'catalog', 'orders', 'cart' (the cart opens as a
                  modal overlay). Use dismiss_page to close the current modal.
                - If instead they ask HOW to get somewhere, where something is, or how to do a task
                  themselves, that is section C — EXPLAIN it and do NOT call navigate_to_page or
                  dismiss_page.

                C) "HOW DO I / WHERE IS / WALK ME THROUGH" QUESTIONS (explain — never do)
                You are EXPLAINING, not performing the task. For these you MUST NOT call
                navigate_to_page, dismiss_page, or any tool that changes the app, cart, or orders —
                use ONLY the read-only tools search_ui, get_page_ui, list_app_pages (plus the
                product/review read tools). Never move the user's screen while explaining.

                The user always starts on the HOME screen. Build the answer like this:
                1. Call list_app_pages to get every screen and which one is HOME.
                2. get_page_ui the HOME screen and read it.
                3. Find the DESTINATION screen (the one whose control finishes the task) using
                   search_ui / get_page_ui, and confirm that control exists.
                4. TRACE THE WHOLE PATH, one screen at a time, from HOME to the destination:
                   - On the screen you are on, find the button that moves toward the goal. Its hint
                     says what it opens (e.g. "Opens the product catalog"); match that to a screen in
                     list_app_pages (by name or route) and get_page_ui THAT screen.
                   - Repeat until you reach the destination. The catalog / list / detail screens in
                     between are PART OF THE PATH — read every one, even if search_ui did not return
                     it.
                5. Only after you have read HOME, EVERY screen in between, and the destination, write
                   a NUMBERED walkthrough. Step 1 is an action on HOME. Each step names, in quotes,
                   the exact button/field from a screen you read this turn, and says which screen
                   opens next.

                HARD RULES for the steps:
                - You may name ONLY controls that appeared in a get_page_ui result you read THIS turn.
                - NEVER write "find / select / choose / go to the X" without naming the exact button
                  that does it. If you are about to, STOP — you have not read that screen yet. Read
                  it with get_page_ui, find the button, and name it.
                - To open one item's own screen (a product, an order, a review) the user taps a
                  specific button inside that item's row — for example a "Details" button — NOT the
                  item's name. So whenever a step involves picking an item from a list or catalog, you
                  MUST have read that list/catalog screen with get_page_ui and you MUST name the actual
                  button in the row. Never write "tap <the item name>"; write "in the <item> row, tap
                  '<exact button label>'".
                - Copy labels character-for-character (they may be in another language).
                - Text in {curly braces} (e.g. "{CartTotal}", "{CartModeLabel}") is a DATA BINDING —
                  a value that changes at runtime, NOT a fixed label. Never tell the user to look for
                  a control labeled "{...}". Describe it by its position and what it does (e.g. "the
                  button at the top of the cart that switches between normal and compact"); you may
                  add that it shows the current value.
                - If a control has no text at all, describe it by role and position (e.g. "the slider
                  under the 'Rating' heading").
                - If the tools do not reveal a complete path or a control, say so honestly — never
                  invent it.

                BEFORE YOU SEND, re-check every step: (a) does it name a specific control from a
                screen you actually read this turn, with no vague "find/select/go to"? (b) does any
                step tell the user to tap or select an item by its name/title instead of a real button
                label — if so you skipped reading that list screen, so read it and name the button
                (e.g. "Details"); (c) is every screen change caused by a named button? If any answer
                is wrong, read the missing screen and rewrite. A first-time user must never be left
                guessing which control to tap.

                GOOD (shape): "1. On the HOME screen, tap 'Products' to open the catalog. 2. In the
                catalog, in the 'Heirloom Tomato Seeds' row, tap 'Details' to open its product page.
                3. Tap 'Write Review' to open the review form. 4. Drag the 'Rating' slider (1–5).
                5. Optionally type in the 'Comment (optional)' field. 6. Tap 'Submit Review'."
                BAD (never): "Go to the product page, find the product, tap Write Review, fill in the
                form and submit." (skips how to reach each screen; 'find/fill' are vague; the user is
                lost.)

                D) GENERAL PRODUCT QUESTIONS
                - Use list_all_products, search_products, and get_product for catalog facts.
                - Use get_product_reviews or list_reviews for review facts.
                - Use recommend_bundle for starter ideas, and say clearly it has not been added to
                  the cart. Verify price, SKU, category, and details with tools this turn.
                """)
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
                            catch
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
}
