using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Controls.Chat;

/// <summary>
/// Implemented by a custom approval view to provide a custom
/// <see cref="ToolApprovalResponseContent"/> when the user approves or rejects a request.
/// </summary>
/// <remarks>
/// This enables optional edit-before-run experiences by allowing a view-model to return an
/// updated <see cref="FunctionCallContent"/> inside the approval response, while keeping the
/// underlying approval flow in the chat middleware.
/// </remarks>
public interface IToolApprovalResponseFactory
{
    /// <summary>
    /// Creates the approval response to submit for the supplied request.
    /// </summary>
    /// <param name="request">The approval request currently being reviewed.</param>
    /// <param name="approved">Whether the user approved the tool call.</param>
    /// <returns>The response to send back into the MEAI pipeline.</returns>
    ToolApprovalResponseContent CreateApprovalResponse(ToolApprovalRequestContent request, bool approved);
}
