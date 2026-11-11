namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Resource keys used by <see cref="AIChatView"/> and its built-in views.
/// </summary>
/// <remarks>
/// <para>
/// Every visual reads these keys as dynamic resources, so defining a key in your application (or page)
/// resources overrides the default from <see cref="AIChatTheme"/> at runtime.
/// </para>
/// <para>
/// Colors come in three forms. <c>AIChat.Color.*</c> is the active color for both themes. The defaults are
/// copied from <c>AIChat.Light.*</c> or <c>AIChat.Dark.*</c> whenever the app theme changes, so you can either
/// override <c>AIChat.Color.Accent</c> once, or <c>AIChat.Light.Accent</c> and <c>AIChat.Dark.Accent</c>
/// separately.
/// </para>
/// </remarks>
public static class AIChatThemeKeys
{
    /// <summary>The prefix of the light palette, for example <c>AIChat.Light.Accent</c>.</summary>
    public const string LightPalettePrefix = "AIChat.Light.";

    /// <summary>The prefix of the dark palette, for example <c>AIChat.Dark.Accent</c>.</summary>
    public const string DarkPalettePrefix = "AIChat.Dark.";

    /// <summary>The primary text color.</summary>
    public const string TextColor = "AIChat.Color.Text";

    /// <summary>Muted text: placeholders, status lines, and tool details.</summary>
    public const string SecondaryTextColor = "AIChat.Color.SecondaryText";

    /// <summary>The accent color: the send button, links, and primary actions.</summary>
    public const string AccentColor = "AIChat.Color.Accent";

    /// <summary>Text and glyphs drawn on the accent color.</summary>
    public const string OnAccentColor = "AIChat.Color.OnAccent";

    /// <summary>The background of the user's message bubble.</summary>
    public const string UserBubbleColor = "AIChat.Color.UserBubble";

    /// <summary>The text color inside the user's message bubble.</summary>
    public const string UserTextColor = "AIChat.Color.UserText";

    /// <summary>The background of cards, chips, and the message box.</summary>
    public const string SurfaceColor = "AIChat.Color.Surface";

    /// <summary>The outline of cards, chips, and the message box.</summary>
    public const string StrokeColor = "AIChat.Color.Stroke";

    /// <summary>The background of code blocks and tool arguments.</summary>
    public const string CodeBackgroundColor = "AIChat.Color.CodeBackground";

    /// <summary>Error text.</summary>
    public const string ErrorColor = "AIChat.Color.Error";

    /// <summary>The background of error cards.</summary>
    public const string ErrorBackgroundColor = "AIChat.Color.ErrorBackground";

    /// <summary>Positive status, such as an approved tool call.</summary>
    public const string SuccessColor = "AIChat.Color.Success";

    /// <summary>
    /// The body font size (a <see cref="double"/>). Markdown answers take their size from it, and their headings
    /// scale from it.
    /// </summary>
    public const string BodyFontSize = "AIChat.FontSize.Body";

    /// <summary>The font size of secondary text (a <see cref="double"/>).</summary>
    public const string SmallFontSize = "AIChat.FontSize.Small";

    /// <summary>The font size of code (a <see cref="double"/>).</summary>
    public const string CodeFontSize = "AIChat.FontSize.Code";

    /// <summary>
    /// The font family of every built-in text except code (a <see cref="string"/>). <see langword="null"/> by default,
    /// which is the platform font; set it to your app's font, for example <c>OpenSansRegular</c>.
    /// </summary>
    public const string FontFamily = "AIChat.FontFamily";

    /// <summary>The monospace font family used for code (a <see cref="string"/>).</summary>
    public const string CodeFontFamily = "AIChat.FontFamily.Code";

    /// <summary>The <see cref="ControlTemplate"/> applied to <see cref="AIChatView"/>.</summary>
    public const string ChatViewTemplate = "AIChat.Template.ChatView";

    /// <summary>The <see cref="Style"/> of the <see cref="ContentView"/> that hosts each message row.</summary>
    public const string RowStyle = "AIChat.Style.Row";

    /// <summary>The <see cref="Style"/> of the user's message bubble (<see cref="Border"/>).</summary>
    public const string UserBubbleStyle = "AIChat.Style.UserBubble";

    /// <summary>The <see cref="Style"/> of the user's message text (<see cref="Label"/>).</summary>
    public const string UserTextStyle = "AIChat.Style.UserText";

    /// <summary>
    /// The <see cref="Style"/> of assistant text (<see cref="Label"/>): Markdown paragraphs and list items, and the
    /// welcome title. A <c>FontSize</c> setter does not resize Markdown, which takes its sizes from
    /// <see cref="BodyFontSize"/> and <see cref="CodeFontSize"/>.
    /// </summary>
    public const string AssistantTextStyle = "AIChat.Style.AssistantText";

    /// <summary>The <see cref="Style"/> of muted text (<see cref="Label"/>).</summary>
    public const string SecondaryTextStyle = "AIChat.Style.SecondaryText";

    /// <summary>The <see cref="Style"/> of tool, approval, action, and file cards (<see cref="Border"/>).</summary>
    public const string CardStyle = "AIChat.Style.Card";

    /// <summary>The <see cref="Style"/> of a card's title (<see cref="Label"/>).</summary>
    public const string CardTitleStyle = "AIChat.Style.CardTitle";

    /// <summary>The <see cref="Style"/> of Markdown code blocks and tool arguments (<see cref="Border"/>).</summary>
    public const string CodeBlockStyle = "AIChat.Style.CodeBlock";

