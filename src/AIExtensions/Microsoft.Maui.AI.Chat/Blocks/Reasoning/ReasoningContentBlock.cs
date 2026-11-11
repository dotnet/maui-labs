// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;

namespace Microsoft.Maui.AI.Chat;

/// <summary>Streaming model reasoning, including provider-protected reasoning data.</summary>
public class ReasoningContentBlock : ContentBlock
{
    private readonly StringBuilder _builder = new();
    private string? _text;

    public string Text => _text ??= _builder.ToString();

    internal int TextLength => _builder.Length;

    public string? ProtectedData { get; set; }

    public bool IsEncrypted => ProtectedData is not null && _builder.Length == 0;

    /// <summary>Gets or sets the id of the message the reasoning belongs to, when the provider sends one.</summary>
    internal string? MessageId { get; set; }

    public void AppendText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _builder.Append(text);
        _text = null;
    }
}
