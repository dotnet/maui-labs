using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using Microsoft.Maui.Chat.Controls.Themes;
using Microsoft.Maui.Controls.Shapes;

namespace Microsoft.Maui.Chat.Controls;

/// <summary>
/// A complete chat surface: header, message list, welcome and empty states, busy indicator,
/// suggestions, footer, and a composer with text and attachments.
/// </summary>
/// <remarks>
/// <para>
/// The whole visual tree comes from a replaceable <see cref="ControlTemplate"/>. Sections are located by
/// well-known part names, and every part is optional — omit one to drop that section:
/// </para>
/// <list type="bullet">
/// <item><c>PART_Header</c> — <see cref="ContentView"/> hosting <see cref="HeaderTemplate"/></item>
/// <item><c>PART_MessageList</c> — the <see cref="ChatMessagesView"/> that renders the conversation</item>
/// <item><c>PART_WelcomePanel</c> — shown while the conversation is empty</item>
/// <item><c>PART_WelcomeIcon</c> — the welcome glyph</item>
/// <item><c>PART_WelcomeMessage</c> — the welcome text</item>
/// <item><c>PART_EmptyView</c> — <see cref="ContentView"/> hosting <see cref="EmptyViewTemplate"/></item>
/// <item><c>PART_BusyIndicator</c> — reflects <see cref="IsBusy"/></item>
/// <item><c>PART_Suggestions</c> — <see cref="Layout"/> filled from <see cref="Suggestions"/></item>
/// <item><c>PART_Footer</c> — <see cref="ContentView"/> hosting <see cref="FooterTemplate"/></item>
/// <item><c>PART_InputArea</c> — the composer container</item>
/// <item><c>PART_Attachments</c> — <see cref="Layout"/> filled from <see cref="Attachments"/></item>
/// <item><c>PART_AttachButton</c> — opens the attachment picker</item>
/// <item><c>PART_InputEntry</c> — the text input, two-way bound to <see cref="Text"/></item>
/// <item><c>PART_SendButton</c> — sends the draft</item>
/// </list>
/// <para>
/// Sending is guarded against reentrancy, so a double tap or an <c>Enter</c> keypress landing on top of a
/// tap cannot send twice, and the composer is cleared only when the conversation accepted the draft.
/// Expected failures surface as the already user-safe <see cref="SendError"/> and
/// <see cref="AttachmentError"/> strings instead of throwing from an event handler.
/// </para>
/// <para>
/// <b>Threading:</b> the control and its conversation are single-thread affine. Drive them from the UI
/// thread only.
/// </para>
/// </remarks>
[ContentProperty(nameof(ContentTemplates))]
public class ChatView : TemplatedView
{
    /// <summary>The name of the header host part.</summary>
    public const string HeaderPartName = "PART_Header";

    /// <summary>The name of the <see cref="ChatMessagesView"/> part.</summary>
    public const string MessageListPartName = "PART_MessageList";

    /// <summary>The name of the welcome panel part.</summary>
    public const string WelcomePanelPartName = "PART_WelcomePanel";

    /// <summary>The name of the welcome icon part.</summary>
    public const string WelcomeIconPartName = "PART_WelcomeIcon";

    /// <summary>The name of the welcome message part.</summary>
    public const string WelcomeMessagePartName = "PART_WelcomeMessage";

    /// <summary>The name of the custom empty-state host part.</summary>
    public const string EmptyViewPartName = "PART_EmptyView";

    /// <summary>The name of the busy indicator part.</summary>
    public const string BusyIndicatorPartName = "PART_BusyIndicator";

    /// <summary>The name of the suggestions layout part.</summary>
    public const string SuggestionsPartName = "PART_Suggestions";

    /// <summary>The name of the footer host part.</summary>
    public const string FooterPartName = "PART_Footer";

    /// <summary>The name of the composer container part.</summary>
    public const string InputAreaPartName = "PART_InputArea";

    /// <summary>The name of the attachments layout part.</summary>
    public const string AttachmentsPartName = "PART_Attachments";

    /// <summary>The name of the attach button part.</summary>
    public const string AttachButtonPartName = "PART_AttachButton";

    /// <summary>The name of the text input part.</summary>
    public const string InputEntryPartName = "PART_InputEntry";

    /// <summary>The name of the send button part.</summary>
    public const string SendButtonPartName = "PART_SendButton";

    /// <summary>The generic message shown when sending fails.</summary>
    public const string DefaultSendErrorMessage = "Your message could not be sent. Please try again.";

    /// <summary>The generic message shown when adding an attachment fails.</summary>
    public const string DefaultAttachmentErrorMessage = "That attachment could not be added.";

    // ── Bindable properties ──

    /// <summary>Backing property for <see cref="Conversation"/>.</summary>
    public static readonly BindableProperty ConversationProperty =
        BindableProperty.Create(
            nameof(Conversation),
            typeof(ChatConversation),
            typeof(ChatView),
            propertyChanged: static (bindable, oldValue, newValue) =>
                ((ChatView)bindable).OnConversationChanged(
                    oldValue as ChatConversation,
                    newValue as ChatConversation));

