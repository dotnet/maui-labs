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
    ///   <item><b>Navigation service</b> — AINavigationService: route-aware navigate/get_routes/get_current_route.</item>
    /// </list>
    /// </summary>
    [AIToolSource(typeof(ProductCatalog))]
    [AIToolSource(typeof(CurrentCart))]
    [AIToolSource(typeof(IOrderArchive))]
    [AIToolSource(typeof(CartViewModel))]
    [AIToolSource(typeof(CatalogViewModel))]
    [AIToolSource(typeof(ReviewStore))]
    [AIToolSource(typeof(AINavigationService))]
    private partial class GardenShopTools : AIToolContext { }

    private readonly IChatClient _chatClient;
    private readonly ShellNavigationService _navigationService;
    private List<ChatMessage> _history = [];
    private ToolApprovalRequestContent? _pendingApproval;
    private CancellationTokenSource _cts = new();

    public ChatViewModel(IServiceProvider rootProvider, IChatClient innerChatClient, ShellNavigationService navigationService)
    {
        _chatClient = new ChatClientBuilder(innerChatClient)
            .UseFunctionInvocation()
            .Build(rootProvider);

        _navigationService = navigationService;

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
        "Open the product catalog",
        "Switch cart display mode",
        "Checkout my shopping list",
        "Go to my past orders",
        "Rate the tomato seeds 5 stars",
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

    private string BuildSystemPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("""
            You are a helpful garden-shop assistant. Help the user browse seeds, soil,
            tools, and equipment, manage their cart, and review past orders.

            IMPORTANT RULES:
            - Always use tools to perform actions. Never assume you know the cart state
              from previous messages — call show_list to check.
            - Use search_products to discover items by name or category.
            - Use recommend_bundle when the user asks for a starter kit, gift set, or curated bundle idea.
            - When the user says "check out", call checkout_list (which requires approval).
            - After checkout clears the cart, the cart is EMPTY. If the user asks to add
              items again, always call add_to_list — do not say items are already there.

            NAVIGATION:
            - When the user asks to "open", "show", "go to", or "see" a page, product,
              order, or review — ALWAYS use navigate(route) to open the actual page.
              Do NOT just list information in chat when the user wants to see a page.
            - Put parameter values directly in the path after the route that accepts them.
            - Use navigate("..") to go back, navigate("//main/chat") to go home.
            - You can call get_routes() to see all available routes and their parameters.

            CART DISPLAY:
            - Use set_cart_mode("normal") or set_cart_mode("compact") to change the cart view.

            REVIEWS:
            - Use submit_review to add a review via AI, or navigate to the review page UI.
            - Use get_product_reviews / list_reviews to read reviews.

            Be concise and friendly.
            """);

        // Dynamically inject the discovered route table with template-style URIs
        try
        {
            var routes = _navigationService.GetRoutes();
            if (routes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("AVAILABLE ROUTES (use with the navigate tool):");
                sb.AppendLine("Put parameter values inline in the path, right after the route segment.");
                sb.AppendLine();
                foreach (var route in routes)
                {
                    if (route.Parameters.Count > 0)
                    {
                        sb.AppendLine($"  {route.FullPath}/<{route.Parameters[0].QueryName}>");
                    }
                    else
                    {
                        sb.AppendLine($"  {route.FullPath}");
                    }
                }
                sb.AppendLine();
                sb.AppendLine("Examples:");
                sb.AppendLine("  navigate(\"//main/products\")                        → product catalog");
                sb.AppendLine("  navigate(\"//main/products/product/seed-tomato\")     → product detail for seed-tomato");
                sb.AppendLine("  navigate(\"//main/products/product/seed-basil/review\") → review page for basil");
                sb.AppendLine("  navigate(\"//main/orders/order/ORD-00001\")           → order detail");
                sb.AppendLine("  navigate(\"..\")  → go back");
                sb.AppendLine("  navigate(\"//main/chat\")  → go home");
                sb.AppendLine("  navigate(\"cart\")  → open cart modal");
            }
        }
        catch
        {
            // Route discovery may fail before Shell is fully initialized
        }

        return sb.ToString();
    }
}
