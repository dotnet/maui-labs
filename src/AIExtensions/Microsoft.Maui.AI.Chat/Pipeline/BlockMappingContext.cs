// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// The per-update context handed to each <see cref="ContentBlockHandler{TState}"/>: exposes the current
/// <see cref="ChatResponseUpdate"/> and its not-yet-claimed content, and lets a handler mark content as handled.
/// </summary>
/// <remarks>Handlers iterate <see cref="UnhandledContents"/> and call <see cref="MarkHandled"/> to claim items.</remarks>
public class BlockMappingContext
{
    private readonly bool[] _handled;
    private int _handledCount;
    private int _progress;
    private int _firstClaimed = int.MaxValue;
    private bool _updateHandled;
    private readonly IReadOnlyList<IHandlerEntry>? _inactiveHandlers;
    private readonly FunctionCallRegistry? _calls;
    private List<FunctionCallContent>? _claimedCalls;

    internal BlockMappingContext(ChatResponseUpdate update)
        : this(update, inactiveHandlers: null)
    {
    }

    internal BlockMappingContext(
        ChatResponseUpdate update,
        IReadOnlyList<IHandlerEntry>? inactiveHandlers,
        FunctionCallRegistry? calls = null,
        bool claimsUIActions = true)
    {
        Update = update;
        _handled = new bool[update.Contents.Count];
        _inactiveHandlers = inactiveHandlers;
        _calls = calls;
        ClaimsUIActions = claimsUIActions;
    }

    public ChatResponseUpdate Update { get; }

    /// <summary>
    /// Gets whether registered UI actions claim their calls; not while a UI action builds the block
    /// that shows its call.
    /// </summary>
    internal bool ClaimsUIActions { get; }

    public UnhandledContentsEnumerable UnhandledContents => new(Update.Contents, _handled);

    /// <summary>Claims a content item of the update so no other handler shows it.</summary>
    /// <param name="content">The item. When the update lists the same instance more than once, each call claims one.</param>
    public void MarkHandled(AIContent content)
    {
        var index = IndexOf(content);
        if (index < 0 || _handled[index])
            return;

        _handled[index] = true;
        _handledCount++;
        RecordClaim(index);
        switch (content)
        {
            case FunctionCallContent call:
                (_claimedCalls ??= new()).Add(call);
                break;
            case FunctionResultContent result:
                _calls?.ReportAnswered(result.CallId);
                break;
        }
    }

    /// <summary>
    /// Returns the function calls marked handled since the last call, so the pipeline can record which block
    /// shows them, and starts collecting again.
    /// </summary>
    internal List<FunctionCallContent>? TakeClaimedCalls()
    {
        var claimed = _claimedCalls;
        _claimedCalls = null;
        return claimed;
    }

    public void MarkUpdateHandled()
    {
        _updateHandled = true;
    }

    /// <summary>
    /// Gets whether the update carries output: any content other than usage. An update without output,
    /// such as one that only reports a model id or usage, does not end the message a block is streaming.
    /// </summary>
    internal bool HasOutput
    {
        get
        {
            foreach (var content in Update.Contents)
            {
                if (content is not UsageContent)
                    return true;
            }

            return false;
        }
    }

    public bool AllHandled =>
        _handledCount >= Update.Contents.Count && (Update.Contents.Count > 0 || _updateHandled);

    internal int HandledProgress => _handledCount + _progress + (_updateHandled ? 1 : 0);

    /// <summary>
    /// Gets the position in the update of the first content claimed since <see cref="BeginClaim"/>, or
    /// <see cref="int.MaxValue"/> when none was.
    /// </summary>
    internal int FirstClaimed => _firstClaimed;

    /// <summary>Starts recording which contents the next handler claims.</summary>
    internal void BeginClaim() => _firstClaimed = int.MaxValue;

    /// <summary>
    /// Records that a handler emitted a block for part of a content item it has not marked handled
    /// yet, so the pipeline asks it again for the rest of the item.
    /// </summary>
    internal void ReportProgress(AIContent content)
    {
        _progress++;
        var index = IndexOf(content);
        if (index >= 0)
            RecordClaim(index);
    }

    private void RecordClaim(int index)
    {
        if (index < _firstClaimed)
            _firstClaimed = index;
    }

    /// <summary>
    /// Gets the position of the first unhandled occurrence of the item, or of its first occurrence when all
    /// are handled, or -1.
    /// </summary>
    private int IndexOf(AIContent content)
    {
        var contents = Update.Contents;
        var first = -1;
        for (var i = 0; i < contents.Count; i++)
        {
            if (!ReferenceEquals(contents[i], content))
                continue;

            if (!_handled[i])
                return i;

            if (first < 0)
                first = i;
        }

        return first;
    }

    public ContentBlock? CreateInnerBlock(AIContent content)
        => CreateInnerBlock(content, claimUIActions: true, out _);

    /// <summary>
    /// Creates a block for <paramref name="content"/> with the registered handlers, as
    /// <see cref="CreateInnerBlock(AIContent)"/> does, and returns the handler entry that maps the
    /// content's later updates, such as a call's result, into it.
    /// </summary>
    internal ContentBlock? CreateInnerBlock(
        AIContent content,
        bool claimUIActions,
        out IActiveEntry? entry)
    {
        ArgumentNullException.ThrowIfNull(content);

        entry = null;
        if (_inactiveHandlers is null)
        {
            return null;
        }

        var tempUpdate = new ChatResponseUpdate
        {
            Role = Update.Role,
            AuthorName = Update.AuthorName,
            CreatedAt = Update.CreatedAt,
            MessageId = Update.MessageId,
            Contents = [content],
        };
        var tempContext = new BlockMappingContext(tempUpdate, _inactiveHandlers, _calls, claimUIActions);

        for (var i = 0; i < _inactiveHandlers.Count; i++)
        {
            var activeEntry = _inactiveHandlers[i].TryHandle(tempContext);
            if (activeEntry is not null)
            {
                var block = activeEntry.Block;
                block.Role = tempUpdate.Role;
                block.AuthorName = tempUpdate.AuthorName;
                block.CreatedAt = tempUpdate.CreatedAt;
                block.LifecycleState = BlockLifecycleState.Inactive;

                // The wrapping block's call may get its result in a later round.
                if (content is FunctionCallContent call)
                    _calls?.Register(call, block, activeEntry);

                entry = activeEntry;
                return block;
            }
        }

        return null;
    }

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
                {
                    return true;
                }
            }
            return false;
        }
    }
}
