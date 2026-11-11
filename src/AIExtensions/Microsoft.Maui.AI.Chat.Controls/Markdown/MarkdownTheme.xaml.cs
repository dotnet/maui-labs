// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// The Markdown styles (the <c>AIChat.Markdown.*</c> keys in <see cref="AIChatThemeKeys"/>). <see cref="AIChatTheme"/>
/// merges one instance, so the styles load, and are overridden, with the rest of the chat theme.
/// </summary>
internal partial class MarkdownTheme : ResourceDictionary
{
    public MarkdownTheme() => InitializeComponent();
}
