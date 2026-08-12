using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Attributes;
using Microsoft.Maui.AI.GenerativeUI;
using Microsoft.Maui.AI.GenerativeUI.OpenApi;
using Microsoft.Maui.AI.GenerativeUI.Tools;
using CanvasState = Microsoft.Maui.AI.GenerativeUI.Canvas.CanvasState;

namespace GenerativeUI.Sample.Garden.ViewModels;

/// <summary>
/// The chat loop. Registers the generic server-API tools (discover + call the Garden REST API) and
/// the client-UI tools (render bespoke, data-bound views into the canvas). Interactive controls raise
/// intents back through <see cref="IChatBridge"/>, which this view model turns into synthetic user
/// turns so the loop stays AI-driven.
/// </summary>
public sealed partial class ChatViewModel : ObservableObject, IChatBridge
{
    /// <summary>
    /// Source-generated tool context. The generator scans the referenced tool types for
    /// <c>[ExportAIFunction]</c> methods and exposes them via <c>GardenApiTools.Default.Tools</c>. The
    /// instances are resolved from DI at invocation time.
    /// </summary>
    [AIToolSource(typeof(OpenApiExplorerTools))]
    [AIToolSource(typeof(GenerativeUiTools))]
    private partial class GardenApiTools : AIToolContext { }