    /// <summary>Backing property for <see cref="Text"/>.</summary>
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(
            nameof(Text),
            typeof(string),
            typeof(ChatView),
            string.Empty,
            BindingMode.TwoWay,
            propertyChanged: static (bindable, _, _) => ((ChatView)bindable).UpdateCanSend());

    /// <summary>Backing property for <see cref="ContentTemplates"/>.</summary>
    public static readonly BindableProperty ContentTemplatesProperty =
        BindableProperty.Create(
            nameof(ContentTemplates),
            typeof(IList<ChatContentTemplate>),
            typeof(ChatView),
            defaultValueCreator: static _ => new ObservableCollection<ChatContentTemplate>());

    /// <summary>Backing property for <see cref="UseDefaultContentTemplates"/>.</summary>
    public static readonly BindableProperty UseDefaultContentTemplatesProperty =
        BindableProperty.Create(nameof(UseDefaultContentTemplates), typeof(bool), typeof(ChatView), true);

    /// <summary>Backing property for <see cref="Appearance"/>.</summary>
    public static readonly BindableProperty AppearanceProperty =
        BindableProperty.Create(
            nameof(Appearance),
            typeof(ChatAppearance),
            typeof(ChatView),
            defaultValueCreator: static _ => new ChatAppearance());

    /// <summary>Backing property for <see cref="AutoScrollToLatest"/>.</summary>
    public static readonly BindableProperty AutoScrollToLatestProperty =
        BindableProperty.Create(nameof(AutoScrollToLatest), typeof(bool), typeof(ChatView), true);

    /// <summary>Backing property for <see cref="HeaderTemplate"/>.</summary>
    public static readonly BindableProperty HeaderTemplateProperty =
        BindableProperty.Create(
            nameof(HeaderTemplate),
            typeof(DataTemplate),
            typeof(ChatView),
            propertyChanged: static (bindable, _, _) => ((ChatView)bindable).ApplyHeaderTemplate());

    /// <summary>Backing property for <see cref="FooterTemplate"/>.</summary>
    public static readonly BindableProperty FooterTemplateProperty =
        BindableProperty.Create(
            nameof(FooterTemplate),
            typeof(DataTemplate),
            typeof(ChatView),
            propertyChanged: static (bindable, _, _) => ((ChatView)bindable).ApplyFooterTemplate());

    /// <summary>Backing property for <see cref="EmptyViewTemplate"/>.</summary>
    public static readonly BindableProperty EmptyViewTemplateProperty =
        BindableProperty.Create(
            nameof(EmptyViewTemplate),
            typeof(DataTemplate),
            typeof(ChatView),
            propertyChanged: static (bindable, _, _) => ((ChatView)bindable).ApplyEmptyViewTemplate());

    /// <summary>Backing property for <see cref="Suggestions"/>.</summary>
    public static readonly BindableProperty SuggestionsProperty =
        BindableProperty.Create(
            nameof(Suggestions),
            typeof(IList<ChatSuggestion>),
            typeof(ChatView),
            defaultValueCreator: static _ => new ObservableCollection<ChatSuggestion>(),
            propertyChanged: static (bindable, oldValue, newValue) =>
            {
                var view = (ChatView)bindable;
                view.Rehook(oldValue, newValue, view.OnSuggestionsChanged);
                view.RebuildSuggestions();
            });

    /// <summary>Backing property for <see cref="SuggestionPrompts"/>.</summary>
    public static readonly BindableProperty SuggestionPromptsProperty =
        BindableProperty.Create(
            nameof(SuggestionPrompts),
            typeof(IList<string>),
            typeof(ChatView),
            defaultValueCreator: static _ => new ObservableCollection<string>(),
            propertyChanged: static (bindable, oldValue, newValue) =>
            {
                var view = (ChatView)bindable;
                view.Rehook(oldValue, newValue, view.OnSuggestionsChanged);
                view.RebuildSuggestions();
            });

    /// <summary>Backing property for <see cref="SuggestionTemplate"/>.</summary>
    public static readonly BindableProperty SuggestionTemplateProperty =
        BindableProperty.Create(
            nameof(SuggestionTemplate),
            typeof(DataTemplate),
            typeof(ChatView),
            propertyChanged: static (bindable, _, _) => ((ChatView)bindable).ApplySuggestionTemplate());

    /// <summary>Backing property for <see cref="AttachmentTemplate"/>.</summary>
    public static readonly BindableProperty AttachmentTemplateProperty =
        BindableProperty.Create(
            nameof(AttachmentTemplate),
            typeof(DataTemplate),
            typeof(ChatView),
            propertyChanged: static (bindable, _, _) => ((ChatView)bindable).ApplyAttachmentTemplate());

