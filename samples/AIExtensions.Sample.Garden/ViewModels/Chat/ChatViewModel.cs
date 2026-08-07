using System.Collections.ObjectModel;
using AIExtensions.Sample.Garden.Chat;
using AIExtensions.Sample.Garden.Messages;
using AIExtensions.Sample.Garden.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Attributes;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.Garden.ViewModels;

/// <summary>
/// Hosts the AI chat for the garden shop over the reusable <see cref="AgentContext"/> engine. The
/// message loop, streaming, tool invocation, and approval flow all live in the engine + controls; this
/// view model only configures the agent (tools, instructions, custom block handlers), exposes the
/// <see cref="Session"/> for the chat controls to bind to, and surfaces sample chrome (suggestions,
/// the available-tools list for the empty state, and the fancy/plain view toggle state).
/// </summary>
public sealed partial class ChatViewModel : ObservableObject,
    IRecipient<StartNewChatSessionMessage>,
    IRecipient<ChatBlockPreviewModeChangedMessage>
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
    private partial class GardenShopTools : AIToolContext { }

    private const string SystemPrompt =
        """
        You are a helpful garden-shop assistant named Sage. Help the user browse seeds, soil,
        tools, and equipment, manage their cart, and review past orders.

        IMPORTANT RULES:
        - Always use tools to perform actions. Never assume you know the cart state
          from previous messages — call show_list to check.
        - Use search_products to discover items by name or category. Use get_product to look
          up a single item. The app renders product results as cards, so a short sentence plus
          the tool call is enough — do not re-list every field in prose.
        - Use recommend_bundle when the user asks for a starter kit, gift set, or curated bundle idea.
        - When the user says "check out", call checkout_list (which requires approval).
        - Use list_past_orders to see past orders, and find_order to look up one order by its id.
          The app renders a found order as a receipt card, so a short sentence plus the tool call
          is enough — do not re-list every line item in prose.
        - After checkout clears the cart, the cart is EMPTY. If the user asks to add
          items again, always call add_to_list — do not say items are already there.

        NAVIGATION:
        - Use navigate_to_page("catalog") to browse the product catalog.
        - Use navigate_to_page("orders") to see past orders.
        - Use navigate_to_page("cart") to view the cart.
        - Use dismiss_page() to close a modal and return to chat.

        CART DISPLAY:
        - Use set_cart_mode("normal") or set_cart_mode("compact") to change the cart view.

        REVIEWS:
        - Use submit_review to add a product review with a rating and comment.
        - Use get_product_reviews to see reviews for a specific product.
        - Use list_reviews to see all reviews.

        FORMATTING:
        - Format text answers with **bold** for emphasis and "- " bullets for lists.

        IMAGES:
        - When the user asks you to draw, generate, or picture something, use image generation.

        Be concise and friendly.
        """;

    private bool _turnActive;

    private readonly IChatClient _chatClient;

    // The handler mode of the current Session. Switching mode requires a new session (handlers are baked
    // into the pipeline), so this is tracked here and compared against incoming new-session requests.
    private bool _useCustomHandlers = true;

    public ChatViewModel(IChatClient chatClient)
    {
        _chatClient = chatClient;

        // A single live session. Its handler set is baked into the pipeline, so switching handler mode
        // recreates the session (see the StartNewChatSessionMessage handler) rather than holding several.
        Session = CreateSession(_useCustomHandlers);

        WeakReferenceMessenger.Default.RegisterAll(this);

        RefreshAvailableTools();
    }

    /// <summary>
    /// Builds a fresh <see cref="AgentContext"/> over the shared chat client. With
    /// <paramref name="useCustomHandlers"/> the custom Garden handlers are registered; otherwise the
    /// pipeline uses only the built-ins, so tools surface as raw <c>FunctionInvocationContentBlock</c>s.
    /// Demonstrates that a different handler set is just a different agent — created ad-hoc, no engine
    /// changes.
    /// </summary>
    private AgentContext CreateSession(bool useCustomHandlers)
    {
        // Image generation is always available: the hosted tool lets the model produce images inline,
        // and MauiProgram wires the matching UseImageGeneration middleware beneath function invocation
        // so the image streams back as DataContent and renders through the built-in MediaContentTemplate.
        var tools = new List<AITool>(GardenShopTools.Default.Tools)
        {
            new HostedImageGenerationTool(),
        };

        var agent = new UIAgent(_chatClient, options =>
        {
            options.ChatOptions = new ChatOptions
            {
                Instructions = SystemPrompt,
                Tools = [.. tools],
            };

            if (useCustomHandlers)
            {
                // Assistant text becomes rich formatted text; product lookups aggregate into a carousel/card.
                options.AddBlockHandler(new GardenFormattedTextHandler());
                options.AddBlockHandler(new ProductResultsHandler());
                // Registers generated 1:1 handlers such as OrderSummaryBlock. Aggregate and text
                // projections above remain handwritten because they intentionally span multiple events.
                options.AddGeneratedToolBlocks();
            }
        });

        var context = new AgentContext(agent);
        context.RegisterOnStatusChanged(OnStatusChanged);
        return context;
    }

    /// <summary>The single live conversation the chat control binds to.</summary>
    [ObservableProperty]
    public partial AgentContext Session { get; set; }

    /// <summary>Tools surfaced in the empty-state grid so the user can see what Sage can do.</summary>
    public ObservableCollection<ToolInfoViewModel> AvailableTools { get; } = [];

    /// <summary>
    /// Rendering axis: the designed views vs the raw block-preview inspector. The chat view's template
    /// Style swaps its content templates via a data trigger on this flag. Driven by the header toggle via
    /// <see cref="ChatBlockPreviewModeChangedMessage"/>.
    /// </summary>
    [ObservableProperty]
    public partial bool IsPreview { get; set; }

    /// <summary>Starter prompts shown as suggestion chips.</summary>
    public IReadOnlyList<string> SuggestionPrompts { get; } =
    [
        "Add 5 packs of tomato seeds and a trowel",
        "Show me the basil seeds",
        "Draw a watercolor of a thriving vegetable garden",
        "Compare the tomato and pepper seeds",
        "Build me a starter bundle",
        "Switch cart display mode",
        "Checkout my shopping list",
        "Go to my past orders",
        "Rate the tomato seeds 5 stars",
    ];

    /// <summary>
    /// Starts a fresh conversation. When the requested handler mode matches the current session, just
    /// clear it; when it differs, recreate the session with the new handler set (a new, empty
    /// conversation). This single path serves both the "new chat" button and the handler toggle.
    /// </summary>
    void IRecipient<StartNewChatSessionMessage>.Receive(StartNewChatSessionMessage message)
    {
        if (message.UseCustomHandlers == _useCustomHandlers)
        {
            Session.Clear();
        }
        else
        {
            _useCustomHandlers = message.UseCustomHandlers;
            var old = Session;
            Session = CreateSession(_useCustomHandlers);
            old.Dispose();
        }
    }

    void IRecipient<ChatBlockPreviewModeChangedMessage>.Receive(ChatBlockPreviewModeChangedMessage message) =>
        IsPreview = message.IsPreview;

    private void OnStatusChanged(ConversationStatus status)
    {
        if (status is ConversationStatus.Streaming or ConversationStatus.AwaitingInput)
        {
            _turnActive = true;
            return;
        }

        // A turn just finished (idle/error). Notify listeners so orders/cart panes can refresh.
        // Guarded by _turnActive so clearing the session doesn't fire a spurious completion.
        if (status is ConversationStatus.Idle or ConversationStatus.Error && _turnActive)
        {
            _turnActive = false;
            MainThread.BeginInvokeOnMainThread(() =>
                WeakReferenceMessenger.Default.Send(new ChatTurnCompletedMessage()));
        }
    }

    private void RefreshAvailableTools()
    {
        AvailableTools.Clear();
        foreach (var tool in GardenShopTools.Default.Tools.OrderBy(t => t.Name))
            AvailableTools.Add(new ToolInfoViewModel(tool.Name, tool.Description ?? ""));
    }
}
