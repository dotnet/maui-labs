using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.Chat.Controls;

public partial class AIChatView
{
    private readonly ChatComposer _composer;
    private readonly ChatMessageList _messageList;
    private readonly Command<string> _suggestionCommand;
    private readonly Command<DataContent> _removeAttachmentCommand;
    private readonly List<IDisposable> _sessionSubscriptions = [];
    private readonly HashSet<BlockTemplate> _hookedTemplates = [];
    private OwnedSession? _ownedSession;
    private AgentContext? _effectiveSession;
    private IList<BlockTemplate>? _observedTemplates;
    private IList<string>? _observedSuggestions;
    private bool _isLoaded;
    private IChatAudioRecorder? _defaultRecorder;
    private IChatSpeechRecognizer? _defaultRecognizer;
    private ContentView? _headerPart;
    private ContentView? _messagesPart;
    private ContentView? _emptyViewPart;
    private View? _defaultEmptyContent;
    private Layout? _suggestionsPart;
    private Layout? _attachmentsPart;
    private InputView? _inputPart;
    private AIChatTheme? _fallbackTheme;

    /// <summary>Creates a chat view with the default theme and template.</summary>
    public AIChatView()
    {
        _composer = new ChatComposer(this);
        _messageList = new ChatMessageList(this);

        SendCommand = new Command(() => _ = SendAsync(), () => CanSend);
        StopCommand = new Command(() => _ = StopAsync(), () => CanStop);
        PickAttachmentsCommand = new Command(() => _ = PickAttachmentsAsync(), () => _composer.CanPickAttachments);
        ToggleAudioCaptureCommand = new Command(() => _ = _composer.ToggleAudioCaptureAsync(), () => _composer.CanToggleAudioCapture);
        ToggleLiveSpeechCommand = new Command(() => _ = _composer.ToggleLiveSpeechAsync(), () => _composer.CanToggleLiveSpeech);
        _suggestionCommand = new Command<string>(
            suggestion => _ = _composer.SendTextAsync(suggestion ?? string.Empty),
            _ => CanSendSuggestion);
        _removeAttachmentCommand = new Command<DataContent>(attachment =>
        {
            if (attachment is not null)
                _composer.RemoveAttachment(attachment);
        });

        EnsureTheme();
        SetDynamicResource(ControlTemplateProperty, AIChatThemeKeys.ChatViewTemplate);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        UpdateState();
    }

    internal ChatComposer Composer => _composer;

    internal ChatMessageList MessageList => _messageList;

    internal OwnedSession? OwnedChatSession => _ownedSession;

    /// <summary>Replaces the app's service provider (for tests).</summary>
    internal IServiceProvider? ServiceProviderOverride { get; set; }

    internal IServiceProvider? Services =>
        ServiceProviderOverride ?? Handler?.MauiContext?.Services ?? Application.Current?.Handler?.MauiContext?.Services;

    /// <summary>Gets <see cref="Session"/>, or the session the view created for <see cref="ChatClient"/>.</summary>
    internal AgentContext? EffectiveSession => _effectiveSession;

    internal bool IsSessionBusy =>
        _effectiveSession?.Status is ConversationStatus.Streaming or ConversationStatus.AwaitingInput;

    private bool CanSendSuggestion =>
        _effectiveSession is not null && IsInputEnabled && !IsSessionBusy && !_composer.IsSending && !_composer.IsComposing;

    internal IChatAttachmentPicker GetAttachmentPicker() =>
        AttachmentPicker ?? Services?.GetService<IChatAttachmentPicker>() ?? FileChatAttachmentPicker.Default;

    internal IChatAudioRecorder GetAudioRecorder() =>
        AudioRecorder ?? Services?.GetService<IChatAudioRecorder>() ?? (_defaultRecorder ??= new MauiChatAudioRecorder());

    internal IChatSpeechRecognizer GetSpeechRecognizer() =>
        SpeechRecognizer ?? Services?.GetService<IChatSpeechRecognizer>() ?? (_defaultRecognizer ??= new MauiChatSpeechRecognizer());

