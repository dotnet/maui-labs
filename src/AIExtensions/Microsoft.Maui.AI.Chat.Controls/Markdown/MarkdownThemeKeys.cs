// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Resource keys the Markdown renderer resolves as dynamic resources.</summary>
/// <remarks>
/// <para>
/// Style keys follow the chat theme convention: light and dark values live inside the styles
/// through <c>AppThemeBinding</c> (see <c>MarkdownTheme.xaml</c>), so replacing one style in an
/// app, page or bubble scope restyles that element without losing the rest of the theme.
/// </para>
/// <para>
/// Every view also falls back to the matching default style from <see cref="MarkdownTheme"/>
/// when a key cannot be resolved, so Markdown renders sensibly before the theme is merged.
/// </para>
/// </remarks>
internal static class MarkdownThemeKeys
{
    private const string Prefix = "MauiAIChat.Markdown.";

    /// <summary><see cref="double"/>: body font size; headings and code scale from it.</summary>
    public const string FontSize = Prefix + "FontSize";

    /// <summary><see cref="double"/>: vertical space between top-level blocks.</summary>
    public const string BlockSpacing = Prefix + "BlockSpacing";

    /// <summary><see cref="Style"/> for <see cref="Label"/>: paragraphs and list item text.</summary>
    public const string TextStyle = Prefix + "TextStyle";

    /// <summary><see cref="Style"/> for <see cref="Label"/>: headings (levels 1–6).</summary>
    public const string HeadingStyle = Prefix + "HeadingStyle";

    /// <summary><see cref="Style"/> for <see cref="Span"/>: tappable links.</summary>
    public const string LinkStyle = Prefix + "LinkStyle";

    /// <summary><see cref="Style"/> for <see cref="Span"/>: inline <c>code</c>.</summary>
    public const string InlineCodeStyle = Prefix + "InlineCodeStyle";

    /// <summary><see cref="Style"/> for <see cref="Border"/>: the fenced code block container.</summary>
    public const string CodeBlockStyle = Prefix + "CodeBlockStyle";

    /// <summary><see cref="Style"/> for <see cref="Label"/>: fenced code text.</summary>
    public const string CodeBlockTextStyle = Prefix + "CodeBlockTextStyle";

    /// <summary><see cref="Style"/> for <see cref="Label"/>: the code block language caption.</summary>
    public const string CodeBlockLanguageStyle = Prefix + "CodeBlockLanguageStyle";

    /// <summary><see cref="Style"/> for <see cref="BoxView"/>: the bar beside a block quote.</summary>
    public const string BlockQuoteBarStyle = Prefix + "BlockQuoteBarStyle";

    /// <summary><see cref="Style"/> for <see cref="Label"/>: text inside a block quote.</summary>
    public const string BlockQuoteTextStyle = Prefix + "BlockQuoteTextStyle";

    /// <summary><see cref="Style"/> for <see cref="Label"/>: bullets, numbers and task boxes.</summary>
    public const string ListMarkerStyle = Prefix + "ListMarkerStyle";

    /// <summary><see cref="Style"/> for <see cref="Border"/>: the outline around a table.</summary>
    public const string TableBorderStyle = Prefix + "TableBorderStyle";

    /// <summary><see cref="Style"/> for <see cref="BoxView"/>: grid lines between table cells.</summary>
    public const string TableLineStyle = Prefix + "TableLineStyle";

    /// <summary><see cref="Style"/> for <see cref="Label"/>: table header cells.</summary>
    public const string TableHeaderCellStyle = Prefix + "TableHeaderCellStyle";

    /// <summary><see cref="Style"/> for <see cref="Label"/>: table body cells.</summary>
    public const string TableCellStyle = Prefix + "TableCellStyle";

    /// <summary><see cref="Style"/> for <see cref="BoxView"/>: thematic breaks (<c>---</c>).</summary>
    public const string ThematicBreakStyle = Prefix + "ThematicBreakStyle";
}