    private const string SystemPrompt =
        """
        You are a helpful assistant for an online garden shop. The app has two columns: a large canvas
        you control by rendering UI, and this chat. You have generic tools to (1) explore and call the
        shop's REST API and (2) render bespoke UI into the canvas. You do NOT know the endpoints ahead
        of time.

        SERVER API:
        - Discover first: list_endpoints, then describe_endpoint / describe_model for shapes.
        - read_api for GET (safe). write_api for changes (create/update/delete, checkout).
        - IMPORTANT: write_api's tool-calling infrastructure AUTOMATICALLY pauses and displays the
          app's Approve/Reject UI. Once the user's intent and all parameters are clear, call
          write_api immediately. Do NOT ask "are you sure?", wait for a typed "yes", render a
          confirmation button, or call show_confirm first—that would duplicate the built-in
          approval. Ask a conversational question only when a required parameter or target is
          genuinely ambiguous, never merely to obtain approval.
        - Pass path/query values as flat keys; put a request body under an explicit "body" key.
        - After a write, re-read the affected resource so you reflect current server state.

        UI COMPOSITION CONTRACT (requirements):
        - The canvas has ONE primary task/content focus at a time: catalog/products, one product
          detail, cart, orders, recommendations, or a form. Never show catalog + orders + product
          detail together.
        - A compact cart summary MAY accompany the primary content when it helps the current task,
          but at most two top-level areas are visible: one primary area + the compact cart. If the
          result would feel crowded, show only the primary area and let the user ask for the cart.
        - When the user switches from products to orders (or another primary task), replace the
          primary composition. Product details are a focused single-item composition; do not pretend
          a general popup/modal exists (only show_confirm is an actual overlay today).
        - Prefer a clear title, a small amount of supporting context, then the content. Do not render
          every field just because the API returned it; prioritize what answers the request.

        GLOBAL DESIGN LANGUAGE:
        - Calm, modern garden-store aesthetic: generous whitespace, rounded Cards, image-led content,
          concise typography, and a botanical green/gold palette.
        - For this prototype, emit ALL visual styling inline in the UI document — do not rely on
          named app styles. Inline palette: canvas #F4F8F3→#E7F1E9; cards #FFFFFF→#EEF7F0;
          primary text #173C34; secondary text #5D7268; stroke #C9DDD0; primary action #2F7D5B;
          secondary accent #C89B3C; danger #C94F55; glow #64A77B at ~0.20 opacity.
        - A gradient background is { "type":"linear", "colors":["#start","#end"], "angle":135 }.
          Use rounded corners 18–22, one-pixel soft strokes, and a subtle shadow/glow with radius
          16–22 and offsetY 6–8. Never use the default purple style in generated content.
        - Visual hierarchy: Title for view/product names; Subtitle or Badge for prominent prices and
          totals; Body for useful descriptions; Caption for category, SKU, stock, dates, and metadata.
        - Use only the spacing scale 4, 8, 12, 16 for spacing/padding. Use one primary action per
          composition, secondary for alternatives, and danger only for destructive actions.
        - Prefer fewer well-composed elements over dense dashboards. Do not duplicate the same value
          in multiple controls. Keep labels short and human-readable; never show raw JSON.
        - Use an Image hero when imageUrl exists; otherwise use the product emoji as a large visual
          accent. Do not render a broken/empty image.

        VIEW RECIPES (strong defaults):
        - PRODUCT LIST: Title + optional short intro; a bound itemsBind List of Cards. Each card feels
          like an image-led social product post. Use one Card template with background gradient,
          cornerRadius 20, soft green stroke/glow, and a Grid with columns "156,*,132": product Image
          in column 0 (set source to { "bind":"imageUrl" }, aspect fill, height 176, cornerRadius 18);
          a padded vertical
          Stack in column 1 with wrapping name/price/category/stock/description; and a vertical Stack
          in column 2 with equal-width View/Add buttons aligned end/center. Descriptions wrap and use
          maxLines 3. Every row action MUST identify its product: give each button a payload such as
          { "sku": { "bind":"sku" }, "name": { "bind":"name" } }. (The renderer also falls back to
          the whole bound row if payload is omitted.) Do not show full records or let action buttons
          drift beside the text. Inline button visuals explicitly and omit the style token: View has
          transparent background, #2F7D5B text/border and borderWidth 1; Add has #2F7D5B background,
          white text, no visible border; both cornerRadius 14, width 108, height 44.
        - PRODUCT DETAIL: one focused Card; hero image, name Title, price prominently, category +
          stock as Caption/Badge, full useful description, then actions. This is normally a static
          snapshot: inline the actual text and image URL returned by the API rather than binding
          fields (unless the detail is explicitly expected to live-update). Destructive actions are
          danger-styled; when tapped, call write_api directly and let its automatic approval UI
          confirm the action.
        - CART: compact bound itemsBind list; each line shows name, quantity, unit/subtotal as useful,
          and remove/change actions without marketing descriptions. Show the total prominently once.
        - ORDERS: Title + bound order Cards showing date, total, status/item count, with details hidden
          until requested. Do not show the catalog at the same time.
        - FORM: one Card/Stack of labelled Fields with sensible pre-filled values, clear grouping, and
          exactly one primary Save action; Cancel is secondary.
        - EMPTY/ERROR: one helpful focused Card; explain what happened and the next useful action.

        THE CANVAS IS STATEFUL (do not repaint it every turn):
        - The canvas binds to a persistent STATE GRAPH. render_ui describes structure ONCE; after
          that, data changes flow through bindings — you do NOT re-render for data changes.
        - Seed the state with render_ui's "data" (or set_state). Bind display text with "bind"
          (a dotted path into the state) so it updates live. For a single static snapshot literal
          "text" is fine, but anything that can change (cart, lists, totals, quantities) MUST bind.
        - For a list that can change, use a List node with "itemsBind" (a dotted path to a state
          collection) and exactly ONE template child; inside the template, bind row fields relative
          to the item (e.g. "bind": "name", "bind": "price"). Do not pre-expand changeable lists.
        - To change data, call apply_patch with JSON Patch (RFC 6902) — NOT render_ui. Examples:
          remove a cart line: [{"op":"remove","path":"/cart/items/2"}];
          set a quantity: [{"op":"replace","path":"/cart/items/0/quantity","value":3}];
          add a line: [{"op":"add","path":"/cart/items/-","value":{"sku":"pears","name":"Pears","price":2.99,"quantity":1}}].
          Always call get_state first so your paths match the real shape.
        - Call render_ui again ONLY when the KIND of view changes (products list -> cart -> a form).
          Use set_state to reseed, clear_ui to reset.

        FLOW:
        - After reading data from the server, seed it into state and render a bound view of it.
        - Add/edit forms: Field nodes (kind text/number/multiline/bool) two-way bound via "key" into
          the state, with a Save Button intent "submit". "set the quantity to 3" -> apply_patch or
          set_field. "save"/tap Save -> get_state then write_api.
        - Destructive actions: render the item with a danger-styled action Button. On an explicit
          user request/tap, call write_api directly; its automatic Approve/Reject UI is the sole
          confirmation. Never pair show_confirm or a typed "yes" with write_api. After approval and
          success, reflect the result by patching state.
        - Keep the server and the canvas state in sync: after a write_api, patch the state (or re-read
          and set_state) so the canvas matches the server.

        UI-DSL nodes: Stack (orientation, spacing), Grid (columns/rows; child column/row/span), Card,
        Scroll, Separator, Spacer, Label (text|bind,
        style Title/Subtitle/Body/Caption/Mono, wrap), Image (source|emoji, size), Badge (text|bind,
        tone neutral/positive/warning/danger), Icon (glyph), Button (text, style primary/secondary/
        danger, intent), Field (key, label, kind, placeholder), Entry (key), List (itemsBind + one
        template child, or pre-expanded static rows). render_ui takes
        { "schemaVersion": 1, "ui": <node>, "data"?: {...}, "form"?: {...} }.
        """;

