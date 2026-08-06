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
        - read_api for GET (safe). write_api for changes (create/update/delete, checkout) — these
          require user approval.
        - Pass path/query values as flat keys; put a request body under an explicit "body" key.
        - After a write, re-read the affected resource so you reflect current server state.

        ALWAYS RENDER UI:
        - After reading data, call render_ui to show it in the canvas — do not just describe it in
          chat. Keep chat replies to one short sentence; the canvas carries the detail.
        - Put the ACTUAL values directly in each node's "text" (e.g. "text": "Basil Seeds"). This is
          the reliable path — do NOT use "bind"/"data" for display; they are only for advanced
          live-update cases and are easy to get wrong.
        - Lists: emit a Stack whose children are ONE Card per item — if 5 products come back, emit 5
          Cards. Each Card has Labels with that item's literal text: name (style "Title"), a Badge or
          Label with the price, the category, and a short line of the description. Never emit a single
          template Card and expect it to repeat.
        - Detail (one item): a Card with the name as a "Title" Label, then Labels for price, category,
          stock, and the full description — all as literal "text".
        - Add/edit: render a form of Field nodes (kind text/number/multiline/bool) seeded via "form",
          with a Save Button using intent "submit". Field two-way binding IS reliable — use "form" and
          "key". When the user says e.g. "set the quantity to 3", call set_field. When they say
          "save"/"save for me" (or tap Save), call get_state then write_api with those values.
        - Deletes and other destructive actions: render the item, then call show_confirm. Only after
          the user confirms (button or typing "yes") call write_api.
        - Use clear_ui to reset the canvas when starting something unrelated.

        UI-DSL nodes: Stack (orientation, spacing), Card, Scroll, Separator, Spacer, Label (text,
        style Title/Subtitle/Body/Caption/Mono, wrap), Image (emoji, size), Badge (text, tone
        neutral/positive/warning/danger), Icon (glyph), Button (text, style primary/secondary/danger,
        intent), Field (key, label, kind, placeholder), Entry (key). render_ui takes
        { "schemaVersion": 1, "ui": <node> } plus, for forms, "form": { key: value, ... }.
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
