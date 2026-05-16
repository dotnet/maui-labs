// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Maui.AI.Controls.Controls;

/// <summary>
/// Specifies where the sidebar panel is placed relative to the chat area.
/// </summary>
public enum SidebarPlacement
{
    /// <summary>Sidebar is hidden.</summary>
    None,

    /// <summary>Sidebar is placed to the left of the chat.</summary>
    Left,

    /// <summary>Sidebar is placed to the right of the chat.</summary>
    Right,

    /// <summary>Sidebar is placed below the chat (useful for narrow screens).</summary>
    Bottom
}
