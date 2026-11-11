namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Assistant text. Full width, no bubble.</summary>
/// <remarks>Plain text for now; the Markdown renderer replaces this view in <see cref="BuiltInBlockTemplates"/>.</remarks>
internal sealed class TextBlockView : BlockView
{
    private readonly Label _label = ChatVisuals.Label(AIChatThemeKeys.AssistantTextStyle);

    public TextBlockView()
    {
        AutomationId = "AIChatAssistantText";
        Content = _label;
    }

    internal Label Label => _label;

    protected override void OnBlockChanged() => _label.Text = (Block as RichContentBlock)?.RawText;
}

/// <summary>A compact tool call card: name and status, with expandable arguments and result.</summary>
internal sealed class ToolCallView : BlockView
{
    private readonly Label _title = ChatVisuals.Label(AIChatThemeKeys.CardTitleStyle);
    private readonly Label _status = ChatVisuals.Label(AIChatThemeKeys.SecondaryTextStyle);
    private readonly Button _toggle;
    private readonly VerticalStackLayout _details = new() { Spacing = 6, IsVisible = false };
    private readonly Label _argumentsHeader = ChatVisuals.Label(AIChatThemeKeys.SecondaryTextStyle, "Arguments");
    private readonly Label _arguments = ChatVisuals.Label(AIChatThemeKeys.CodeTextStyle);
    private readonly Border _argumentsBlock;
    private readonly Label _resultHeader = ChatVisuals.Label(AIChatThemeKeys.SecondaryTextStyle, "Result");
    private readonly Label _result = ChatVisuals.Label(AIChatThemeKeys.CodeTextStyle);
    private readonly Border _resultBlock;
    private bool _expanded;

    public ToolCallView()
    {
        AutomationId = "AIChatToolCall";

        _toggle = new Button
        {
            Text = "\u25B8",
            FontSize = 14,
            WidthRequest = 28,
            HeightRequest = 28,
            VerticalOptions = LayoutOptions.Center,
            Command = new Command(Toggle),
            AutomationId = "AIChatToolCallDetails",
        }.Styled(AIChatThemeKeys.IconButtonStyle);

        _status.VerticalOptions = LayoutOptions.Center;
        _title.VerticalOptions = LayoutOptions.Center;

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 8,
        };
        header.Add(_title, 0);
        header.Add(_status, 1);
        header.Add(_toggle, 2);

        _argumentsBlock = ChatVisuals.CodeBlock(_arguments);
        _resultBlock = ChatVisuals.CodeBlock(_result);
        _details.Children.Add(_argumentsHeader);
        _details.Children.Add(_argumentsBlock);
        _details.Children.Add(_resultHeader);
        _details.Children.Add(_resultBlock);

        Content = ChatVisuals.Card(new VerticalStackLayout { Spacing = 6, Children = { header, _details } });
    }

    internal Label TitleLabel => _title;

    internal Label StatusLabel => _status;

    internal VerticalStackLayout Details => _details;

    internal void Toggle()
    {
        _expanded = !_expanded;
        Update();
    }

    protected override void OnBlockChanged() => Update();

    private void Update()
    {
        var block = Block as FunctionInvocationContentBlock;
        var name = block?.ToolName ?? "Tool";
        _title.Text = name;
        _status.Text = GetStatus(block);

        var arguments = ChatVisuals.FormatArguments(block?.Arguments);
        _arguments.Text = arguments;
        _argumentsHeader.IsVisible = _argumentsBlock.IsVisible = arguments.Length > 0;

        var result = block?.Result;
        _result.Text = result is null
            ? string.Empty
            : result.Exception?.Message ?? ChatVisuals.FormatValue(result.Result);
        _resultHeader.IsVisible = _resultBlock.IsVisible = result is not null;

        _details.IsVisible = _expanded;
        _toggle.Text = _expanded ? "\u25BE" : "\u25B8";
        var action = _expanded ? "Hide" : "Show";
        SemanticProperties.SetDescription(_toggle, $"{action} details for {name}");
        SemanticProperties.SetDescription(_title, $"Tool {name}, {_status.Text}");
    }

    private static string GetStatus(FunctionInvocationContentBlock? block) =>
        block?.Result switch
        {
            null => "Running…",
            { Exception: not null } => "Failed",
            _ => "Completed",
        };
}

