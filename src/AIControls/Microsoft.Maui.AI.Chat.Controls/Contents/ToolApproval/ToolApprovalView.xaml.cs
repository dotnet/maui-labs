using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls.Themes;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Standalone C# ContentView for tool approval requests.
/// No XAML backing — styled via ControlTemplate.
/// Custom templates can include a root named <c>PART_Root</c>; if omitted,
/// the view falls back to applying visual states to itself.
/// </summary>
public class ToolApprovalView : ContentContextView
{
    private const string ActiveApproveAutomationId = "ApproveToolButton";
    private const string ActiveRejectAutomationId = "RejectToolButton";

    public static readonly BindableProperty ToolNameProperty =
        BindableProperty.Create(nameof(ToolName), typeof(string), typeof(ToolApprovalView));

    public string? ToolName
    {
        get => (string?)GetValue(ToolNameProperty);
        set => SetValue(ToolNameProperty, value);
    }

    public static readonly BindableProperty IsPendingProperty =
        BindableProperty.Create(nameof(IsPending), typeof(bool), typeof(ToolApprovalView), true);

    public bool IsPending
    {
        get => (bool)GetValue(IsPendingProperty);
        set => SetValue(IsPendingProperty, value);
    }

    public static readonly BindableProperty IsResolvedProperty =
        BindableProperty.Create(nameof(IsResolved), typeof(bool), typeof(ToolApprovalView));

    public bool IsResolved
    {
        get => (bool)GetValue(IsResolvedProperty);
        set => SetValue(IsResolvedProperty, value);
    }

    public static readonly BindableProperty ResolutionTextProperty =
        BindableProperty.Create(nameof(ResolutionText), typeof(string), typeof(ToolApprovalView));

    public string? ResolutionText
    {
        get => (string?)GetValue(ResolutionTextProperty);
        set => SetValue(ResolutionTextProperty, value);
    }

    public static readonly BindableProperty ApproveCommandProperty =
        BindableProperty.Create(nameof(ApproveCommand), typeof(ICommand), typeof(ToolApprovalView));

    public ICommand ApproveCommand
    {
        get => (ICommand)GetValue(ApproveCommandProperty);
        set => SetValue(ApproveCommandProperty, value);
    }

    public static readonly BindableProperty RejectCommandProperty =
        BindableProperty.Create(nameof(RejectCommand), typeof(ICommand), typeof(ToolApprovalView));

    public ICommand RejectCommand
    {
        get => (ICommand)GetValue(RejectCommandProperty);
        set => SetValue(RejectCommandProperty, value);
    }

    /// <summary>
    /// Optional inner content view type. When set, the wrapper resolves this view
    /// from DI (supporting constructor injection) and places it in the content slot.
    /// The resolved view can implement <see cref="IContentContextAware"/>
    /// to receive the <see cref="ContentContext"/> — like MAUI's IQueryAttributable.
    /// </summary>
    internal Type? InnerContentType { get; set; }

    private VisualElement? _stateRoot;
    private VisualElement? _buttonsRow;
    private VisualElement? _resolutionLabel;
    private Button? _approveButton;
    private Button? _rejectButton;

    public ToolApprovalView()
    {
        ApproveCommand = new AsyncRelayCommand(() => RespondAsync(true));
        RejectCommand = new AsyncRelayCommand(() => RespondAsync(false));
    }

    protected override void RefreshFromContentContext()
    {
        Refresh();
    }

    private void Refresh()
    {
        RefreshToolName();
        BuildInnerContent();
        RefreshApprovalState();
    }

    private void RefreshToolName()
    {
        ToolName = ContentContext?.ToolName;
    }

