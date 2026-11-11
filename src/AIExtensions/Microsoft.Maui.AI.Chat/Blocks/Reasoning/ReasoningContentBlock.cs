// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Text;

namespace Microsoft.Maui.AI.Chat;

/// <summary>Streaming model reasoning, including provider-protected reasoning data.</summary>
/// <remarks>
/// Raises <see cref="ContentBlock.PropertyChanged"/> for <see cref="Text"/>, <see cref="ProtectedData"/> and
/// <see cref="IsEncrypted"/> when they change.
/// </remarks>
public class ReasoningContentBlock : ContentBlock
{
    private static readonly PropertyChangedEventArgs s_textChanged = new(nameof(Text));
    private static readonly PropertyChangedEventArgs s_isEncryptedChanged = new(nameof(IsEncrypted));

    private readonly StringBuilder _builder = new();
    private string? _text;
    private string? _protectedData;

    public string Text => _text ??= _builder.ToString();

    internal int TextLength => _builder.Length;

    public string? ProtectedData
    {
        get => _protectedData;
        set
        {
            var wasEncrypted = IsEncrypted;
            if (SetProperty(ref _protectedData, value))
                ReportIsEncryptedChange(wasEncrypted);
        }
    }

    public bool IsEncrypted => ProtectedData is not null && _builder.Length == 0;

    /// <summary>Gets or sets the id of the message the reasoning belongs to, when the provider sends one.</summary>
    internal string? MessageId { get; set; }

    public void AppendText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
            return;

        var wasEncrypted = IsEncrypted;
        _builder.Append(text);
        _text = null;
        RaisePropertyChanged(s_textChanged);
        ReportIsEncryptedChange(wasEncrypted);
    }

    private void ReportIsEncryptedChange(bool wasEncrypted)
    {
        if (IsEncrypted != wasEncrypted)
            RaisePropertyChanged(s_isEncryptedChanged);
    }
}