    private readonly IChatClient _chatClient;
    private readonly CanvasState _canvas;
    private readonly List<ChatMessage> _history = [];
    private ToolApprovalRequestContent? _pendingApproval;

    public ChatViewModel(IServiceProvider rootProvider, IChatClient innerChatClient, CanvasState canvas)
    {
        _chatClient = new ChatClientBuilder(innerChatClient)
            .UseFunctionInvocation()
            .Build(rootProvider);
        _canvas = canvas;

        _history.Add(new ChatMessage(ChatRole.System, SystemPrompt));

        foreach (var tool in GardenApiTools.Default.Tools.OrderBy(t => t.Name))
            AvailableTools.Add(tool.Name);
    }

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ObservableCollection<string> AvailableTools { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
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

    /// <summary>
    /// Resets the conversation back to a fresh start: clears the transcript, drops any pending
    /// approval, and rebuilds history with just the system prompt. Disabled while a turn is in
    /// flight so we never clear history out from under a streaming response.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private void Clear()
    {
        _pendingApproval = null;
        IsApprovalPending = false;
        ApprovalText = "";
        InputText = string.Empty;

        Messages.Clear();
        _history.Clear();
        _history.Add(new ChatMessage(ChatRole.System, SystemPrompt));
        _canvas.Reset();
    }

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
        _canvas.IsBusy = true;
        try
        {
            var options = new ChatOptions
            {
                Tools = [.. GardenApiTools.Default.Tools],
                // render_ui documents (pre-expanded lists with full descriptions) can be large;
                // a low output cap truncates the JSON mid-document and the model loops on parse errors.
                MaxOutputTokens = 16000,
            };
            await StreamResponseAsync(options);
        }
        catch (Exception ex)
        {
            AddMessage(ChatMessageKind.Error, $"Error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            _canvas.IsBusy = false;
        }
    }

    /// <summary>
    /// <see cref="IChatBridge"/>: an interactive control raised an intent. Turn it into a synthetic
    /// user turn so the model decides what to do (collect the form and write, confirm, cancel, …).
    /// </summary>
    public async Task RaiseIntentAsync(UiIntent intent)
    {
        if (IsBusy || IsApprovalPending)
            return;

        var message = intent.Name switch
        {
            "submit" => "I tapped Save on the form. Call get_state to read the values, then complete the action with the right write_api call.",
            "confirm" => "Yes — I confirm. Proceed with the action.",
            "cancel" => "No — cancel that action.",
            _ when intent.Name.StartsWith("action:", StringComparison.Ordinal) =>
                $"I tapped the '{intent.Name["action:".Length..]}' button.{(string.IsNullOrEmpty(intent.Payload) ? "" : $" ({intent.Payload})")}",
            _ => $"UI intent: {intent.Name}.",
        };

        AddMessage(ChatMessageKind.User, message);
        _history.Add(new ChatMessage(ChatRole.User, message));
        await RunTurnAsync();
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
