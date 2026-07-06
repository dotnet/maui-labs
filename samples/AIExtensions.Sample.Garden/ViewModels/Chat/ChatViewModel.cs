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
/// <see cref="Session"/> for a <c>CopilotChatView</c> to bind to, and surfaces sample chrome
/// (suggestions, the available-tools list for the empty state, and a fancy/plain template toggle).
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

    public ChatViewModel(IChatClient chatClient)
    {
        // Image generation is always available: the hosted tool lets the model produce images inline,
        // and MauiProgram wires the matching UseImageGeneration middleware beneath function invocation
        // so the image streams back as DataContent and renders through the built-in MediaContentTemplate.
        var tools = new List<AITool>(GardenShopTools.Default.Tools)
        {
            new HostedImageGenerationTool(),
        };

        var agent = new UIAgent(chatClient, options =>
        {
            options.ChatOptions = new ChatOptions
            {
                Instructions = SystemPrompt,
                Tools = [.. tools],
            };
            // Assistant text becomes rich formatted text; product lookups aggregate into a carousel/card.
            options.AddBlockHandler(new GardenFormattedTextHandler());
            options.AddBlockHandler(new ProductResultsHandler());
        });

        Session = new AgentContext(agent);
        Session.RegisterOnStatusChanged(OnStatusChanged);

        WeakReferenceMessenger.Default.Register(this);

        RefreshAvailableTools();
    }

    /// <summary>The stateful conversation a <c>CopilotChatView</c> binds to.</summary>
    public AgentContext Session { get; }

    /// <summary>Tools surfaced in the empty-state grid so the user can see what Sage can do.</summary>
    public ObservableCollection<ToolInfoViewModel> AvailableTools { get; } = [];

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

    void IRecipient<StartNewChatSessionMessage>.Receive(StartNewChatSessionMessage message) =>
        Session.Clear();

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
