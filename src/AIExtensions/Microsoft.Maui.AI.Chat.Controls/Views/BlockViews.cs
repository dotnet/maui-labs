namespace Microsoft.Maui.AI.Chat.Controls;

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
        _status.Text = ChatVisuals.DescribeCall(block, IsTurnActive, Turn?.IsStopped == true);
        _status.IsVisible = _status.Text.Length > 0;

        var arguments = ChatVisuals.FormatArguments(block?.Arguments);
        _arguments.Text = arguments;
        _argumentsHeader.IsVisible = _argumentsBlock.IsVisible = arguments.Length > 0;

        var result = block?.Result;
        _result.Text = result is null ? string.Empty : ChatVisuals.FormatResult(result);
        _resultHeader.IsVisible = _resultBlock.IsVisible = result is not null;

        _details.IsVisible = _expanded;
        _toggle.Text = _expanded ? "\u25BE" : "\u25B8";
        var action = _expanded ? "Hide" : "Show";
        SemanticProperties.SetDescription(_toggle, $"{action} details for {name}");
        SemanticProperties.SetDescription(_title, _status.Text.Length > 0 ? $"Tool {name}, {_status.Text}" : $"Tool {name}");
    }
}

/// <summary>
/// Asks the user to approve or reject a tool call, then shows the decision and, once the call ran, its result —
/// one card per call.
/// </summary>
internal sealed class ToolApprovalView : BlockView
{
    private readonly Label _title = ChatVisuals.Label(AIChatThemeKeys.CardTitleStyle);
    private readonly Label _status = ChatVisuals.Label(AIChatThemeKeys.SecondaryTextStyle);
    private readonly ContentView _details = new();
    private readonly Label _arguments = ChatVisuals.Label(AIChatThemeKeys.CodeTextStyle);
    private readonly Border _argumentsBlock;
    private readonly Label _resultHeader = ChatVisuals.Label(AIChatThemeKeys.SecondaryTextStyle, "Result");
    private readonly Label _result = ChatVisuals.Label(AIChatThemeKeys.CodeTextStyle);
    private readonly Border _resultBlock;
    private readonly HorizontalStackLayout _buttons;
    private BlockTemplate? _innerTemplate;
    private View? _innerView;

    public ToolApprovalView()
    {
        AutomationId = "AIChatToolApproval";
        _title.LineBreakMode = LineBreakMode.WordWrap;
        _title.VerticalOptions = LayoutOptions.Center;
        _status.VerticalOptions = LayoutOptions.Center;
        _argumentsBlock = ChatVisuals.CodeBlock(_arguments);
        _resultBlock = ChatVisuals.CodeBlock(_result);

        ApproveCommand = new Command(Approve, () => CanDecide);
        RejectCommand = new Command(Reject, () => CanDecide);

        var approve = new Button { Text = "Approve", AutomationId = "ApproveToolButton", Command = ApproveCommand }
            .Styled(AIChatThemeKeys.PrimaryButtonStyle);
        var reject = new Button { Text = "Reject", AutomationId = "RejectToolButton", Command = RejectCommand }
            .Styled(AIChatThemeKeys.SecondaryButtonStyle);
        _buttons = new HorizontalStackLayout { Spacing = 8, Children = { approve, reject } };

        var header = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 8,
        };
        header.Add(_title, 0);
        header.Add(_status, 1);

