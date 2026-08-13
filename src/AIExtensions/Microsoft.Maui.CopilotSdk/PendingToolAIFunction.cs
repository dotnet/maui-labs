using GitHub.Copilot;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.CopilotSdk;

/// <summary>
/// An SDK-invocable proxy that preserves a caller's declaration metadata but waits for the outer
/// Microsoft.Extensions.AI tool loop to supply the actual result.
/// </summary>
internal sealed class PendingToolAIFunction(
    AIFunctionDeclaration declaration,
    PendingToolCoordinator coordinator) : AIFunction
{
    public override string Name => declaration.Name;

    public override string Description => declaration.Description;

    public override System.Text.Json.JsonElement JsonSchema =>
        declaration.JsonSchema;

    public override System.Text.Json.JsonElement? ReturnJsonSchema =>
        declaration.ReturnJsonSchema;

    public override IReadOnlyDictionary<string, object?> AdditionalProperties =>
        declaration.AdditionalProperties;

    protected override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        if (arguments.Context is null
            || !arguments.Context.TryGetValue(
            typeof(ToolInvocation),
            out var value)
            || value is not ToolInvocation invocation
            || string.IsNullOrEmpty(invocation.ToolCallId))
        {
            throw new InvalidOperationException(
                $"Copilot did not provide invocation context for tool '{Name}'.");
        }

        return coordinator.WaitForResultAsync(
            invocation.ToolCallId,
            cancellationToken);
    }
}

/// <summary>Correlates SDK proxy invocations with results from the outer M.E.AI loop.</summary>
internal sealed class PendingToolCoordinator
{
    private readonly Dictionary<string, TaskCompletionSource<object?>> _pending =
        new(StringComparer.Ordinal);

    internal ValueTask<object?> WaitForResultAsync(
        string toolCallId,
        CancellationToken cancellationToken)
    {
        if (_pending.ContainsKey(toolCallId))
        {
            throw new InvalidOperationException(
                $"Tool CallId '{toolCallId}' is already awaiting a result.");
        }

        var completion = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pending.Add(toolCallId, completion);
        CancellationTokenRegistration registration = default;
        if (cancellationToken.CanBeCanceled)
        {
            registration = cancellationToken.Register(
                static state =>
                {
                    var (source, token) =
                        ((TaskCompletionSource<object?>, CancellationToken))state!;
                    source.TrySetCanceled(token);
                },
                (completion, cancellationToken));
        }

        return new ValueTask<object?>(
            AwaitResultAsync(completion.Task, registration));
    }

    internal void SupplyResult(FunctionResultContent result)
    {
        if (string.IsNullOrEmpty(result.CallId)
            || !_pending.Remove(result.CallId, out var completion))
        {
            throw new InvalidOperationException(
                $"There is no pending Copilot tool call for CallId '{result.CallId}'.");
        }

        if (result.Exception is not null)
            completion.TrySetException(result.Exception);
        else
            completion.TrySetResult(result.Result);
    }

    internal bool IsPending(string? toolCallId) =>
        !string.IsNullOrEmpty(toolCallId)
        && _pending.ContainsKey(toolCallId);

    private static async Task<object?> AwaitResultAsync(
        Task<object?> resultTask,
        CancellationTokenRegistration registration)
    {
        try
        {
            return await resultTask.ConfigureAwait(false);
        }
        finally
        {
            registration.Dispose();
        }
    }
}
