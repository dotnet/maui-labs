// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Chat.Controls.Blazor;

/// <summary>Overrides the automatic <c>prefers-color-scheme</c> theme applied to a <see cref="ChatView"/>.</summary>
public enum ChatViewTheme
{
    /// <summary>Follow the browser's <c>prefers-color-scheme</c>.</summary>
    System,

    /// <summary>Force the light theme.</summary>
    Light,

    /// <summary>Force the dark theme.</summary>
    Dark,
}
