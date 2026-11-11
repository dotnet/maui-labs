namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>A compact tool call card: name and status, with expandable arguments and result.</summary>
internal sealed class ToolCallView : ChatBlockView
{
    private readonly Label _title = ChatVisuals.Label(AIChatThemeKeys.CardTitleStyle);
    private readonly Label _status = ChatVisuals.Label(AIChatThemeKeys.SecondaryTextStyle);
    private readonly Button _toggle;
    private readonly VerticalStackLayout _details = new ChatVerticalStackLayout { Spacing = 6, IsVisible = false };
    private readonly Label _argumentsHeader = ChatVisuals.Label(AIChatThemeKeys.SecondaryTextStyle, "Arguments");
    private readonly Label _arguments = ChatVisuals.Label(AIChatThemeKeys.CodeTextStyle);
    private readonly Border _argumentsBlock;
    private readonly Label _resultHeader = ChatVisuals.Label(AIChatThemeKeys.SecondaryTextStyle, "Result");
    private readonly Label _result = ChatVisuals.Label(AIChatThemeKeys.CodeTextStyle);
    private readonly Border _resultBlock;

    public ToolCallView()
    {
        AutomationId = "AIChatToolCall";

        _toggle = ChatVisuals.DisclosureButton(Toggle, "AIChatToolCallDetails");

        _status.VerticalOptions = LayoutOptions.Center;
        _title.VerticalOptions = LayoutOptions.Center;

        var header = new ChatGrid
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

        Content = ChatVisuals.Card(new ChatVerticalStackLayout { Spacing = 8, Children = { header, _details } });
    }

    internal Label TitleLabel => _title;

    internal Label StatusLabel => _status;

    internal VerticalStackLayout Details => _details;

    internal void Toggle()
    {
        ViewState.IsExpanded = !ViewState.IsExpanded;
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
        _status.SetDynamicResource(
            Label.TextColorProperty,
            block?.Result?.Exception is not null ? AIChatThemeKeys.ErrorColor : AIChatThemeKeys.SecondaryTextColor);

        var arguments = ChatVisuals.FormatArguments(block?.Arguments);
        _arguments.Text = arguments;
        _argumentsHeader.IsVisible = _argumentsBlock.IsVisible = arguments.Length > 0;

        var result = block?.Result;
        _result.Text = result is null ? string.Empty : ChatVisuals.FormatResult(result);
        _resultHeader.IsVisible = _resultBlock.IsVisible = result is not null;

        var expanded = ViewState.IsExpanded;
        _details.IsVisible = expanded;
        ChatVisuals.SetExpanded(_toggle, expanded, name);
        SemanticProperties.SetDescription(_title, _status.Text.Length > 0 ? $"Tool {name}, {_status.Text}" : $"Tool {name}");
    }
}

/// <summary>
/// Asks the user to approve or reject a tool call, then shows the decision and, once the call ran, its result —
/// one card per call. Once decided, the arguments and result fold away like a tool call's details.
/// </summary>
internal sealed class ToolApprovalView : ChatBlockView
{
    private readonly Label _title = ChatVisuals.Label(AIChatThemeKeys.CardTitleStyle);
    private readonly Label _status = ChatVisuals.Label(AIChatThemeKeys.SecondaryTextStyle);
    private readonly Button _toggle;
    private readonly VerticalStackLayout _details = new ChatVerticalStackLayout { Spacing = 6 };
    private readonly ContentView _call = new ChatContentView();
    private readonly Label _argumentsHeader = ChatVisuals.Label(AIChatThemeKeys.SecondaryTextStyle, "Arguments");
    private readonly Label _arguments = ChatVisuals.Label(AIChatThemeKeys.CodeTextStyle);
    private readonly Border _argumentsBlock;
    private readonly Label _resultHeader = ChatVisuals.Label(AIChatThemeKeys.SecondaryTextStyle, "Result");
    private readonly Label _result = ChatVisuals.Label(AIChatThemeKeys.CodeTextStyle);
    private readonly Border _resultBlock;
    private readonly Button _approve;
    private readonly Button _reject;
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
        _toggle = ChatVisuals.DisclosureButton(Toggle, "AIChatToolApprovalDetails");