/// <summary>Asks the user to approve or reject a tool call.</summary>
internal sealed class ToolApprovalView : BlockView
{
    private readonly Label _title = ChatVisuals.Label(AIChatThemeKeys.CardTitleStyle);
    private readonly ContentView _details = new();
    private readonly Label _arguments = ChatVisuals.Label(AIChatThemeKeys.CodeTextStyle);
    private readonly Border _argumentsBlock;
    private readonly HorizontalStackLayout _buttons;
    private readonly Label _resolution = ChatVisuals.Label(AIChatThemeKeys.SecondaryTextStyle);
    private BlockTemplate? _innerTemplate;
    private View? _innerView;

    public ToolApprovalView()
    {
        AutomationId = "AIChatToolApproval";
        _title.LineBreakMode = LineBreakMode.WordWrap;
        _argumentsBlock = ChatVisuals.CodeBlock(_arguments);

        ApproveCommand = new Command(Approve, () => Pending is not null);
        RejectCommand = new Command(Reject, () => Pending is not null);

        var approve = new Button { Text = "Approve", AutomationId = "ApproveToolButton", Command = ApproveCommand }
            .Styled(AIChatThemeKeys.PrimaryButtonStyle);
        var reject = new Button { Text = "Reject", AutomationId = "RejectToolButton", Command = RejectCommand }
            .Styled(AIChatThemeKeys.SecondaryButtonStyle);
        _buttons = new HorizontalStackLayout { Spacing = 8, Children = { approve, reject } };

        Content = ChatVisuals.Card(new VerticalStackLayout
        {
            Spacing = 8,
            Children = { _title, _details, _buttons, _resolution },
        });
    }

    internal Command ApproveCommand { get; }

    internal Command RejectCommand { get; }

    internal Label TitleLabel => _title;

    internal Label ResolutionLabel => _resolution;

    internal HorizontalStackLayout Buttons => _buttons;

    internal View? InnerView => _innerView;

    private ToolApprovalBlock? Pending => Block is ToolApprovalBlock { Status: ApprovalStatus.Pending } block ? block : null;

    protected override void OnBlockChanged()
    {
        var block = Block as ToolApprovalBlock;
        var name = block?.ToolName ?? "this tool";
        var status = block?.Status ?? ApprovalStatus.Pending;

        _title.Text = status == ApprovalStatus.Pending ? $"Allow {name} to run?" : name;
        UpdateDetails(block);

        _buttons.IsVisible = status == ApprovalStatus.Pending;
        _resolution.IsVisible = status != ApprovalStatus.Pending;
        _resolution.Text = status switch
        {
            ApprovalStatus.Approved => "Approved",
            ApprovalStatus.Rejected => "Rejected",
            _ => null,
        };
        _resolution.SetDynamicResource(
            Label.TextColorProperty,
            status == ApprovalStatus.Approved ? AIChatThemeKeys.SuccessColor : AIChatThemeKeys.ErrorColor);

        ApproveCommand.ChangeCanExecute();
        RejectCommand.ChangeCanExecute();
    }

    private void UpdateDetails(ToolApprovalBlock? block)
    {
        // An app template for the tool call (for example a [ToolBlock] card) renders inside the approval.
        var inner = block?.InnerBlock;
        var template = inner is null ? null : Owner?.SelectAppTemplate(inner);
        if (template is not null && inner is not null)
        {
            if (!ReferenceEquals(template, _innerTemplate) || _innerView is null)
            {
                _innerTemplate = template;
                _innerView = template.CreateView(inner, this, Owner?.Services);
            }

            Bind(_innerView, inner, Owner, Session);
            _innerView.IsEnabled = block?.Status == ApprovalStatus.Pending;
            _details.Content = _innerView;
            _details.IsVisible = true;
            return;
        }

        _innerTemplate = null;
        _innerView = null;
        var arguments = ChatVisuals.FormatArguments(block?.Arguments);
        _arguments.Text = arguments;
        _details.Content = arguments.Length > 0 ? _argumentsBlock : null;
        _details.IsVisible = arguments.Length > 0;
    }

    private void Approve()
    {
        if (Pending is not { } block)
            return;

        block.Approve();
        OnBlockChanged();
    }

    private void Reject()
    {
        if (Pending is not { } block)
            return;

        block.Reject();
        OnBlockChanged();
    }
}

/// <summary>A client-side UI action: status, and a Run button for manual actions.</summary>
internal sealed class UIActionView : BlockView
{
    private readonly Label _title = ChatVisuals.Label(AIChatThemeKeys.CardTitleStyle);
    private readonly Label _status = ChatVisuals.Label(AIChatThemeKeys.SecondaryTextStyle);
    private readonly Button _run;
    private bool _isRunning;

