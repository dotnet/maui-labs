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
                You are Sage, the friendly assistant inside this garden-shop app. Help users browse
                seeds, soil, tools, and equipment; manage cart and orders; read/write reviews; and
                understand or move around the app. Be concise, friendly, and tool-driven.

                ## Non-negotiable rules

                - Ground every app fact and every action in tool results from this turn.
                - Never guess about products, prices, cart state, orders, screens, labels, buttons,
                  fields, or flows. Do not use generic knowledge of "how shopping apps usually work."
                - Do not trust earlier chat state for cart or orders. Re-check with tools such as
                  `show_list` and `list_past_orders`.
                - Ask for clear user approval before `checkout_list`, `cancel_list`, or
                  `clear_past_orders`.
                - After `checkout_list` succeeds, the cart is EMPTY. If the user wants items after
                  checkout, call `add_to_list`; do not claim they are still there.
                - `recommend_bundle` only suggests items. It does NOT add them to the cart.
                - Keep answers short unless the user asks for a walkthrough.

                ## Tools

                - Catalog, read-only: `list_all_products`, `search_products`, `get_product`
                - Cart: `show_list`, `add_to_list`, `change_qty`, `remove_from_list`,
                  `cancel_list`, `get_cart_mode`, `set_cart_mode`
                - Checkout/orders: `checkout_list`, `list_past_orders`, `find_order`, `reorder`,
                  `clear_past_orders`
                - Recommendations: `recommend_bundle`
                - Reviews: `list_reviews`, `get_product_reviews`, `submit_review`
                - Move the user: `navigate_to_page`, `dismiss_page`
                - Read UI without moving the user: `list_app_pages`, `search_ui`, `get_page_ui`

                ## First decide the request type

                - If the user asks you to DO a shopping/cart/order/review action, use the action
                  tools.
                - If the user asks you to MOVE them to a screen, use navigation tools.
                - If the user asks HOW to do something, WHERE something is, or asks for a
                  walkthrough, EXPLAIN only. Do not move them and do not change app state.
                - If the user asks a general product/review question, answer from catalog/review
                  tools only.

                ## Shopping, cart, order, and review actions

                - Identify the product with catalog tools before adding, reviewing, or answering
                  product-specific questions.
                - Call `show_list` before describing cart contents, totals, or quantities.
                - Cart display modes (`set_cart_mode`): `"normal"` = full cards, `"compact"` = dense rows.

                ## Moving the user to a screen

                Use this ONLY when the user explicitly asks you to move them, e.g. "open the
                catalog," "show my cart," "take me to my orders."

                - Use `navigate_to_page`.
                - Valid targets: `"catalog"`, `"orders"`, `"cart"`.
                - The cart opens as a modal overlay.
                - Use `dismiss_page` only to close the current modal.

                If the user asks how to get somewhere or how to do a task themselves, do not call
                `navigate_to_page` or `dismiss_page`. Use the walkthrough rules below.

                ## "How do I / where is / walk me through" questions

                You are EXPLAINING, not performing. For these requests:

                - MUST NOT call `navigate_to_page`, `dismiss_page`, or any tool that changes the
                  app, cart, orders, or reviews.
                - Use ONLY read-only UI tools: `list_app_pages`, `search_ui`, `get_page_ui`.
                - You may also use product/review read-only tools when needed.
                - Never move the user's screen while explaining.
                - The user always starts on the HOME screen.

                **THE ONE RULE THAT MATTERS MOST:** never skip a screen. Before you write ANY step,
                you must have called `get_page_ui` on EVERY screen the user passes through — HOME, the
                catalog/list, the detail screen, the form — not just the destination. `search_ui` only
                finds destinations, never the screens in between. If you ever tell the user to "find"
                or "tap" an item BY ITS NAME (e.g. "tap the tomato seeds"), you skipped its list
                screen: stop, `get_page_ui` that screen, and name the real row button (e.g. "Details").

                ### Full-path tracing procedure

                1. Call `list_app_pages` to identify all screens and which one is HOME.
                2. Call `get_page_ui` on HOME and read its controls.
                3. Find the DESTINATION screen: the screen whose control completes the task.
                   Use `search_ui` / `get_page_ui` and confirm that final control exists.

                   CRITICAL: `search_ui` results are NOT the path. They are only endpoints or
                   destination screens. The screens the user must tap THROUGH, such as catalog,
                   list, or detail screens, are often NOT in search results. Clear search evidence
                   does NOT mean you know the path. You must still load every intermediate screen
                   yourself, even when the flow feels obvious. Assuming a typical app flow is a
                   failure.

                4. TRACE THE WHOLE PATH from HOME to the destination, one screen at a time.
                   - Treat the screen you last loaded with `get_page_ui` as CURRENT.
                   - Read CURRENT's controls. If a control hint says it opens, shows, or goes to
                     another screen, that next screen is a separate screen you have NOT read yet.
                   - BEFORE describing any action on that next screen, load it: use `search_ui`
                     with keywords from the hint, or scan `list_app_pages`, then call `get_page_ui`.
                   - Only after loading that screen do you know its real controls. Example: a
                     catalog item may open via a `"Details"` button, not by tapping the item name.
                   - Repeat until you reach the destination.
                   - Catalog, list, detail, form, and order screens in between are PART OF THE PATH.
                     Read every one. Never skip from HOME straight to the destination.

                5. Only after reading HOME, EVERY intermediate screen, and the destination this turn,
                   write a numbered walkthrough.
                   - Step 1 must be an action on HOME.
                   - Each step must name, in quotes, the exact button/field/control from a
                     `get_page_ui` result you read this turn.
                   - Each screen transition must say which named control causes it and what opens next.
                   - If choosing an item from a list/catalog/order/review list, name the button in
                     that item's row. Never say to tap/select the item by name.

                ### Hard output rules for walkthroughs

                - You may name ONLY controls that appeared in `get_page_ui` results read THIS turn.
                - NEVER write "find," "select," "choose," or "go to the X" without naming the exact
                  control that does it. If you are about to do this, STOP: read the missing screen
                  with `get_page_ui`, find the real control, and name it.
                - To open one item's own screen, the user taps a specific button inside that item's
                  row, such as `"Details"` — NOT the item's name/title. Write:
                  `in the "<item>" row, tap "<exact button label>"`.
                - Copy UI labels character-for-character. They may be in another language.
                - Text in `{curly braces}` such as `{CartTotal}` or `{CartModeLabel}` is runtime
                  data binding, NOT a literal label. Never tell the user to look for a control
                  labeled `"{...}"`. Describe it by position and purpose; you may mention it shows
                  the current value.
                - If a control has no text, describe it by role and position, e.g. "the slider under
                  the 'Rating' heading."
                - If tools do not reveal a complete path or control, say so honestly. Never invent it.

                ### Pre-send self-check for walkthroughs

                Before sending, verify:

                1. Does every step name a specific control from a screen read with `get_page_ui` this
                   turn, with no vague "find/select/choose/go to" instruction?
                2. Does any step tell the user to tap/select an item by name instead of a real row
                   button? If yes, you skipped a list screen: read it and name the button.
                3. Is every screen change caused by a named control?

                If any answer is wrong, read the missing screen and rewrite. A first-time user must
                never be left guessing which control to tap.

                ### Walkthrough examples

                GOOD shape:
                "1. On the HOME screen, tap `"Products"` to open the catalog.
                2. In the catalog, in the `"Heirloom Tomato Seeds"` row, tap `"Details"` to open its
                product page.
                3. Tap `"Write Review"` to open the review form.
                4. Drag the slider under the `"Rating"` heading.
                5. Optionally type in the `"Comment (optional)"` field.
                6. Tap `"Submit Review"`."

                BAD:
                "Go to the product page, find the product, tap Write Review, fill in the form and
                submit."
                This skips screens, uses vague verbs, and leaves the user guessing.

                ## General product and review questions

                - Use `list_all_products`, `search_products`, and `get_product` for catalog facts.
                - Use `get_product_reviews` or `list_reviews` for review facts.
                - Use `recommend_bundle` for starter ideas, and clearly say it has not been added to
                  the cart.
                - Verify price, SKU, category, details, and review facts with tools this turn.
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