        ApproveCommand = new Command(Approve, () => CanDecide);
        RejectCommand = new Command(Reject, () => CanDecide);

        _approve = new ChatButton { Text = "Approve", AutomationId = "ApproveToolButton", Command = ApproveCommand }
            .Styled(AIChatThemeKeys.PrimaryButtonStyle);
        _reject = new ChatButton { Text = "Reject", AutomationId = "RejectToolButton", Command = RejectCommand }
            .Styled(AIChatThemeKeys.SecondaryButtonStyle);
        _buttons = new ChatHorizontalStackLayout { Spacing = 8, Children = { _approve, _reject } };

        var header = new ChatGrid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 8,
            MinimumHeightRequest = 28,
        };
        header.Add(_title, 0);
        header.Add(_status, 1);
        header.Add(_toggle, 2);

        _details.Children.Add(_argumentsHeader);
        _details.Children.Add(_call);
        _details.Children.Add(_resultHeader);
        _details.Children.Add(_resultBlock);

        Content = ChatVisuals.Card(new ChatVerticalStackLayout { Spacing = 8, Children = { header, _details, _buttons } });
    }

    internal Command ApproveCommand { get; }

    internal Command RejectCommand { get; }

    internal Label TitleLabel => _title;

    internal Label StatusLabel => _status;

    internal Label ResultLabel => _result;

    internal HorizontalStackLayout Buttons => _buttons;

    internal View? InnerView => _innerView;

    internal Button DetailsToggle => _toggle;

    internal VerticalStackLayout Details => _details;

    /// <summary>Gets whether the user can still decide: the request is pending and its turn is still waiting.</summary>
    private bool CanDecide => Block is ToolApprovalBlock { IsAwaitingInput: true };

    internal void Toggle()
    {
        ViewState.IsExpanded = !ViewState.IsExpanded;
        OnBlockChanged();
    }

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

        // An app template for the call shows its result itself and stays open.
        var result = block?.Result;
        var hasResult = result is not null && _innerView is null;
        _result.Text = hasResult ? ChatVisuals.FormatResult(result!) : null;
        _resultHeader.IsVisible = _resultBlock.IsVisible = hasResult;
        _call.IsVisible = _call.Content is not null;

        // Pending, the arguments are the question; once decided they are details, labeled like a tool call's.
        var foldable = !canDecide && _innerView is null && (_call.Content is not null || hasResult);
        _argumentsHeader.IsVisible = foldable && _call.Content is not null;
        var open = !foldable || ViewState.IsExpanded;
        _details.IsVisible = open && (_call.Content is not null || hasResult);
        _toggle.IsVisible = foldable;
        ChatVisuals.SetExpanded(_toggle, open, name);

        _buttons.IsVisible = canDecide;
        SemanticProperties.SetDescription(_approve, $"Approve {name}");
        SemanticProperties.SetDescription(_reject, $"Reject {name}");
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
                _innerView = BuiltInBlockTemplates.CreateView(template, inner, this);
            }

            Bind(_innerView, inner, Owner, Session, Turn);
            _innerView.IsEnabled = canDecide;
            _call.Content = _innerView;
            return;
        }

        _innerTemplate = null;
        _innerView = null;
        var arguments = ChatVisuals.FormatArguments(block?.Arguments);
        _arguments.Text = arguments;
        _call.Content = arguments.Length > 0 ? _argumentsBlock : null;
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
internal sealed class UIActionView : ChatBlockView
{
    private readonly Label _title = ChatVisuals.Label(AIChatThemeKeys.CardTitleStyle);
    private readonly Label _status = ChatVisuals.Label(AIChatThemeKeys.SecondaryTextStyle);
    private readonly Button _run;

    public UIActionView()
    {
        AutomationId = "AIChatUIAction";
        RunCommand = new Command(async () => await RunAsync(), () => CanRun);
        _run = new ChatButton
        {
            Text = "Run",
            AutomationId = "AIChatUIActionRun",
            HorizontalOptions = LayoutOptions.Start,
            Command = RunCommand,
        }.Styled(AIChatThemeKeys.PrimaryButtonStyle);

        _title.VerticalOptions = LayoutOptions.Center;
        _status.VerticalOptions = LayoutOptions.Center;
        var header = new ChatGrid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 8,
            MinimumHeightRequest = 28,
        };
        header.Add(_title, 0);
        header.Add(_status, 1);

