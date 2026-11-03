// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Provides one streamed response update to a state mapper and lets it consume content that should
/// update application state instead of producing visible content blocks.
/// </summary>
/// <remarks>
/// State mapping runs before block mapping for assistant updates. This type is single-thread-affine
/// and is not thread-safe.
/// </remarks>
public sealed class StateMapperContext
{
    private readonly bool[] _handled;
    private int _handledCount;

    internal StateMapperContext(ChatResponseUpdate update)
    {
        Update = update;
        _handled = new bool[update.Contents.Count];
    }

    /// <summary>Gets the original response update.</summary>
    public ChatResponseUpdate Update { get; }

    /// <summary>Enumerates content items that have not been consumed by this mapper.</summary>
    public UnhandledContentsEnumerable UnhandledContents => new(Update.Contents, _handled);

    /// <summary>Marks one content item as state-only so it does not reach the block pipeline.</summary>
    public void MarkHandled(AIContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var contents = Update.Contents;
        for (var i = 0; i < contents.Count; i++)
        {
            if (!ReferenceEquals(contents[i], content))
                continue;

            if (!_handled[i])
            {
                _handled[i] = true;
                _handledCount++;
            }
            return;
        }
    }

    /// <summary>Gets the state value supplied by the mapper, if any.</summary>
    public object? StateValue { get; private set; }

    /// <summary>Gets whether <see cref="StateValue"/> is provisional until explicitly accepted.</summary>
    public bool IsPredictiveState { get; private set; }

    /// <summary>Supplies the new typed state value for a <see cref="UIAgent{TState}"/>.</summary>
    public void SetState(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        StateValue = value;
        IsPredictiveState = false;
    }

    /// <summary>
    /// Supplies a provisional typed state value that is rolled back when the current turn ends
    /// unless the application accepts it.
    /// </summary>
    public void SetPredictiveState(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        StateValue = value;
        IsPredictiveState = true;
    }

    internal bool HasHandledContent => _handledCount > 0;

    internal ChatResponseUpdate GetFilteredUpdate()
    {
        if (_handledCount == 0)
            return Update;

        var filtered = Update.Clone();
        filtered.Contents =
        [
            .. Update.Contents.Where(
                (_, index) => !_handled[index]),
        ];
        return filtered;
    }

    /// <summary>A lightweight enumerable over unhandled content.</summary>
    public readonly struct UnhandledContentsEnumerable
    {
        private readonly IList<AIContent> _contents;
        private readonly bool[] _handled;

        internal UnhandledContentsEnumerable(IList<AIContent> contents, bool[] handled)
        {
            _contents = contents;
            _handled = handled;
        }

        public UnhandledContentsEnumerator GetEnumerator() => new(_contents, _handled);
    }

    /// <summary>Enumerates unhandled content without allocating an intermediate collection.</summary>
    public struct UnhandledContentsEnumerator
    {
        private readonly IList<AIContent> _contents;
        private readonly bool[] _handled;
        private int _index;

        internal UnhandledContentsEnumerator(IList<AIContent> contents, bool[] handled)
        {
            _contents = contents;
            _handled = handled;
            _index = -1;
        }

        public AIContent Current => _contents[_index];

        public bool MoveNext()
        {
            while (++_index < _contents.Count)
            {
                if (!_handled[_index])
                    return true;
            }
            return false;
        }
    }
}
