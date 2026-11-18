// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Text;

namespace Microsoft.Maui.AI.Chat;

/// <summary>Streaming model reasoning, including provider-protected reasoning data.</summary>
/// <remarks>
/// Raises <see cref="ContentBlock.PropertyChanged"/> for <see cref="Text"/>, <see cref="ProtectedData"/>,
/// <see cref="IsEncrypted"/>, <see cref="StartedAt"/>, <see cref="CompletedAt"/>, and <see cref="Duration"/>
/// when they change.
/// </remarks>
public class ReasoningContentBlock : ContentBlock
{
    private static readonly PropertyChangedEventArgs s_textChanged = new(nameof(Text));
    private static readonly PropertyChangedEventArgs s_isEncryptedChanged = new(nameof(IsEncrypted));
    private static readonly PropertyChangedEventArgs s_startedAtChanged = new(nameof(StartedAt));
    private static readonly PropertyChangedEventArgs s_completedAtChanged = new(nameof(CompletedAt));
    private static readonly PropertyChangedEventArgs s_durationChanged = new(nameof(Duration));

    private readonly StringBuilder _builder = new();
    private string? _text;
    private string? _protectedData;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _completedAt;

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

    /// <summary>
    /// Gets when the reasoning started streaming, or <see langword="null"/> when restored from history.
    /// </summary>
    public DateTimeOffset? StartedAt
    {
        get => _startedAt;
        internal set
        {
            var previousDuration = Duration;
            if (SetProperty(ref _startedAt, value))
            {
                if (Duration != previousDuration)
                    RaisePropertyChanged(s_durationChanged);
            }
        }
    }

    /// <summary>
    /// Gets when the reasoning finished streaming, or <see langword="null"/> while it is still active or when
    /// restored from history.
    /// </summary>
    public DateTimeOffset? CompletedAt
    {
        get => _completedAt;
        internal set
        {
            var previousDuration = Duration;
            if (SetProperty(ref _completedAt, value))
            {
                if (Duration != previousDuration)
                    RaisePropertyChanged(s_durationChanged);
            }
        }
    }

    /// <summary>
    /// Gets how long the reasoning took once it has both <see cref="StartedAt"/> and <see cref="CompletedAt"/>, or
    /// <see langword="null"/> while it is still active or when restored from history.
    /// </summary>
    public TimeSpan? Duration =>
        _startedAt is { } start && _completedAt is { } end
            ? (end >= start ? end - start : TimeSpan.Zero)
            : null;

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

    internal void Complete(DateTimeOffset? completedAt)
    {
        var previousDuration = Duration;
        var completedAtChanged = completedAt is not null
            && _startedAt is not null
            && _completedAt is null
            && !EqualityComparer<DateTimeOffset?>.Default.Equals(_completedAt, completedAt);
        if (completedAtChanged)
            _completedAt = completedAt;

        LifecycleState = BlockLifecycleState.Inactive;
        if (completedAtChanged)
        {
            RaisePropertyChanged(s_completedAtChanged);
            if (Duration != previousDuration)
                RaisePropertyChanged(s_durationChanged);
        }
    }

    private void ReportIsEncryptedChange(bool wasEncrypted)
    {
        if (IsEncrypted != wasEncrypted)
            RaisePropertyChanged(s_isEncryptedChanged);
    }
}
