using Microsoft.Extensions.AI;

namespace Microsoft.Maui.Essentials.AI;

internal static class GeminiNanoPromptFormatter
{
	internal static void ValidateMessages(IReadOnlyList<ChatMessage> messages)
	{
		if (messages.Count == 0)
			throw new ArgumentException("At least one chat message is required.", nameof(messages));

		foreach (var message in messages)
		{
			ArgumentNullException.ThrowIfNull(message);
			FormatRole(message.Role);
			foreach (var content in message.Contents)
			{
				ArgumentNullException.ThrowIfNull(content);
				if (message.Role == ChatRole.System && content is not TextContent)
					throw new NotSupportedException($"System content type '{content.GetType().Name}' is not supported by Gemini Nano.");

				switch (content)
				{
					case TextContent:
					case TextReasoningContent when message.Role == ChatRole.Assistant:
						break;
					case DataContent data when data.HasTopLevelMediaType("image") &&
						data.Data is { Length: > 0 }:
						break;
					case DataContent data when data.HasTopLevelMediaType("image"):
						throw new NotSupportedException("Gemini Nano image content must contain non-empty in-memory data.");
					default:
						throw new NotSupportedException($"Content type '{content.GetType().Name}' is not supported by Gemini Nano.");
				}
			}
		}

		if (!GetConversationMessages(messages).Any())
			throw new ArgumentException("At least one user or assistant message with text or image content is required.", nameof(messages));
	}

	internal static IEnumerable<ChatMessage> GetConversationMessages(IReadOnlyList<ChatMessage> messages) =>
		messages.Where(message => message.Role != ChatRole.System && message.Contents.Any(content =>
			content is TextContent { Text.Length: > 0 } or DataContent));

	internal static void ValidateOptions(ChatOptions? options)
	{
		if (options is null)
			return;

		if (options.Temperature is { } temperature && (!float.IsFinite(temperature) || temperature is < 0 or > 1))
			throw new ArgumentOutOfRangeException(nameof(options), options.Temperature, "Temperature must be finite and between 0 and 1.");
		if (options.TopK is <= 0)
			throw new ArgumentOutOfRangeException(nameof(options), options.TopK, "TopK must be greater than zero.");
		if (options.Seed is < 0 or > int.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(options), options.Seed, $"Seed must be between 0 and {int.MaxValue}.");
		if (options.MaxOutputTokens is < 1 or > 4096)
			throw new ArgumentOutOfRangeException(nameof(options), options.MaxOutputTokens, "MaxOutputTokens must be between 1 and 4096.");

		ThrowIfSpecified(options.TopP, nameof(options.TopP));
		ThrowIfSpecified(options.FrequencyPenalty, nameof(options.FrequencyPenalty));
		ThrowIfSpecified(options.PresencePenalty, nameof(options.PresencePenalty));
		ThrowIfSpecified(options.ConversationId, nameof(options.ConversationId));
		ThrowIfSpecified(options.ContinuationToken, nameof(options.ContinuationToken));
		ThrowIfSpecified(options.ModelId, nameof(options.ModelId));
		ThrowIfSpecified(options.RawRepresentationFactory, nameof(options.RawRepresentationFactory));

		// Like the Apple client, None disables even supplied tool definitions.
		if (options.ToolMode != ChatToolMode.None)
		{
			ThrowIfSpecified(options.ToolMode, nameof(options.ToolMode));
			ThrowIfSpecified(options.AllowMultipleToolCalls, nameof(options.AllowMultipleToolCalls));
			if (options.Tools is { Count: > 0 })
				throw new NotSupportedException("The ML Kit GenAI Prompt beta4 API does not expose tool calling.");
		}

		if (options.AllowBackgroundResponses == true)
			throw new NotSupportedException("Gemini Nano does not support background responses.");
		if (options.StopSequences is { Count: > 0 })
			throw new NotSupportedException("Gemini Nano does not support stop sequences.");
		if (options.AdditionalProperties is { Count: > 0 })
			throw new NotSupportedException("Gemini Nano does not support unrecognized additional chat options.");
		if (options.Reasoning?.Effort is { } effort && !Enum.IsDefined(effort))
			throw new ArgumentOutOfRangeException(nameof(options), effort, "Reasoning effort must be a defined value.");
		if (options.Reasoning?.Output is { } output && !Enum.IsDefined(output))
			throw new ArgumentOutOfRangeException(nameof(options), output, "Reasoning output must be a defined value.");
		if (options.Reasoning?.Output == ReasoningOutput.Summary)
			throw new NotSupportedException("Gemini Nano exposes full thinking output but not reasoning summaries.");
	}

	private static void ThrowIfSpecified<T>(T? value, string optionName)
	{
		if (value is not null)
			throw new NotSupportedException($"Gemini Nano does not support ChatOptions.{optionName}.");
	}

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