    internal IChatAudioTranscriber? GetAudioTranscriber() =>
        AudioTranscriber ?? Services?.GetService<IChatAudioTranscriber>();

    /// <summary>Selects an app template (never a built-in) that can render <paramref name="block"/>.</summary>
    internal BlockTemplate? SelectAppTemplate(ContentBlock block) =>
        BuiltInBlockTemplates.Select(block, ContentTemplates, includeBuiltIns: false) is { CanRender: true } template
            ? template
            : null;

    internal void OnComposerChanged() => UpdateState();

    internal void OnSendStarted() => _messageList.PinToEnd();

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_messagesPart is not null)
            _messagesPart.Content = null;

        _headerPart = FindPart<ContentView>(HeaderPartName);
        _messagesPart = FindPart<ContentView>(MessagesPartName);
        _emptyViewPart = FindPart<ContentView>(EmptyViewPartName);
        _defaultEmptyContent = _emptyViewPart?.Content;
        _suggestionsPart = FindPart<Layout>(SuggestionsPartName);
        _attachmentsPart = FindPart<Layout>(AttachmentsPartName);
        _inputPart = FindPart<InputView>(InputPartName);

        if (_messagesPart is not null)
            _messagesPart.Content = _messageList;

        InheritBindingContext();
        ApplyHeader();
        ApplyEmptyView();
        RebuildSuggestions();
        RebuildAttachments();
    }

    private T? FindPart<T>(string name)
        where T : class
    {
        try
        {
            return GetTemplateChild(name) as T;
        }
        catch (InvalidOperationException)
        {
            // A template built in code has no name scope, and so no parts.
            return null;
        }
    }

    /// <inheritdoc />
    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        InheritBindingContext();
        foreach (var template in _hookedTemplates)
            SetInheritedBindingContext(template, BindingContext);
    }

    /// <summary>
    /// Gives the template the view's binding context. <see cref="TemplatedView"/> does not, but the header, empty
    /// view, and input slots hold app content that binds to the page's view model.
    /// </summary>
    private void InheritBindingContext()
    {
        foreach (var child in ((IVisualTreeElement)this).GetVisualChildren())
        {
            if (child is BindableObject root)
                SetInheritedBindingContext(root, BindingContext);
        }

        // A collection view header does not inherit its list's binding context.
        if (_messageList.Header is BindableObject header)
            SetInheritedBindingContext(header, BindingContext);
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────────────────────────────────

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (_isLoaded)
            return;

        EnsureTheme();
        ObserveTemplates(ContentTemplates);
        ObserveSuggestions(Suggestions);

        // Fall back to an IChatClient from the app's services once a handler can provide them.
        if (Session is null && _ownedSession is null)
            UpdateEffectiveSession(recreateOwned: false);

        _isLoaded = true;
        _messageList.InvalidateTemplates();
        AttachSession();
        RebuildSuggestions();
        UpdateState();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (!_isLoaded)
            return;

        // Release everything that would let the session (or app collections) keep this view alive.
        _isLoaded = false;
        ObserveTemplates(null);
        ObserveSuggestions(null);
        DetachSession();
        _composer.CancelCapture();
    }

    /// <inheritdoc/>
    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        base.OnHandlerChanging(args);

        // The view is going away (MAUI disconnects the handlers of a popped page), unlike a plain Unloaded such as
        // a tab switch: stop the session this view created, so a response doesn't keep streaming and running tools.
        if (args.OldHandler is not null && args.NewHandler is null)
            ReleaseOwnedSession();
    }

    private void EnsureTheme()
    {
        if (AIChatTheme.EnsureLoaded())
        {
            // The app now has the theme; a private copy would hide the app's own overrides.
            if (_fallbackTheme is not null)
            {
                Resources.MergedDictionaries.Remove(_fallbackTheme);
                _fallbackTheme = null;
            }

            return;
        }

        // No application yet (or none at all, as in unit tests): carry the theme with the view.
        if (_fallbackTheme is null)
        {
            _fallbackTheme = new AIChatTheme();
            Resources.MergedDictionaries.Add(_fallbackTheme);
        }
    }

    private void AttachSession()
    {
        if (_effectiveSession is { } session)
        {
            _sessionSubscriptions.Add(session.RegisterOnStatusChanged(_ => this.RunOnOwnerThread(UpdateState)));
            _sessionSubscriptions.Add(session.RegisterOnTurnAdded(_ => this.RunOnOwnerThread(UpdateState)));
        }

        _messageList.Attach(_effectiveSession);
    }

    private void DetachSession()
    {
        foreach (var subscription in _sessionSubscriptions)
            subscription.Dispose();
        _sessionSubscriptions.Clear();
        _messageList.Detach();
    }

    // ── Session ownership ───────────────────────────────────────────────────────────────────────────────

    private void UpdateEffectiveSession(bool recreateOwned)
    {
        var previousOwned = _ownedSession;
        if (Session is not null)
            _ownedSession = null;
        else if (recreateOwned || _ownedSession is null)
            _ownedSession = CreateOwnedSession();

        var effective = Session ?? _ownedSession?.Context;
        if (!ReferenceEquals(effective, _effectiveSession))
        {
            var hadSession = _effectiveSession is not null;
            if (_isLoaded)
                DetachSession();

            _effectiveSession = effective;

            // A different conversation must not inherit the old draft or in-flight capture.
            _composer.Reset(clearDraft: hadSession);

            if (_isLoaded)
                AttachSession();
        }

        if (previousOwned is not null && !ReferenceEquals(previousOwned, _ownedSession))
            previousOwned.Dispose();

        UpdateState();
    }

    /// <summary>Stops and disposes the session this view created. An app's <see cref="Session"/> is the app's to dispose.</summary>
    private void ReleaseOwnedSession()
    {
        if (_ownedSession is not { } owned)
            return;

        _ownedSession = null;
        if (ReferenceEquals(_effectiveSession, owned.Context))
        {
            if (_isLoaded)
                DetachSession();

            _effectiveSession = null;
            _composer.Reset(clearDraft: true);

            if (_isLoaded)
                AttachSession();
        }

        owned.Dispose();
        UpdateState();
    }

    private OwnedSession? CreateOwnedSession()
    {
        var client = ChatClient ?? Services?.GetService<IChatClient>();
        return client is null ? null : OwnedSession.Create(client, Instructions, Tools);
    }

    private void UpdateOwnedOptions() => _ownedSession?.Update(Instructions, Tools);

    private async Task ClearSessionAsync()
    {
        if (_effectiveSession is { } session)
            await session.ClearAsync();
        UpdateState();
    }

    // ── Templates and collections ───────────────────────────────────────────────────────────────────────

    private void OnContentTemplatesChanged(IList<BlockTemplate>? oldValue, IList<BlockTemplate>? newValue)
    {
        if (_isLoaded)
            ObserveTemplates(newValue);
        _messageList.InvalidateTemplates();
    }

    private void ObserveTemplates(IList<BlockTemplate>? templates)
    {
        if (!ReferenceEquals(_observedTemplates, templates))
        {
            if (_observedTemplates is INotifyCollectionChanged previous)
                previous.CollectionChanged -= OnTemplatesCollectionChanged;
            _observedTemplates = templates;
            if (templates is INotifyCollectionChanged current)
                current.CollectionChanged += OnTemplatesCollectionChanged;
        }

        HookTemplates();
    }

    private void HookTemplates()
    {
        var current = _observedTemplates?.Where(static template => template is not null).ToHashSet() ?? [];
        foreach (var template in _hookedTemplates.Where(template => !current.Contains(template)).ToArray())
        {
            template.PropertyChanged -= OnTemplatePropertyChanged;
            _hookedTemplates.Remove(template);
        }

        foreach (var template in current)
        {
            if (_hookedTemplates.Add(template))
            {
                template.PropertyChanged += OnTemplatePropertyChanged;
                SetInheritedBindingContext(template, BindingContext);
            }
        }
    }

    private void OnTemplatesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HookTemplates();
        _messageList.InvalidateTemplates();
    }

    private void OnTemplatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != BindingContextProperty.PropertyName)
            _messageList.InvalidateTemplates();
    }

    private void OnSuggestionsChanged(IList<string>? oldValue, IList<string>? newValue)
    {
        if (_isLoaded)
            ObserveSuggestions(newValue);
        RebuildSuggestions();
    }

    private void ObserveSuggestions(IList<string>? suggestions)
    {
        if (ReferenceEquals(_observedSuggestions, suggestions))
            return;

        if (_observedSuggestions is INotifyCollectionChanged previous)
            previous.CollectionChanged -= OnSuggestionsCollectionChanged;
        _observedSuggestions = suggestions;
        if (suggestions is INotifyCollectionChanged current)
            current.CollectionChanged += OnSuggestionsCollectionChanged;
    }

    private void OnSuggestionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildSuggestions();

    private void OnCaptureOptionChanged()
    {
        if ((!AllowAudioCapture && (_composer.IsRecordingAudio || _composer.IsTranscribingAudio)) ||
            (!AllowLiveSpeech && _composer.IsListening))
        {
            _composer.CancelCapture();
        }

        UpdateState();
    }

    private void OnCaptureServiceChanged()
    {
        // A replaced recorder or recognizer must not finish an operation the old one started.
        _composer.CancelCapture();
        UpdateState();
    }

    // ── Template parts ──────────────────────────────────────────────────────────────────────────────────

    private void ApplyHeader()
    {
        if (_headerPart is null)
            return;

        var content = CreateContent(HeaderTemplate);
        _headerPart.Content = content;
        _headerPart.IsVisible = content is not null;
    }

    private void ApplyEmptyView()
    {
        if (_emptyViewPart is not null)
            _emptyViewPart.Content = CreateContent(EmptyViewTemplate) ?? _defaultEmptyContent;
    }

    private void ApplyMessagesHeader()
    {
        _messageList.Header = CreateContent(MessagesHeaderTemplate);
        InheritBindingContext();
    }

    private void RebuildSuggestions()
    {
        if (_suggestionsPart is not null)
        {
            _suggestionsPart.Children.Clear();
            foreach (var suggestion in GetSuggestions())
            {
                var chip = new Button
                {
                    Text = suggestion,
                    Command = _suggestionCommand,
                    CommandParameter = suggestion,
                    AutomationId = "AIChatSuggestion",
                }.Styled(AIChatThemeKeys.SuggestionStyle);
                SemanticProperties.SetHint(chip, "Sends this message");
                _suggestionsPart.Children.Add(chip);
            }
        }

        UpdateState();
    }

    private void RebuildAttachments()
    {
        if (_attachmentsPart is null)
            return;

        _attachmentsPart.Children.Clear();
        foreach (var attachment in Attachments)
        {
            var remove = new Button
            {
                Text = "\u2715",
                FontSize = 12,
                WidthRequest = 28,
                HeightRequest = 28,
                VerticalOptions = LayoutOptions.Center,
                Command = _removeAttachmentCommand,
                CommandParameter = attachment,
                AutomationId = "AIChatRemoveAttachment",
            }.Styled(AIChatThemeKeys.IconButtonStyle);
            SemanticProperties.SetDescription(remove, $"Remove {attachment.Name ?? "attachment"}");
            _attachmentsPart.Children.Add(ChatVisuals.CreateFileChip(attachment, trailing: remove));
        }
    }

    private IEnumerable<string> GetSuggestions() =>
        Suggestions?.Where(static suggestion => !string.IsNullOrWhiteSpace(suggestion)) ?? [];

    private static View? CreateContent(DataTemplate? template) =>
        template?.CreateContent() switch
        {
            View view => view,
            _ => null,
        };

    // ── State ───────────────────────────────────────────────────────────────────────────────────────────

    private void UpdateState()
    {
        // Bindable property callbacks can run before the constructor finishes.
        if (_composer is null)
            return;

        var session = _effectiveSession;
        var isBusy = IsSessionBusy;
        var isEmpty = session is null || session.Turns.Count == 0;

        SetValue(IsBusyPropertyKey, isBusy);
        SetValue(IsEmptyPropertyKey, isEmpty);
        SetValue(ShowSuggestionsPropertyKey, isEmpty && GetSuggestions().Any());
        SetValue(CanStopPropertyKey, isBusy);
        SetValue(CanSendPropertyKey, _composer.CanSend);
        SetValue(ErrorMessagePropertyKey, _composer.ErrorMessage);
        SetValue(HasErrorPropertyKey, !string.IsNullOrEmpty(_composer.ErrorMessage));
        SetValue(IsRecordingAudioPropertyKey, _composer.IsRecordingAudio);
        SetValue(IsTranscribingAudioPropertyKey, _composer.IsTranscribingAudio);
        SetValue(IsListeningPropertyKey, _composer.IsListening);
        SetValue(CanCaptureAudioPropertyKey, AllowAudioCapture && GetAudioRecorder().IsSupported);
        SetValue(CanUseLiveSpeechPropertyKey, AllowLiveSpeech && GetSpeechRecognizer().IsSupported);

        var attachments = _composer.Attachments;
        if (!Attachments.SequenceEqual(attachments))
        {
            SetValue(AttachmentsPropertyKey, attachments.ToArray());
            RebuildAttachments();
        }

        SetValue(HasAttachmentsPropertyKey, attachments.Count > 0);

        ((Command)SendCommand).ChangeCanExecute();
        ((Command)StopCommand).ChangeCanExecute();
        ((Command)PickAttachmentsCommand).ChangeCanExecute();
        ((Command)ToggleAudioCaptureCommand).ChangeCanExecute();
        ((Command)ToggleLiveSpeechCommand).ChangeCanExecute();
        _suggestionCommand.ChangeCanExecute();
    }
}

