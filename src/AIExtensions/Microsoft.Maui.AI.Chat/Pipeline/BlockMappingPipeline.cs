// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Runs the registered <see cref="ContentBlockHandler{TState}"/>s over each streamed
/// <c>ChatResponseUpdate</c>, letting handlers claim content and emit, update, or complete
/// <see cref="ContentBlock"/>s.
/// </summary>
/// <remarks>
/// Built by <see cref="UIAgent"/> for each message. Active (streaming) blocks get first chance at new
/// content; unclaimed content then falls through to inactive handlers, ending at the text fallback.
/// </remarks>
internal class BlockMappingPipeline
{
    private readonly List<IHandlerEntry> _handlers = new();
    private readonly List<IActiveEntry> _activeStack = new();
    private readonly ILogger _logger;
    private readonly FunctionCallRegistry _calls;

    internal BlockMappingPipeline(
        UIAgentOptions options,
        ILogger? logger = null,
        FunctionCallRegistry? calls = null)
    {
        _logger = logger ?? NullLogger.Instance;
        _calls = calls ?? new FunctionCallRegistry();

        // Registered UI actions claim their function calls first, so the action always runs. A handler
        // registered for an action's name shows the call inside the action instead.
        if (options.UIActions.Count > 0)
        {
            _handlers.Add(new HandlerEntry<UIActionHandler.UIActionHandlerState>(
                new UIActionHandler(options.UIActions, options.Services, _logger)));
        }

        // User-registered handlers go next so they can customize behavior
        foreach (var registration in options.HandlerRegistrations)
        {
            _handlers.Add(registration.CreateEntry());
        }

        // Built-in approval handler (before function invocation so it claims ToolApprovalRequestContent first)
        _handlers.Add(new HandlerEntry<ToolApprovalHandler.ApprovalHandlerState>(new ToolApprovalHandler()));

        // Built-in function invocation handler
        _handlers.Add(new HandlerEntry<FunctionInvocationContentBlock>(new FunctionInvocationHandler()));

        // Built-in reasoning handler (before text so reasoning is rendered separately)
        _handlers.Add(new HandlerEntry<ReasoningContentBlock>(new ReasoningHandler()));

        // Built-in media handler (before text so DataContent and UriContent are claimed before fallback)
        _handlers.Add(new HandlerEntry<MediaContentHandler.MediaHandlerState>(new MediaContentHandler()));

        // Provider-supplied rich snapshots take precedence over the plain text fallback.
        _handlers.Add(new HandlerEntry<RichContentBlock>(new RichTextContentHandler()));

        // Built-in text handler is always last (fallback)
        _handlers.Add(new HandlerEntry<TextContentBlock>(new TextBlockHandler()));
    }