    private void RefreshApprovalState()
    {
        IsPending = ContentContext?.ApprovalState == ToolApprovalState.Pending;
        IsResolved = ContentContext?.ApprovalResolved ?? false;
        ResolutionText = ContentContext?.ApprovalResolutionText;

        if (_buttonsRow is not null)
        {
            _buttonsRow.IsVisible = !IsResolved;
            _buttonsRow.IsEnabled = !IsResolved;
        }

        if (_resolutionLabel is not null)
            _resolutionLabel.IsVisible = IsResolved;

        if (Content is View innerView)
            innerView.IsEnabled = !IsResolved;

        ApplyVisualState();
        RefreshAutomationIds();
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _stateRoot = GetTemplateChild("PART_Root") as VisualElement;
        _buttonsRow = GetTemplateChild("ButtonsRow") as VisualElement;
        _resolutionLabel = GetTemplateChild("ResolutionLabel") as VisualElement;
        _approveButton = GetTemplateChild("PART_ApproveButton") as Button;
        _rejectButton = GetTemplateChild("PART_RejectButton") as Button;

        if (ContentContext is not null)
            Refresh();

        ApplyVisualState();
        RefreshAutomationIds();
    }

    private void BuildInnerContent()
    {
        if (ContentContext is null)
            return;

        View innerView;
        if (InnerContentType is not null)
        {
            innerView = ContentTemplate.CreateView(InnerContentType, Handler?.MauiContext?.Services);

            (innerView as IContentContextAware)?.ApplyContentContext(ContentContext);
        }
        else
        {
            innerView = BuildDefaultArgsView();
        }

        if (ContentContext?.ApprovalResolved == true)
            innerView.IsEnabled = false;

        Content = innerView;
    }

    private void ApplyVisualState()
    {
        VisualStateManager.GoToState(_stateRoot ?? this, IsResolved ? "Resolved" : "Pending");
    }

    private void RefreshAutomationIds()
    {
        if (_approveButton is not null && string.IsNullOrWhiteSpace(_approveButton.AutomationId))
            _approveButton.AutomationId = ActiveApproveAutomationId;

        if (_rejectButton is not null && string.IsNullOrWhiteSpace(_rejectButton.AutomationId))
            _rejectButton.AutomationId = ActiveRejectAutomationId;
    }

    private View BuildDefaultArgsView()
    {
        if (ContentContext?.Content is not ToolApprovalRequestContent approval ||
            approval.ToolCall is not FunctionCallContent fc ||
            fc.Arguments is null || fc.Arguments.Count == 0)
        {
            return ApplyStyleResource(
                new Label { Text = "(no arguments)" },
                ChatThemeKeys.ToolApprovalEmptyArgsLabelStyle);
        }

        var stack = ApplyStyleResource(
            new VerticalStackLayout(),
            ChatThemeKeys.ToolApprovalArgsStackStyle);

        foreach (var kvp in fc.Arguments)
        {
            var row = ApplyStyleResource(
                new HorizontalStackLayout(),
                ChatThemeKeys.ToolApprovalArgsRowStyle);

            row.Add(ApplyStyleResource(
                new Label { Text = $"{kvp.Key}:" },
                ChatThemeKeys.ToolApprovalArgNameLabelStyle));

            row.Add(ApplyStyleResource(
                new Label { Text = kvp.Value?.ToString() ?? "(null)" },
                ChatThemeKeys.ToolApprovalArgValueLabelStyle));

            stack.Add(row);
        }

        return stack;
    }

    private static T ApplyStyleResource<T>(T view, string resourceKey) where T : VisualElement
    {
        view.SetDynamicResource(StyleProperty, resourceKey);
        return view;
    }

    private async Task RespondAsync(bool approved)
    {
        if (ContentContext is null || ContentContext.Content is not ToolApprovalRequestContent request)
            return;

        var response = ResolveResponseFactory()?.CreateApprovalResponse(request, approved)
            ?? request.CreateResponse(approved, approved ? null : "User rejected");

        await ContentContext.Session.SubmitApprovalAsync(response);
    }

    private IToolApprovalResponseFactory? ResolveResponseFactory()
    {
        if (Content is IToolApprovalResponseFactory viewFactory)
            return viewFactory;

        return null;
    }
}