/// <summary>A session the view created for <see cref="AIChatView.ChatClient"/> (or a DI client) and disposes.</summary>
internal sealed class OwnedSession : IDisposable
{
    private OwnedSession(IChatClient source, IChatClient client, ChatOptions options, UIAgent agent)
    {
        Source = source;
        Client = client;
        Options = options;
        Agent = agent;
        Context = new AgentContext(agent);
    }

    /// <summary>Gets the client the app provided.</summary>
    public IChatClient Source { get; }

    /// <summary>Gets the client the session uses: the source, wrapped for function invocation when needed.</summary>
    public IChatClient Client { get; }

    /// <summary>Gets the options the agent reads on every request, so edits apply to the next message.</summary>
    public ChatOptions Options { get; }

    public UIAgent Agent { get; }

    public AgentContext Context { get; }

    public static OwnedSession Create(IChatClient source, string? instructions, IList<AITool>? tools)
    {
        // The engine needs a FunctionInvokingChatClient to run tools; add one unless the app already did.
        var client = source.GetService<FunctionInvokingChatClient>() is null
            ? source.AsBuilder().UseFunctionInvocation().Build()
            : source;

        var options = new ChatOptions();
        var session = new OwnedSession(source, client, options, new UIAgent(client, agentOptions => agentOptions.ChatOptions = options));
        session.Update(instructions, tools);
        return session;
    }

    public void Update(string? instructions, IList<AITool>? tools)
    {
        Options.Instructions = instructions;
        Options.Tools = tools;
    }

    public void Dispose()
    {
        // The app owns the source client, and the function-invocation wrapper would dispose it, so neither is
        // disposed here.
        Context.Dispose();
        Agent.Dispose();
    }
}
