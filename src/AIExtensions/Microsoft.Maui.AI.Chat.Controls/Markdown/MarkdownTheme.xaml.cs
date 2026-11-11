// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Default light and dark styles for rendered Markdown (keys in <see cref="MarkdownThemeKeys"/>).</summary>
internal partial class MarkdownTheme : ResourceDictionary
{
    private static MarkdownTheme? s_defaults;

    public MarkdownTheme() => InitializeComponent();

    // Shared fallback for views whose element tree does not (yet) provide a key. UI-thread only.
    internal static MarkdownTheme Defaults => s_defaults ??= new MarkdownTheme();
}
