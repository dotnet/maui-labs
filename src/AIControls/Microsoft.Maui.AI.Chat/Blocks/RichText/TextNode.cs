// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat;

public class TextNode : RichTextNode
{
    public TextNode()
    {
    }

    public TextNode(string text)
    {
        Text = text;
    }

    public string Text { get; set; } = string.Empty;
}