    public UIActionView()
    {
        AutomationId = "AIChatUIAction";
        RunCommand = new Command(async () => await RunAsync(), () => CanRun);
        _run = new Button
        {
            Text = "Run",
            AutomationId = "AIChatUIActionRun",
            HorizontalOptions = LayoutOptions.Start,
            Command = RunCommand,
        }.Styled(AIChatThemeKeys.PrimaryButtonStyle);

        Content = ChatVisuals.Card(new VerticalStackLayout { Spacing = 6, Children = { _title, _status, _run } });
    }

    internal Command RunCommand { get; }

    internal Label StatusLabel => _status;

    internal Button RunButton => _run;

    private bool CanRun => Block is UIActionBlock { Mode: UIActionInvocationMode.Manual, IsComplete: false } && !_isRunning;

    protected override void OnBlockChanged()
    {
        var action = Block as UIActionBlock;
        var name = action?.ToolName ?? "Action";
        _title.Text = name;
        _status.Text = action switch
        {
            { IsComplete: true } => "Done",
            { Mode: UIActionInvocationMode.Manual } when !_isRunning => "Waiting for you to run it",
            _ => "Running…",
        };
        _run.IsVisible = action is { Mode: UIActionInvocationMode.Manual, IsComplete: false };
        SemanticProperties.SetDescription(_title, $"Action {name}, {_status.Text}");
        RunCommand.ChangeCanExecute();
    }

    internal async Task RunAsync()
    {
        if (!CanRun || Block is not UIActionBlock action)
            return;

        _isRunning = true;
        OnBlockChanged();
        try
        {
            await action.InvokeAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException)
        {
            // The action already ran or the turn was stopped; the block reports the outcome.
        }
        finally
        {
            _isRunning = false;
            OnBlockChanged();
        }
    }
}

/// <summary>The model's reasoning, collapsed by default.</summary>
internal sealed class ReasoningView : BlockView
{
    private readonly Button _header;
    private readonly Label _text = ChatVisuals.Label(AIChatThemeKeys.SecondaryTextStyle);
    private bool _expanded;

    public ReasoningView()
    {
        AutomationId = "AIChatReasoning";
        _header = new Button
        {
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            Padding = new Thickness(0),
            HorizontalOptions = LayoutOptions.Start,
            FontSize = 13,
            Command = new Command(Toggle),
            AutomationId = "AIChatReasoningToggle",
        };
        _header.SetDynamicResource(Button.TextColorProperty, AIChatThemeKeys.SecondaryTextColor);
        _text.IsVisible = false;

        Content = new VerticalStackLayout { Spacing = 4, Children = { _header, _text } };
    }

    internal Button Header => _header;

    internal Label TextLabel => _text;

    internal void Toggle()
    {
        if (Block is not ReasoningContentBlock { IsEncrypted: false } reasoning || reasoning.Text.Length == 0)
            return;

        _expanded = !_expanded;
        OnBlockChanged();
    }

    protected override void OnBlockChanged()
    {
        var reasoning = Block as ReasoningContentBlock;
        if (reasoning is null || reasoning.IsEncrypted)
        {
            _header.Text = "Reasoning is hidden";
            _header.IsEnabled = false;
            _text.IsVisible = false;
            _text.Text = null;
            return;
        }

        _header.IsEnabled = true;
        _header.Text = _expanded ? "Reasoning \u25BE" : "Reasoning \u25B8";
        SemanticProperties.SetDescription(_header, _expanded ? "Hide reasoning" : "Show reasoning");
        _text.Text = reasoning.Text;
        _text.IsVisible = _expanded && reasoning.Text.Length > 0;
    }
}

/// <summary>Media the assistant returned: images inline, anything else as a file chip.</summary>
internal sealed class MediaBlockView : BlockView
{
    private readonly VerticalStackLayout _items = new() { Spacing = 8 };

    public MediaBlockView()
    {
        AutomationId = "AIChatMedia";
        Content = _items;
    }

    internal VerticalStackLayout Items => _items;

    protected override void OnBlockChanged()
    {
        _items.Children.Clear();
        if (Block is not MediaContentBlock media)
            return;

        foreach (var item in media.Items)
            _items.Children.Add(ChatVisuals.CreateMediaView(item));
    }
}
