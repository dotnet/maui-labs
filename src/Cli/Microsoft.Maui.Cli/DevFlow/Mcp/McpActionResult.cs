using ModelContextProtocol;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Mcp;

internal static class McpActionResult
{
    public static string RequireSuccess(
        ActionResult result,
        string successMessage,
        string failureMessage)
    {
        if (result.Success)
            return successMessage;

        var message = failureMessage;
        if (!string.IsNullOrWhiteSpace(result.Reason))
            message += $" Reason: {result.Reason}.";
        if (result.StatusCode.HasValue)
            message += $" HTTP status: {result.StatusCode.Value}.";

        if (result.Reason is "stale-capture-epoch" or "capture-window-mismatch")
            message += " Capture a fresh tree or hit-test and retry.";
        else if (result.Reason == "ui-mutation-busy")
            message += " Wait for the current UI mutation to finish, capture a fresh tree or hit-test, and retry.";
        else if (result.Retryable)
            message += " The operation is retryable.";

        if (result.TransportFailure)
            message += " The DevFlow agent could not be reached.";

        throw new McpException(message);
    }
}