        Content = ChatVisuals.Card(new ChatVerticalStackLayout { Spacing = 8, Children = { header, _run } });
    }

    internal Command RunCommand { get; }

    internal Label StatusLabel => _status;

    internal Button RunButton => _run;

    private bool CanRun => Block is UIActionBlock { IsAwaitingInput: true } && !ViewState.IsRunning;

    protected override void OnBlockChanged()
    {
        var action = Block as UIActionBlock;
        var name = action?.ToolName ?? "Action";
        _title.Text = name;
        _status.Text = action switch
        {
            { IsComplete: true } => "Done",
            _ when ViewState.IsRunning => "Running\u2026",
            _ when !IsTurnActive => Turn?.IsStopped == true ? "Stopped" : "Not run",
            { IsAwaitingInput: true } => "Waiting for you to run it",
            _ => "Running\u2026",
        };
        _run.IsVisible = CanRun;
        SemanticProperties.SetDescription(_run, $"Run {name}");
        SemanticProperties.SetDescription(_title, $"Action {name}, {_status.Text}");
        RunCommand.ChangeCanExecute();
    }

    internal async Task RunAsync()
    {
        if (!CanRun || Block is not UIActionBlock action)
            return;

        // The view may show another block by the time the action finishes.
        var state = ViewState;
        state.IsRunning = true;
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
            state.IsRunning = false;
            OnBlockChanged();
        }
    }
}

/// <summary>The model's reasoning, collapsed by default.</summary>
internal sealed class ReasoningView : ChatBlockView
{
    private readonly Button _header;
    private readonly Label _chevron = ChatVisuals.Icon(AIChatThemeKeys.ExpandIcon, fontSize: 16);
    private readonly Label _text = ChatVisuals.Label(AIChatThemeKeys.SecondaryTextStyle);

    public ReasoningView()
    {
        AutomationId = "AIChatReasoning";
        _header = new ChatButton
        {
            HorizontalOptions = LayoutOptions.Start,
            Command = new Command(Toggle),
            AutomationId = "AIChatReasoningToggle",
        }.Styled(AIChatThemeKeys.ReasoningHeaderStyle);
        _text.IsVisible = false;

        // The chevron is decoration next to the header button, which carries the accessible name.
        _chevron.GestureRecognizers.Add(new TapGestureRecognizer { Command = _header.Command });

        Content = new ChatVerticalStackLayout
        {
            Spacing = 4,
            Children = { new ChatHorizontalStackLayout { Spacing = 6, Children = { _header, _chevron } }, _text },
        };
    }

    internal Button Header => _header;

    internal Label Chevron => _chevron;

    internal Label TextLabel => _text;

    internal void Toggle()
    {
        if (string.IsNullOrWhiteSpace((Block as ReasoningContentBlock)?.Text))
            return;

        ViewState.IsExpanded = !ViewState.IsExpanded;
        OnBlockChanged();
    }

    protected override void OnBlockChanged()
    {
        // Reasoning without text (only provider-protected data, such as encrypted reasoning) has nothing to show.
        var reasoning = Block as ReasoningContentBlock;
        if (reasoning is null || string.IsNullOrWhiteSpace(reasoning.Text))
        {
            _header.IsVisible = false;
            _chevron.IsVisible = false;
            _text.IsVisible = false;
            _text.Text = null;
            return;
        }

        var expanded = ViewState.IsExpanded;
        _header.IsVisible = true;
        _header.Text = "Reasoning";
        _chevron.IsVisible = true;
        _chevron.SetDynamicResource(Label.TextProperty, expanded ? AIChatThemeKeys.CollapseIcon : AIChatThemeKeys.ExpandIcon);
        SemanticProperties.SetDescription(_header, expanded ? "Hide reasoning" : "Show reasoning");
        _text.Text = reasoning.Text;
        _text.IsVisible = expanded && reasoning.Text.Length > 0;
    }
}

/// <summary>
/// One media item the assistant returned: an image inline, a link as a link chip, anything else as a file chip.
/// A streaming image (a partial image replaced by the next) updates in place.
/// </summary>
internal sealed class MediaBlockView : ChatBlockView
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
