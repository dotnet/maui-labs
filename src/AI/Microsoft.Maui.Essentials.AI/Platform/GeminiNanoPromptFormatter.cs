using Microsoft.Extensions.AI;

namespace Microsoft.Maui.Essentials.AI;

internal static class GeminiNanoPromptFormatter
{
	internal static string? GetSystemInstruction(
		IReadOnlyList<ChatMessage> messages,
		ChatOptions? options,
		string? responseInstruction = null)
	{
		var instructions = new List<string>();

		if (!string.IsNullOrWhiteSpace(options?.Instructions))
			instructions.Add(options.Instructions);

		foreach (var message in messages.Where(m => m.Role == ChatRole.System))
		{
			foreach (var content in message.Contents)
			{
				if (content is not TextContent text)
					throw new NotSupportedException($"System content type '{content.GetType().Name}' is not supported by Gemini Nano.");

				if (!string.IsNullOrWhiteSpace(text.Text))
					instructions.Add(text.Text);
			}
		}

		if (!string.IsNullOrWhiteSpace(responseInstruction))
			instructions.Add(responseInstruction);

		return instructions.Count == 0 ? null : string.Join("\n\n", instructions);
	}

	internal static string FormatRole(ChatRole role)
	{
		if (role == ChatRole.User)
			return "user";
		if (role == ChatRole.Assistant)
			return "assistant";
		if (role == ChatRole.System)
			return "system";

		throw new NotSupportedException($"Chat role '{role}' is not supported by Gemini Nano.");
	}

	internal static string RoleStart(string role) => $"<|{role}|>\n";

	internal const string RoleEnd = "\n<|end|>\n";
}