        Content = ChatVisuals.Card(new VerticalStackLayout
        {
            Spacing = 8,
            Children = { header, _details, _resultHeader, _resultBlock, _buttons },
        });
    }

    internal Command ApproveCommand { get; }

    internal Command RejectCommand { get; }

    internal Label TitleLabel => _title;

    internal Label StatusLabel => _status;

    internal Label ResultLabel => _result;

    internal HorizontalStackLayout Buttons => _buttons;

    internal View? InnerView => _innerView;

    /// <summary>Gets whether the user can still decide: the request is pending and its turn is still waiting.</summary>
    private bool CanDecide => Block is ToolApprovalBlock { Status: ApprovalStatus.Pending } && IsTurnActive;

    protected override void OnBlockChanged()
    {
        var block = Block as ToolApprovalBlock;
        var name = block?.ToolName ?? "this tool";
        var canDecide = CanDecide;

        _title.Text = canDecide ? $"Allow {name} to run?" : name;
        UpdateDetails(block, canDecide);

        var (status, colorKey) = Describe(block);
        _status.Text = status;
        _status.IsVisible = status.Length > 0;
        _status.SetDynamicResource(Label.TextColorProperty, colorKey);

        // An app template for the call shows its result itself.
        var result = block?.Result;
        var showResult = result is not null && _innerView is null;
        _result.Text = showResult ? ChatVisuals.FormatResult(result!) : null;
        _resultHeader.IsVisible = _resultBlock.IsVisible = showResult;

        _buttons.IsVisible = canDecide;
        SemanticProperties.SetDescription(_title, status.Length > 0 ? $"{name}, {status}" : _title.Text);
        ApproveCommand.ChangeCanExecute();
        RejectCommand.ChangeCanExecute();
    }

    private (string Text, string ColorKey) Describe(ToolApprovalBlock? block)
    {
        var stopped = Turn?.IsStopped == true;
        return block?.Status switch
        {
            ApprovalStatus.Rejected => ("Rejected", AIChatThemeKeys.ErrorColor),
            ApprovalStatus.Approved => block.Result switch
            {
                { Exception: not null } => ("Approved \u00B7 Failed", AIChatThemeKeys.ErrorColor),
                not null => ("Approved", AIChatThemeKeys.SuccessColor),
                _ when IsTurnActive => ("Approved \u00B7 Running\u2026", AIChatThemeKeys.SecondaryTextColor),
                _ when stopped => ("Approved \u00B7 Stopped", AIChatThemeKeys.SecondaryTextColor),
                _ => ("Approved \u00B7 Not completed", AIChatThemeKeys.SecondaryTextColor),
            },
            _ when CanDecide || block is null => (string.Empty, AIChatThemeKeys.SecondaryTextColor),
            _ => (stopped ? "Stopped" : "Not answered", AIChatThemeKeys.SecondaryTextColor),
        };
    }

    private void UpdateDetails(ToolApprovalBlock? block, bool canDecide)
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

            Bind(_innerView, inner, Owner, Session, Turn);
            _innerView.IsEnabled = canDecide;
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
        if (!CanDecide || Block is not ToolApprovalBlock block)
            return;

        block.Approve();
        OnBlockChanged();
    }

    private void Reject()
    {
        if (!CanDecide || Block is not ToolApprovalBlock block)
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

    private bool CanRun =>
        Block is UIActionBlock { Mode: UIActionInvocationMode.Manual, IsComplete: false } && !_isRunning && IsTurnActive;

    protected override void OnBlockChanged()
    {
        var action = Block as UIActionBlock;
        var name = action?.ToolName ?? "Action";
        _title.Text = name;
        _status.Text = action switch
        {
            { IsComplete: true } => "Done",
            _ when _isRunning => "Running\u2026",
            _ when !IsTurnActive => Turn?.IsStopped == true ? "Stopped" : "Not run",
            { Mode: UIActionInvocationMode.Manual } => "Waiting for you to run it",
            _ => "Running\u2026",
        };
        _run.IsVisible = CanRun;
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

/// <summary>
/// One media item the assistant returned: an image inline, a link as a link chip, anything else as a file chip.
/// A streaming image (a partial image replaced by the next) updates in place.
/// </summary>
internal sealed class MediaBlockView : BlockView
{
    private AIContent? _shown;

    public MediaBlockView() => AutomationId = "AIChatMedia";

    internal View? MediaView => Content;

    protected override void OnBlockChanged()
    {
        var content = (Block as MediaContentBlock)?.Content;
        if (ReferenceEquals(content, _shown))
            return;

        _shown = content;
        Content = content is null ? null : ChatVisuals.UpdateMediaView(Content, content);
    }
}