    /// <summary>The <see cref="Style"/> of code text (<see cref="Label"/>).</summary>
    public const string CodeTextStyle = "AIChat.Style.CodeText";

    /// <summary>The <see cref="Style"/> of links in Markdown (<see cref="Span"/>).</summary>
    public const string LinkSpanStyle = "AIChat.Style.LinkSpan";

    /// <summary>The <see cref="Style"/> of the error card (<see cref="Border"/>).</summary>
    public const string ErrorCardStyle = "AIChat.Style.ErrorCard";

    /// <summary>The <see cref="Style"/> of error text (<see cref="Label"/>).</summary>
    public const string ErrorTextStyle = "AIChat.Style.ErrorText";

    /// <summary>The <see cref="Style"/> of primary actions such as Approve and Retry (<see cref="Button"/>).</summary>
    public const string PrimaryButtonStyle = "AIChat.Style.PrimaryButton";

    /// <summary>The <see cref="Style"/> of secondary actions such as Reject (<see cref="Button"/>).</summary>
    public const string SecondaryButtonStyle = "AIChat.Style.SecondaryButton";

    /// <summary>The <see cref="Style"/> of the button that shows or hides the model's reasoning (<see cref="Button"/>).</summary>
    public const string ReasoningHeaderStyle = "AIChat.Style.ReasoningHeader";

    /// <summary>The <see cref="Style"/> of suggestion chips (<see cref="Button"/>).</summary>
    public const string SuggestionStyle = "AIChat.Style.Suggestion";

    /// <summary>The <see cref="Style"/> of staged attachment chips (<see cref="Border"/>).</summary>
    public const string AttachmentChipStyle = "AIChat.Style.AttachmentChip";

    /// <summary>The <see cref="Style"/> of the message box container (<see cref="Border"/>).</summary>
    public const string ComposerStyle = "AIChat.Style.Composer";

    /// <summary>The <see cref="Style"/> of the message box (<see cref="Editor"/>).</summary>
    public const string InputStyle = "AIChat.Style.Input";

    /// <summary>The <see cref="Style"/> of the attach, audio, and live voice buttons (<see cref="Button"/>).</summary>
    public const string IconButtonStyle = "AIChat.Style.IconButton";

    /// <summary>The <see cref="Style"/> of the send and stop buttons (<see cref="Button"/>).</summary>
    public const string SendButtonStyle = "AIChat.Style.SendButton";

    // Markdown. Paragraphs use AssistantTextStyle, links LinkSpanStyle, and fenced code CodeBlockStyle and
    // CodeTextStyle; colors and fonts come from the AIChat.Color.*, AIChat.FontSize.* and AIChat.FontFamily.* keys.

    /// <summary>The vertical space between top-level Markdown blocks (a <see cref="double"/>).</summary>
    public const string MarkdownBlockSpacing = "AIChat.Markdown.BlockSpacing";

    /// <summary>
    /// The <see cref="Style"/> of Markdown headings (<see cref="Label"/>). Their sizes scale from
    /// <see cref="BodyFontSize"/>.
    /// </summary>
    public const string MarkdownHeadingStyle = "AIChat.Markdown.HeadingStyle";

    /// <summary>The <see cref="Style"/> of inline <c>code</c> in Markdown (<see cref="Span"/>).</summary>
    public const string MarkdownInlineCodeStyle = "AIChat.Markdown.InlineCodeStyle";

    /// <summary>The <see cref="Style"/> of the language caption above fenced code (<see cref="Label"/>).</summary>
    public const string MarkdownCodeBlockLanguageStyle = "AIChat.Markdown.CodeBlockLanguageStyle";

    /// <summary>The <see cref="Style"/> of the bar beside a Markdown block quote (<see cref="BoxView"/>).</summary>
    public const string MarkdownBlockQuoteBarStyle = "AIChat.Markdown.BlockQuoteBarStyle";

    /// <summary>The <see cref="Style"/> of text inside a Markdown block quote (<see cref="Label"/>).</summary>
    public const string MarkdownBlockQuoteTextStyle = "AIChat.Markdown.BlockQuoteTextStyle";

    /// <summary>The <see cref="Style"/> of Markdown list bullets, numbers, and task boxes (<see cref="Label"/>).</summary>
    public const string MarkdownListMarkerStyle = "AIChat.Markdown.ListMarkerStyle";

    /// <summary>The <see cref="Style"/> of the outline around a Markdown table (<see cref="Border"/>).</summary>
    public const string MarkdownTableBorderStyle = "AIChat.Markdown.TableBorderStyle";

    /// <summary>The <see cref="Style"/> of the lines between Markdown table cells (<see cref="BoxView"/>).</summary>
    public const string MarkdownTableLineStyle = "AIChat.Markdown.TableLineStyle";

    /// <summary>The <see cref="Style"/> of Markdown table header cells (<see cref="Label"/>).</summary>
    public const string MarkdownTableHeaderCellStyle = "AIChat.Markdown.TableHeaderCellStyle";

    /// <summary>The <see cref="Style"/> of Markdown table body cells (<see cref="Label"/>).</summary>
    public const string MarkdownTableCellStyle = "AIChat.Markdown.TableCellStyle";

    /// <summary>The <see cref="Style"/> of Markdown thematic breaks, <c>---</c> (<see cref="BoxView"/>).</summary>
    public const string MarkdownThematicBreakStyle = "AIChat.Markdown.ThematicBreakStyle";
}