    /// <summary>Backing property for <see cref="AllowAttachments"/>.</summary>
    public static readonly BindableProperty AllowAttachmentsProperty =
        BindableProperty.Create(nameof(AllowAttachments), typeof(bool), typeof(ChatView), false);

    /// <summary>Backing property for <see cref="AttachmentPicker"/>.</summary>
    public static readonly BindableProperty AttachmentPickerProperty =
        BindableProperty.Create(nameof(AttachmentPicker), typeof(IChatAttachmentPicker), typeof(ChatView));

    /// <summary>Backing property for <see cref="AttachmentFileTypes"/>.</summary>
    public static readonly BindableProperty AttachmentFileTypesProperty =
        BindableProperty.Create(nameof(AttachmentFileTypes), typeof(FilePickerFileType), typeof(ChatView));

    /// <summary>Backing property for <see cref="MaxAttachmentBytes"/>.</summary>
    public static readonly BindableProperty MaxAttachmentBytesProperty =
        BindableProperty.Create(nameof(MaxAttachmentBytes), typeof(long), typeof(ChatView), 10L * 1024 * 1024);

    /// <summary>Backing property for <see cref="Placeholder"/>.</summary>
    public static readonly BindableProperty PlaceholderProperty =
        BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(ChatView), "Type a message");

    /// <summary>Backing property for <see cref="SendButtonText"/>.</summary>
    public static readonly BindableProperty SendButtonTextProperty =
        BindableProperty.Create(nameof(SendButtonText), typeof(string), typeof(ChatView), "➤");

    /// <summary>Backing property for <see cref="AttachButtonText"/>.</summary>
    public static readonly BindableProperty AttachButtonTextProperty =
        BindableProperty.Create(nameof(AttachButtonText), typeof(string), typeof(ChatView), "＋");

    /// <summary>Backing property for <see cref="WelcomeMessage"/>.</summary>
    public static readonly BindableProperty WelcomeMessageProperty =
        BindableProperty.Create(nameof(WelcomeMessage), typeof(string), typeof(ChatView), "Start the conversation");

    /// <summary>Backing property for <see cref="WelcomeIcon"/>.</summary>
    public static readonly BindableProperty WelcomeIconProperty =
        BindableProperty.Create(nameof(WelcomeIcon), typeof(string), typeof(ChatView), "💬");

    private static readonly BindablePropertyKey IsBusyPropertyKey =
        BindableProperty.CreateReadOnly(nameof(IsBusy), typeof(bool), typeof(ChatView), false);

    /// <summary>Backing property for <see cref="IsBusy"/>.</summary>
    public static readonly BindableProperty IsBusyProperty = IsBusyPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey SendErrorPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(SendError),
            typeof(string),
            typeof(ChatView),
            null,
            propertyChanged: static (bindable, _, newValue) =>
                ((ChatView)bindable).SetValue(HasSendErrorPropertyKey, newValue is string { Length: > 0 }));

    /// <summary>Backing property for <see cref="SendError"/>.</summary>
    public static readonly BindableProperty SendErrorProperty = SendErrorPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey HasSendErrorPropertyKey =
        BindableProperty.CreateReadOnly(nameof(HasSendError), typeof(bool), typeof(ChatView), false);

    /// <summary>Backing property for <see cref="HasSendError"/>.</summary>
    public static readonly BindableProperty HasSendErrorProperty = HasSendErrorPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey AttachmentErrorPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(AttachmentError),
            typeof(string),
            typeof(ChatView),
            null,
            propertyChanged: static (bindable, _, newValue) =>
                ((ChatView)bindable).SetValue(HasAttachmentErrorPropertyKey, newValue is string { Length: > 0 }));

    /// <summary>Backing property for <see cref="AttachmentError"/>.</summary>
    public static readonly BindableProperty AttachmentErrorProperty = AttachmentErrorPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey HasAttachmentErrorPropertyKey =
        BindableProperty.CreateReadOnly(nameof(HasAttachmentError), typeof(bool), typeof(ChatView), false);

    /// <summary>Backing property for <see cref="HasAttachmentError"/>.</summary>
    public static readonly BindableProperty HasAttachmentErrorProperty =
        HasAttachmentErrorPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey AttachmentsPropertyKey =
        BindableProperty.CreateReadOnly(
            nameof(Attachments),
            typeof(ReadOnlyObservableCollection<ChatAttachment>),
            typeof(ChatView),
            null);

    /// <summary>Backing property for <see cref="Attachments"/>.</summary>
    public static readonly BindableProperty AttachmentsProperty = AttachmentsPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey HasAttachmentsPropertyKey =
        BindableProperty.CreateReadOnly(nameof(HasAttachments), typeof(bool), typeof(ChatView), false);

    /// <summary>Backing property for <see cref="HasAttachments"/>.</summary>
    public static readonly BindableProperty HasAttachmentsProperty = HasAttachmentsPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey IsEmptyPropertyKey =
        BindableProperty.CreateReadOnly(nameof(IsEmpty), typeof(bool), typeof(ChatView), true);

    /// <summary>Backing property for <see cref="IsEmpty"/>.</summary>
    public static readonly BindableProperty IsEmptyProperty = IsEmptyPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey ShowWelcomePropertyKey =
        BindableProperty.CreateReadOnly(nameof(ShowWelcome), typeof(bool), typeof(ChatView), true);

    /// <summary>Backing property for <see cref="ShowWelcome"/>.</summary>
    public static readonly BindableProperty ShowWelcomeProperty = ShowWelcomePropertyKey.BindableProperty;

    private static readonly BindablePropertyKey ShowEmptyViewPropertyKey =
        BindableProperty.CreateReadOnly(nameof(ShowEmptyView), typeof(bool), typeof(ChatView), false);

    /// <summary>Backing property for <see cref="ShowEmptyView"/>.</summary>
    public static readonly BindableProperty ShowEmptyViewProperty = ShowEmptyViewPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey ShowSuggestionsPropertyKey =
        BindableProperty.CreateReadOnly(nameof(ShowSuggestions), typeof(bool), typeof(ChatView), false);

    /// <summary>Backing property for <see cref="ShowSuggestions"/>.</summary>
    public static readonly BindableProperty ShowSuggestionsProperty = ShowSuggestionsPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey CanSendPropertyKey =
        BindableProperty.CreateReadOnly(nameof(CanSend), typeof(bool), typeof(ChatView), false);

    /// <summary>Backing property for <see cref="CanSend"/>.</summary>
    public static readonly BindableProperty CanSendProperty = CanSendPropertyKey.BindableProperty;

    private readonly ObservableCollection<ChatAttachment> _attachments = [];
    private readonly ObservableCollection<ChatSuggestion> _effectiveSuggestions = [];
    private readonly ICommand _suggestionCommand;
    private readonly ICommand _removeAttachmentCommand;

    private DataTemplate? _defaultSuggestionTemplate;
    private DataTemplate? _defaultAttachmentTemplate;
    private IDisposable? _conversationSubscription;
    private ContentView? _headerPart;
    private ContentView? _footerPart;
    private ContentView? _emptyViewPart;
    private Layout? _suggestionsPart;
    private Layout? _attachmentsPart;
    private Entry? _inputEntryPart;
    private Button? _sendButtonPart;
    private Button? _attachButtonPart;
    private bool _isSending;

    /// <summary>Creates the view and applies the default control template.</summary>
    public ChatView()
    {
        SetValue(AttachmentsPropertyKey, new ReadOnlyObservableCollection<ChatAttachment>(_attachments));
        EffectiveSuggestions = new ReadOnlyObservableCollection<ChatSuggestion>(_effectiveSuggestions);

        _attachments.CollectionChanged += OnAttachmentsChanged;
        _suggestionCommand = new Command<ChatSuggestion>(suggestion => _ = SendSuggestionAsync(suggestion));
        _removeAttachmentCommand = new Command<ChatAttachment>(attachment => RemoveAttachment(attachment));

        if (Suggestions is INotifyCollectionChanged suggestions)
            suggestions.CollectionChanged += OnSuggestionsChanged;
        if (SuggestionPrompts is INotifyCollectionChanged prompts)
            prompts.CollectionChanged += OnSuggestionsChanged;

        SetDynamicResource(ControlTemplateProperty, ChatThemeKeys.ChatViewTemplate);
    }

    // ── Public surface ──

    /// <summary>Gets or sets the conversation this view renders and sends to.</summary>
    public ChatConversation? Conversation
    {
        get => (ChatConversation?)GetValue(ConversationProperty);
        set => SetValue(ConversationProperty, value);
    }

    /// <summary>Gets or sets the composer text. Two-way bound to the input part.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Gets or sets the consumer content templates, forwarded to the message list.</summary>
    public IList<ChatContentTemplate> ContentTemplates
    {
        get => (IList<ChatContentTemplate>)GetValue(ContentTemplatesProperty);
        set => SetValue(ContentTemplatesProperty, value);
    }

    /// <summary>Gets or sets whether the built-in content templates are used as fallbacks.</summary>
    public bool UseDefaultContentTemplates
    {
        get => (bool)GetValue(UseDefaultContentTemplatesProperty);
        set => SetValue(UseDefaultContentTemplatesProperty, value);
    }

    /// <summary>Gets or sets the styling applied to every rendered row.</summary>
    public ChatAppearance Appearance
    {
        get => (ChatAppearance)GetValue(AppearanceProperty);
        set => SetValue(AppearanceProperty, value);
    }

    /// <summary>Gets or sets whether the message list follows the latest row.</summary>
    public bool AutoScrollToLatest
    {
        get => (bool)GetValue(AutoScrollToLatestProperty);
        set => SetValue(AutoScrollToLatestProperty, value);
    }

    /// <summary>Gets or sets the template shown in the header part.</summary>
    public DataTemplate? HeaderTemplate
    {
        get => (DataTemplate?)GetValue(HeaderTemplateProperty);
        set => SetValue(HeaderTemplateProperty, value);
    }

    /// <summary>Gets or sets the template shown in the footer part.</summary>
    public DataTemplate? FooterTemplate
    {
        get => (DataTemplate?)GetValue(FooterTemplateProperty);
        set => SetValue(FooterTemplateProperty, value);
    }

    /// <summary>Gets or sets the template shown instead of the built-in welcome panel while the conversation is empty.</summary>
    public DataTemplate? EmptyViewTemplate
    {
        get => (DataTemplate?)GetValue(EmptyViewTemplateProperty);
        set => SetValue(EmptyViewTemplateProperty, value);
    }

    /// <summary>Gets or sets the rich suggestions offered while the conversation is empty.</summary>
    public IList<ChatSuggestion> Suggestions
    {
        get => (IList<ChatSuggestion>)GetValue(SuggestionsProperty);
        set => SetValue(SuggestionsProperty, value);
    }

    /// <summary>Gets or sets plain-string suggestions, a shorthand for <see cref="Suggestions"/>.</summary>
    public IList<string> SuggestionPrompts
    {
        get => (IList<string>)GetValue(SuggestionPromptsProperty);
        set => SetValue(SuggestionPromptsProperty, value);
    }

    /// <summary>Gets or sets the template used for each suggestion chip. Its binding context is a <see cref="ChatSuggestion"/>.</summary>
    public DataTemplate? SuggestionTemplate
    {
        get => (DataTemplate?)GetValue(SuggestionTemplateProperty);
        set => SetValue(SuggestionTemplateProperty, value);
    }

    /// <summary>Gets or sets the template used for each composer attachment. Its binding context is a <see cref="ChatAttachment"/>.</summary>
    public DataTemplate? AttachmentTemplate
    {
        get => (DataTemplate?)GetValue(AttachmentTemplateProperty);
        set => SetValue(AttachmentTemplateProperty, value);
    }

    /// <summary>Gets or sets whether the attach button is shown.</summary>
    public bool AllowAttachments
    {
        get => (bool)GetValue(AllowAttachmentsProperty);
        set => SetValue(AllowAttachmentsProperty, value);
    }

    /// <summary>Gets or sets the picker used by the attach button. Defaults to <see cref="FileChatAttachmentPicker.Default"/>.</summary>
    public IChatAttachmentPicker? AttachmentPicker
    {
        get => (IChatAttachmentPicker?)GetValue(AttachmentPickerProperty);
        set => SetValue(AttachmentPickerProperty, value);
    }

    /// <summary>Gets or sets the file types the picker allows.</summary>
    public FilePickerFileType? AttachmentFileTypes
    {
        get => (FilePickerFileType?)GetValue(AttachmentFileTypesProperty);
        set => SetValue(AttachmentFileTypesProperty, value);
    }

    /// <summary>Gets or sets the largest accepted attachment size in bytes. Defaults to 10 MB.</summary>
    public long MaxAttachmentBytes
    {
        get => (long)GetValue(MaxAttachmentBytesProperty);
        set => SetValue(MaxAttachmentBytesProperty, value);
    }

    /// <summary>Gets or sets the composer placeholder.</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>Gets or sets the send button caption.</summary>
    public string SendButtonText
    {
        get => (string)GetValue(SendButtonTextProperty);
        set => SetValue(SendButtonTextProperty, value);
    }

    /// <summary>Gets or sets the attach button caption.</summary>
    public string AttachButtonText
    {
        get => (string)GetValue(AttachButtonTextProperty);
        set => SetValue(AttachButtonTextProperty, value);
    }

    /// <summary>Gets or sets the welcome message shown while the conversation is empty.</summary>
    public string WelcomeMessage
    {
        get => (string)GetValue(WelcomeMessageProperty);
        set => SetValue(WelcomeMessageProperty, value);
    }

    /// <summary>Gets or sets the welcome glyph shown while the conversation is empty.</summary>
    public string WelcomeIcon
    {
        get => (string)GetValue(WelcomeIconProperty);
        set => SetValue(WelcomeIconProperty, value);
    }

    /// <summary>Gets whether the conversation is busy. Driven by <see cref="ChatConversation.Status"/>.</summary>
    public bool IsBusy => (bool)GetValue(IsBusyProperty);

    /// <summary>Gets a generic, user-safe message describing the last failed send, or <see langword="null"/>.</summary>
    public string? SendError => (string?)GetValue(SendErrorProperty);

    /// <summary>Gets whether <see cref="SendError"/> has a value, for binding visibility.</summary>
    public bool HasSendError => (bool)GetValue(HasSendErrorProperty);

    /// <summary>Gets a generic, user-safe message describing the last failed attachment pick, or <see langword="null"/>.</summary>
    public string? AttachmentError => (string?)GetValue(AttachmentErrorProperty);

    /// <summary>Gets whether <see cref="AttachmentError"/> has a value, for binding visibility.</summary>
    public bool HasAttachmentError => (bool)GetValue(HasAttachmentErrorProperty);

    /// <summary>Gets the attachments staged in the composer.</summary>
    public ReadOnlyObservableCollection<ChatAttachment> Attachments =>
        (ReadOnlyObservableCollection<ChatAttachment>)GetValue(AttachmentsProperty);

    /// <summary>Gets whether the composer has staged attachments.</summary>
    public bool HasAttachments => (bool)GetValue(HasAttachmentsProperty);

    /// <summary>Gets whether the conversation has no messages.</summary>
    public bool IsEmpty => (bool)GetValue(IsEmptyProperty);

    /// <summary>Gets whether the built-in welcome panel should be shown.</summary>
    public bool ShowWelcome => (bool)GetValue(ShowWelcomeProperty);

    /// <summary>Gets whether the custom <see cref="EmptyViewTemplate"/> should be shown.</summary>
    public bool ShowEmptyView => (bool)GetValue(ShowEmptyViewProperty);

    /// <summary>Gets whether suggestion chips should be shown.</summary>
    public bool ShowSuggestions => (bool)GetValue(ShowSuggestionsProperty);

    /// <summary>Gets whether the current draft can be sent right now.</summary>
    public bool CanSend => (bool)GetValue(CanSendProperty);

    /// <summary>Gets the suggestions actually rendered: <see cref="Suggestions"/> followed by <see cref="SuggestionPrompts"/>.</summary>
    public ReadOnlyObservableCollection<ChatSuggestion> EffectiveSuggestions { get; }

    /// <summary>Creates the draft the composer would send right now.</summary>
    /// <returns>A draft carrying the trimmed text and the staged attachments.</returns>
    public ChatDraft CreateDraft() => new(Text, _attachments);

    /// <summary>Stages an attachment in the composer.</summary>
    /// <param name="attachment">The attachment to stage.</param>
    /// <exception cref="ArgumentNullException"><paramref name="attachment"/> is <see langword="null"/>.</exception>
    public void AddAttachment(ChatAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        _attachments.Add(attachment);
    }

    /// <summary>Removes a staged attachment.</summary>
    /// <param name="attachment">The attachment to remove.</param>
    /// <returns><see langword="true"/> when it was staged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="attachment"/> is <see langword="null"/>.</exception>
    public bool RemoveAttachment(ChatAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        return _attachments.Remove(attachment);
    }

    /// <summary>Removes every staged attachment.</summary>
    public void ClearAttachments() => _attachments.Clear();

    /// <summary>
    /// Sends the current draft. Does nothing when a send is already running, when there is no
    /// conversation, or when the conversation rejects the draft. Never throws: expected failures land in
    /// <see cref="SendError"/>.
    /// </summary>
    /// <returns>A task that completes when the send finished.</returns>
    public Task SendAsync() => SendCoreAsync();

    /// <summary>
    /// Prompts for attachments and stages the picked files. Never throws: expected failures land in
    /// <see cref="AttachmentError"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the pick.</param>
    /// <returns>A task that completes when picking finished.</returns>
    public async Task PickAttachmentsAsync(CancellationToken cancellationToken = default)
    {
        SetValue(AttachmentErrorPropertyKey, null);

        try
        {
            var picker = AttachmentPicker ?? FileChatAttachmentPicker.Default;
            var picked = await picker
                .PickAsync(AttachmentFileTypes, MaxAttachmentBytes, cancellationToken)
                .ConfigureAwait(true);

            if (picked is null)
                return;

            foreach (var attachment in picked)
            {
                if (attachment is not null)
                    _attachments.Add(attachment);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelling a picker is a normal outcome, not an error worth showing.
        }
        catch (Exception)
        {
            // Anything a picker throws is reported generically: raw details are never safe to show.
            SetValue(AttachmentErrorPropertyKey, DefaultAttachmentErrorMessage);
        }
    }

    /// <inheritdoc />
    protected override void OnParentSet()
    {
        base.OnParentSet();

        if (Parent is not null)
            ChatControlsTheme.EnsureLoaded();
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        DetachParts();

        _headerPart = FindPart<ContentView>(HeaderPartName);
        _footerPart = FindPart<ContentView>(FooterPartName);
        _emptyViewPart = FindPart<ContentView>(EmptyViewPartName);
        AttachSuggestionsPart(FindPart<Layout>(SuggestionsPartName));
        _attachmentsPart = FindPart<Layout>(AttachmentsPartName);
        _inputEntryPart = FindPart<Entry>(InputEntryPartName);
        _sendButtonPart = FindPart<Button>(SendButtonPartName);
        _attachButtonPart = FindPart<Button>(AttachButtonPartName);

        if (_inputEntryPart is not null)
            _inputEntryPart.Completed += OnInputCompleted;
        if (_sendButtonPart is not null)
            _sendButtonPart.Clicked += OnSendClicked;
        if (_attachButtonPart is not null)
            _attachButtonPart.Clicked += OnAttachClicked;

        ApplyHeaderTemplate();
        ApplyFooterTemplate();
        ApplyEmptyViewTemplate();
        ApplySuggestionTemplate();
        ApplyAttachmentTemplate();
        UpdateState();
    }

    /// <summary>
    /// Attaches the optional suggestions host. Derived controls can use this when adapting a compatible
    /// legacy control template or when supplying template parts in tests.
    /// </summary>
    /// <param name="suggestionsPart">The suggestions layout, or <see langword="null"/>.</param>
    protected void AttachSuggestionsPart(Layout? suggestionsPart)
    {
        _suggestionsPart = suggestionsPart;
        ApplySuggestionTemplate();
        UpdateState();
    }

    /// <summary>
    /// Finds a template part by name, tolerating templates that were built in code and therefore have no
    /// name scope. Every part is optional, so a missing one simply disables that section.
    /// </summary>
    /// <typeparam name="T">The expected part type.</typeparam>
    /// <param name="name">The part name.</param>
    /// <returns>The part, or <see langword="null"/> when the template does not provide it.</returns>
    protected T? FindPart<T>(string name)
        where T : Element
    {
        try
        {
            return GetTemplateChild(name) as T;
        }
        catch (InvalidOperationException)
        {
            // A control template created in code has no name scope, so it simply has no named parts.
            return null;
        }
    }

    private void DetachParts()
    {
        if (_inputEntryPart is not null)
            _inputEntryPart.Completed -= OnInputCompleted;
        if (_sendButtonPart is not null)
            _sendButtonPart.Clicked -= OnSendClicked;
        if (_attachButtonPart is not null)
            _attachButtonPart.Clicked -= OnAttachClicked;
    }

    // ── Conversation ──

    private void OnConversationChanged(ChatConversation? oldConversation, ChatConversation? newConversation)
    {
        _conversationSubscription?.Dispose();
        _conversationSubscription = null;

        SetValue(SendErrorPropertyKey, null);

        if (newConversation is not null)
            _conversationSubscription = newConversation.Subscribe(OnConversationChange);

        UpdateState();
    }

    private void OnConversationChange(ChatConversationChange change)
    {
        switch (change.Kind)
        {
            case ChatConversationChangeKind.StatusChanged:
            case ChatConversationChangeKind.MessageAdded:
            case ChatConversationChangeKind.MessageRemoved:
            case ChatConversationChangeKind.Reset:
                UpdateState();
                break;

            default:
                break;
        }
    }

    private void UpdateState()
    {
        var conversation = Conversation;
        var isEmpty = conversation is null || conversation.Messages.Count == 0;

        SetValue(IsBusyPropertyKey, conversation?.Status == ChatConversationStatus.Busy);
        SetValue(IsEmptyPropertyKey, isEmpty);
        SetValue(ShowEmptyViewPropertyKey, isEmpty && EmptyViewTemplate is not null);
        SetValue(ShowWelcomePropertyKey, isEmpty && EmptyViewTemplate is null);
        SetValue(ShowSuggestionsPropertyKey, isEmpty && _effectiveSuggestions.Count > 0);
        UpdateCanSend();
    }

    private void UpdateCanSend()
    {
        var conversation = Conversation;
        SetValue(CanSendPropertyKey, !_isSending && conversation is not null && conversation.CanSend(CreateDraft()));
    }

    // ── Send ──

    private void OnSendClicked(object? sender, EventArgs e) => _ = SendCoreAsync();

    private void OnInputCompleted(object? sender, EventArgs e) => _ = SendCoreAsync();

    private void OnAttachClicked(object? sender, EventArgs e) => _ = PickAttachmentsAsync();

    /// <summary>
    /// The single send path. A plain boolean guard is enough because the control is single-thread
    /// affine: a second tap or an <c>Enter</c> keypress arriving while the first send is awaiting is
    /// simply ignored, so no lock is involved.
    /// </summary>
    private async Task SendCoreAsync()
    {
        if (_isSending)
            return;

        var conversation = Conversation;
        if (conversation is null)
            return;

        var draft = CreateDraft();
        if (!conversation.CanSend(draft))
            return;

        _isSending = true;
        SetValue(SendErrorPropertyKey, null);
        UpdateCanSend();

        try
        {
            var accepted = await conversation.SendAsync(draft).ConfigureAwait(true);

            // Only an accepted draft is cleared: a rejected one stays so the user can retry it.
            if (accepted)
                ClearAcceptedDraft(draft);
        }
        catch (OperationCanceledException)
        {
            // A cancelled send is not a failure worth reporting.
        }
        catch (Exception)
        {
            // Event handlers must never surface raw exceptions, and raw text is never safe to show.
            SetValue(SendErrorPropertyKey, DefaultSendErrorMessage);
        }
        finally
        {
            _isSending = false;
            UpdateCanSend();
        }
    }

    private void ClearAcceptedDraft(ChatDraft acceptedDraft)
    {
        if (string.Equals(
            Text?.Trim(),
            acceptedDraft.Text,
            StringComparison.Ordinal))
        {
            Text = string.Empty;
        }

        foreach (var attachment in acceptedDraft.Attachments)
            _attachments.Remove(attachment);
    }

    private async Task SendSuggestionAsync(ChatSuggestion? suggestion)
    {
        if (suggestion is null)
            return;

        Text = suggestion.Prompt;
        await SendCoreAsync().ConfigureAwait(true);
    }

    // ── Templates and parts ──

    private void ApplyHeaderTemplate() => ApplyTemplateToHost(_headerPart, HeaderTemplate);

    private void ApplyFooterTemplate() => ApplyTemplateToHost(_footerPart, FooterTemplate);

    private void ApplyEmptyViewTemplate()
    {
        ApplyTemplateToHost(_emptyViewPart, EmptyViewTemplate);
        UpdateState();
    }

    private static void ApplyTemplateToHost(ContentView? host, DataTemplate? template)
    {
        if (host is null)
            return;

        if (template is null)
        {
            host.Content = null;
            host.IsVisible = false;
            return;
        }

        host.Content = template.CreateContent() as View;
        host.IsVisible = host.Content is not null;
    }

    private void ApplySuggestionTemplate()
    {
        if (_suggestionsPart is null)
            return;

        BindableLayout.SetItemsSource(_suggestionsPart, _effectiveSuggestions);
        BindableLayout.SetItemTemplate(
            _suggestionsPart,
            SuggestionTemplate ?? (_defaultSuggestionTemplate ??= CreateDefaultSuggestionTemplate()));
    }

    private void ApplyAttachmentTemplate()
    {
        if (_attachmentsPart is null)
            return;

        BindableLayout.SetItemsSource(_attachmentsPart, _attachments);
        BindableLayout.SetItemTemplate(
            _attachmentsPart,
            AttachmentTemplate ?? (_defaultAttachmentTemplate ??= CreateDefaultAttachmentTemplate()));
    }

    /// <summary>Creates the fallback chip template. Cached, because a list must see a stable template instance.</summary>
    private DataTemplate CreateDefaultSuggestionTemplate() =>
        new(() =>
        {
            var button = new Button
            {
                Margin = new Thickness(2),
                Command = _suggestionCommand,
            };

            button.SetBinding(Button.TextProperty, new Binding(nameof(ChatSuggestion.DisplayText)));
            button.SetBinding(Button.CommandParameterProperty, new Binding("."));
            button.SetBinding(
                SemanticProperties.DescriptionProperty,
                new Binding(nameof(ChatSuggestion.Label), stringFormat: "Suggested prompt: {0}"));
            button.SetDynamicResource(StyleProperty, ChatThemeKeys.SuggestionStyle);

            return button;
        });

    /// <summary>Creates the fallback attachment chip template. Cached, like the suggestion template.</summary>
    private DataTemplate CreateDefaultAttachmentTemplate() =>
        new(() =>
        {
            var label = new Label { VerticalOptions = LayoutOptions.Center };
            label.SetBinding(Label.TextProperty, new Binding(nameof(ChatAttachment.FileName)));

            var remove = new Button
            {
                Text = "✕",
                Padding = new Thickness(6, 0),
                BackgroundColor = Colors.Transparent,
                Command = _removeAttachmentCommand,
            };
            remove.SetBinding(Button.CommandParameterProperty, new Binding("."));
            remove.SetBinding(
                SemanticProperties.DescriptionProperty,
                new Binding(nameof(ChatAttachment.FileName), stringFormat: "Remove attachment {0}"));

            var chip = new Border
            {
                Padding = new Thickness(8, 2),
                Margin = new Thickness(2),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Content = new HorizontalStackLayout { Spacing = 4, Children = { label, remove } },
            };
            chip.SetDynamicResource(StyleProperty, ChatThemeKeys.AttachmentStyle);

            return chip;
        });

    // ── Suggestions and attachments ──

    private void Rehook(object? oldValue, object? newValue, NotifyCollectionChangedEventHandler handler)
    {
        if (oldValue is INotifyCollectionChanged oldCollection)
            oldCollection.CollectionChanged -= handler;
        if (newValue is INotifyCollectionChanged newCollection)
            newCollection.CollectionChanged += handler;
    }

    private void OnSuggestionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildSuggestions();

    private void RebuildSuggestions()
    {
        _effectiveSuggestions.Clear();

        foreach (var suggestion in Suggestions)
        {
            if (suggestion is not null)
                _effectiveSuggestions.Add(suggestion);
        }

        foreach (var prompt in SuggestionPrompts)
        {
            if (!string.IsNullOrWhiteSpace(prompt))
                _effectiveSuggestions.Add(new ChatSuggestion(prompt));
        }

        UpdateState();
    }

    private void OnAttachmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SetValue(HasAttachmentsPropertyKey, _attachments.Count > 0);
        UpdateCanSend();
    }
}