    internal async IAsyncEnumerable<ContentBlock> Process(
        ChatResponseUpdate update,
#pragma warning disable IDE0060 // cancellationToken reserved for future use
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
#pragma warning restore IDE0060
    {
        var contentTypes = string.Join(", ", update.Contents.Select(c => c.GetType().Name));
        BlockMappingPipelineLog.ProcessingUpdate(_logger, update.Role?.Value, update.Contents.Count, contentTypes);

        var context = new BlockMappingContext(update, _handlers, _calls);

        // Phase 1: Active entries get priority (most recent first)
        for (var i = _activeStack.Count - 1; i >= 0; i--)
        {
            if (context.AllHandled)
            {
                break;
            }

            var active = _activeStack[i];
            var result = active.Invoke(context);

            BlockMappingPipelineLog.ActiveHandlerResult(_logger, active.Block.GetType().Name, result.Kind.ToString(), active.Block.Id);

            switch (result.Kind)
            {
                case HandleResult.ResultKind.Pass:
                    break;

                case HandleResult.ResultKind.Update:
                    active.Block.InvokeNotifyChanged();
                    break;

                case HandleResult.ResultKind.Complete:
                    active.Block.LifecycleState = BlockLifecycleState.Inactive;
                    active.Block.InvokeNotifyChanged();
                    _activeStack.RemoveAt(i);
                    break;
            }
        }

        // Calls the turn already shows absorb their replays and results instead of emitting duplicates.
        if (!context.AllHandled)
        {
            ApplyKnownCalls(context);
        }

        // Phase 2: Inactive handlers try to claim remaining content
        if (!context.AllHandled)
        {
            List<(int Position, ContentBlock Block)>? emitted = null;
            for (var i = 0; i < _handlers.Count; i++)
            {
                if (context.AllHandled)
                {
                    break;
                }

                var handler = _handlers[i];
                BlockMappingPipelineLog.TryingInactiveHandler(_logger, handler.GetType().Name);

                while (!context.AllHandled)
                {
                    var progressBefore = context.HandledProgress;
                    context.BeginClaim();
                    var activeEntry = handler.TryHandle(context);
                    if (activeEntry is not null)
                    {
                        var emitBlock = activeEntry.Block;
                        emitBlock.Role = update.Role;
                        emitBlock.AuthorName = update.AuthorName;
                        emitBlock.CreatedAt = update.CreatedAt;
                        var alreadyComplete = IsAlreadyComplete(emitBlock);
                        emitBlock.LifecycleState = alreadyComplete
                            ? BlockLifecycleState.Inactive
                            : BlockLifecycleState.Active;
                        ThrowIfIdMissing(emitBlock);
                        if (!alreadyComplete)
                            _activeStack.Add(activeEntry);
                        RegisterCall(emitBlock, activeEntry);
                        (emitted ??= new()).Add((context.FirstClaimed, emitBlock));

                        // If the handler emitted without consuming any content, stop
                        // re-invoking it to prevent infinite loops.
                        if (context.HandledProgress == progressBefore)
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }

            if (emitted is not null)
            {
                // Handlers run in registration order, but the blocks follow the order of the contents they
                // show, so a message with reasoning, text and a call reads in that order. A block that
                // claimed no content goes last. OrderBy is a stable sort.
                foreach (var (_, emitBlock) in emitted.OrderBy(static emission => emission.Position))
                {
                    BlockMappingPipelineLog.EmittingBlock(_logger, emitBlock.GetType().Name, emitBlock.Id, emitBlock.Role?.Value);
                    yield return emitBlock;
                }
            }
        }
        else
        {
            BlockMappingPipelineLog.AllContentHandledAfterPhase1(_logger);
        }

        await Task.CompletedTask;
    }

    internal IReadOnlyList<ContentBlock> Finalize()
    {
        BlockMappingPipelineLog.Finalizing(_logger, _activeStack.Count);

        foreach (var active in _activeStack)
        {
            active.Block.LifecycleState = BlockLifecycleState.Inactive;
            active.Block.InvokeNotifyChanged();
        }
        _activeStack.Clear();

        return Array.Empty<ContentBlock>();
    }

    private void ApplyKnownCalls(BlockMappingContext context)
    {
        HashSet<string>? newCallIds = null;
        foreach (var content in context.UnhandledContents)
        {
            if (content is not FunctionCallContent call)
                continue;

            if (_calls.TryGet(call.CallId, out var known)
                && (!known.HasResult || ReferenceEquals(known.Call, call)))
            {
                // FunctionInvokingChatClient replays approved and rejected calls before their results.
                context.MarkHandled(call);
            }
            else if (!string.IsNullOrEmpty(call.CallId))
            {
                // A provider may reuse the id of a finished call; that call gets its own block.
                (newCallIds ??= new(StringComparer.Ordinal)).Add(call.CallId);
            }
        }

        foreach (var content in context.UnhandledContents)
        {
            if (content is FunctionResultContent result
                && newCallIds?.Contains(result.CallId) != true
                && _calls.TryGet(result.CallId, out var known))
            {
                ApplyKnownResult(context, result, known);
            }
        }
    }

    private void ApplyKnownResult(
        BlockMappingContext context,
        FunctionResultContent result,
        FunctionCallRegistry.Registration known)
    {
        if (known.HasResult)
        {
            // The result is already shown, for example because it is now sent back as a request.
            context.MarkHandled(result);
            return;
        }

        if (known.ResultEntry is { } entry)
        {
            if (_activeStack.Contains(entry))
                return;

            var handled = entry.Invoke(context);
            BlockMappingPipelineLog.ActiveHandlerResult(
                _logger,
                entry.Block.GetType().Name,
                handled.Kind.ToString(),
                entry.Block.Id);
            if (handled.Kind == HandleResult.ResultKind.Pass)
                return;

            if (handled.Kind == HandleResult.ResultKind.Complete)
                entry.Block.LifecycleState = BlockLifecycleState.Inactive;
            entry.Block.InvokeNotifyChanged();
            if (!ReferenceEquals(entry.Block, known.Block))
                known.Block.InvokeNotifyChanged();
            return;
        }

        switch (known.Block)
        {
            case UIActionBlock uiAction:
                uiAction.SetResult(result);
                break;
            case InteractiveFunctionBlock interactive:
                interactive.InnerBlock.Result = result;
                interactive.InnerBlock.InvokeNotifyChanged();
                interactive.InvokeNotifyChanged();
                break;
            case FunctionInvocationContentBlock functionBlock:
                functionBlock.Result = result;
                functionBlock.InvokeNotifyChanged();
                break;
            default:
                return;
        }

        context.MarkHandled(result);
    }

    private void RegisterCall(ContentBlock block, IActiveEntry entry)
    {
        switch (block)
        {
            case UIActionBlock { Call: { } call }:
                _calls.Register(call, block, entry);
                break;
            case InteractiveFunctionBlock { Call: { } call } interactive:
                // A wrapped call's result is claimed by the handler that created the inner block, or for
                // an approval of a UI action by the action, which maps it into the inner block.
                var resultEntry = _calls.TryGet(call.CallId, out var inner)
                    && (ReferenceEquals(inner.Block, interactive.InnerBlock)
                        || (interactive is ToolApprovalBlock { UIAction: { } action }
                            && ReferenceEquals(inner.Block, action)))
                        ? inner.ResultEntry
                        : null;
                _calls.Register(call, block, resultEntry);
                break;
            case FunctionInvocationContentBlock { Call: { } call }:
                _calls.Register(call, block, entry);
                break;
        }
    }

    private static void ThrowIfIdMissing(ContentBlock block)
    {
        if (string.IsNullOrEmpty(block.Id))
        {
            throw new InvalidOperationException(
                $"Block handler emitted a {block.GetType().Name} without assigning an Id.");
        }
    }

    private static bool IsAlreadyComplete(ContentBlock block) =>
        block is FunctionInvocationContentBlock { HasResult: true }
            or InteractiveFunctionBlock { HasResult: true };
}
