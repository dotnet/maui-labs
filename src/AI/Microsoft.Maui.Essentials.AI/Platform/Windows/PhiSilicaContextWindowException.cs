namespace Microsoft.Maui.Essentials.AI;

/// <summary>
/// Thrown when a prompt does not fit in the model's context window.
/// </summary>
/// <remarks>
/// The context window is shared by the system prompt, the accumulated conversation history and the
/// new prompt, and the Windows AI APIs do not truncate automatically. Recover by trimming the
/// prompt, summarizing the history, or starting a new conversation.
/// <see cref="PhiSilicaChatClient.GetPromptFitAsync"/> reports this before a request is sent.
/// </remarks>
public sealed class PhiSilicaContextWindowException : InvalidOperationException
{
	public PhiSilicaContextWindowException()
		: this("The prompt is larger than the model's context window.")
	{
	}

	public PhiSilicaContextWindowException(string message)
		: base(message)
	{
	}

	public PhiSilicaContextWindowException(string message, Exception? innerException)
		: base(message, innerException)
	{
	}
}
