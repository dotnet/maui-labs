#if WINDOWS
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace EssentialsAISample.Services;

/// <summary>
/// Adds function calling to Phi Silica.
/// </summary>
/// <remarks>
/// <para>
/// Windows App SDK 2.3 exposes constrained JSON generation
/// (<c>LanguageModel.GenerateStructuredJsonResponseAsync</c>, surfaced by
/// <c>PhiSilicaChatClient</c> through <see cref="ChatOptions.ResponseFormat"/>) but there is
/// still no function-calling API on the WinRT surface.
/// </para>
/// <para>
/// This middleware bridges that gap: it describes the available tools in the system prompt and
/// constrains the reply to a tool-call JSON schema, then converts the result into
/// <see cref="FunctionCallContent"/> so the standard <c>UseFunctionInvocation</c> middleware can
/// execute it. Because the schema is enforced by the model runtime, no code-fence stripping or
/// free-form JSON scraping is needed.
/// </para>
/// <para>Usage: <c>new PhiSilicaToolCallingClient(new PhiSilicaChatClient())</c></para>
/// </remarks>
public sealed class PhiSilicaToolCallingClient : DelegatingChatClient
{
	private const string MoreStepsKey = "__more_steps";
	private const string CalledToolsKey = "__called_tools";

	public PhiSilicaToolCallingClient(IChatClient inner) : base(inner) { }

	public override async Task<ChatResponse> GetResponseAsync(
		IEnumerable<ChatMessage> messages,
		ChatOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		if (!HasTools(options))
			return await base.GetResponseAsync(messages, options, cancellationToken);

		var (rewritten, newOptions) = RewriteForTools(messages, options!);
		var response = await base.GetResponseAsync(rewritten, newOptions, cancellationToken);

		ConvertToolCallResponse(response);

		return response;
	}

	public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
		IEnumerable<ChatMessage> messages,
		ChatOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		// Without tools there is nothing to rewrite — structured output (if requested) is handled
		// natively by the underlying client.
		if (!HasTools(options))
			return base.GetStreamingResponseAsync(messages, options, cancellationToken);

		return StreamToolResponseAsync(messages, options!, cancellationToken);
	}

	private async IAsyncEnumerable<ChatResponseUpdate> StreamToolResponseAsync(
		IEnumerable<ChatMessage> messages,
		ChatOptions options,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		// A partial tool call is not actionable, so buffer the whole thing and emit it at once.
		var response = await GetResponseAsync(messages, options, cancellationToken);

		foreach (var message in response.Messages)
			yield return new ChatResponseUpdate { Role = message.Role, Contents = [.. message.Contents] };
	}

	private static bool HasTools(ChatOptions? options) =>
		options?.Tools is { Count: > 0 } tools && tools.OfType<AIFunction>().Any();

	// ═══════════════════════════════════════════════════════════
	// REQUEST REWRITING
	// ═══════════════════════════════════════════════════════════

	private static (IEnumerable<ChatMessage> Messages, ChatOptions Options) RewriteForTools(
		IEnumerable<ChatMessage> messages, ChatOptions options)
	{
		var tools = options.Tools!.OfType<AIFunction>().ToList();

		// Detect follow-up state: which tools were already called, and did the model ask for more?
		var (isFollowUp, calledToolNames) = InspectHistory(messages);

		// Narrow the tool list on follow-up so the enum steers the model to something new.
		var availableTools = isFollowUp && calledToolNames.Count > 0
			? tools.Where(t => !calledToolNames.Contains(t.Name)).ToList()
			: tools;
		if (availableTools.Count == 0)
			availableTools = tools;

		var userSchema = (options.ResponseFormat as ChatResponseFormatJson)?.Schema;
		var schema = BuildToolCallSchema(availableTools, userSchema, isFollowUp);

		var systemPrompt = new ChatMessage(
			ChatRole.System,
			BuildSystemPrompt(availableTools, userSchema.HasValue, isFollowUp));

		var allMessages = new List<ChatMessage> { systemPrompt };
		allMessages.AddRange(messages);

		// Hand the tool-call schema to the model runtime. PhiSilicaChatClient maps this onto
		// GenerateStructuredJsonResponseAsync, so the reply is guaranteed to match the schema.
		var newOptions = options.Clone();
		newOptions.Tools = null;
		newOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema(
			schema,
			schemaName: "tool_call",
			schemaDescription: "A tool call or a direct answer.");

		return (allMessages, newOptions);
	}

	private static (bool IsFollowUp, HashSet<string> CalledToolNames) InspectHistory(IEnumerable<ChatMessage> messages)
	{
		var isFollowUp = false;
		var calledToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var message in messages)
		{
			foreach (var content in message.Contents)
			{
				if (content is not FunctionCallContent call)
					continue;

				if (!string.IsNullOrEmpty(call.Name))
					calledToolNames.Add(call.Name);

				if (call.AdditionalProperties?.TryGetValue(MoreStepsKey, out var moreSteps) == true && moreSteps is true)
					isFollowUp = true;

				if (call.AdditionalProperties?.TryGetValue(CalledToolsKey, out var called) == true && called is string names)
				{
					foreach (var name in names.Split(',', StringSplitOptions.RemoveEmptyEntries))
						calledToolNames.Add(name.Trim());
				}
			}
		}

		return (isFollowUp, calledToolNames);
	}

	private static string BuildSystemPrompt(List<AIFunction> tools, bool hasUserSchema, bool isFollowUp)
	{
		var prompt = new StringBuilder();

		if (!isFollowUp)
			prompt.AppendLine("You are a helpful assistant with access to tools.").AppendLine();

		prompt.AppendLine("Available tools:");
		foreach (var tool in tools)
		{
			prompt.AppendLine($"- {tool.Name}: {tool.Description}");
			prompt.AppendLine($"  Parameters: {tool.JsonSchema}");

			foreach (var hint in DescribeEnumParameters(tool))
				prompt.AppendLine($"  IMPORTANT: {hint}");
		}

		prompt.AppendLine();

		if (isFollowUp)
		{
			prompt.AppendLine("Call the next tool using data from the tool result above.");
		}
		else
		{
			prompt.AppendLine("If the user's question requires a tool, set type to \"tool_call\", set tool_name, and provide arguments.");
			prompt.AppendLine(hasUserSchema
				? "If you can answer directly, set type to \"response\" and fill in the response object."
				: "If you can answer directly without a tool, set type to \"text\" and put your answer in the text field.");
			prompt.AppendLine("Call only ONE tool at a time. After receiving the result, you may call another.");
			prompt.AppendLine("If you will need to call another tool AFTER this one, set more_steps to true.");
		}

		prompt.AppendLine("For enum parameters, use EXACTLY one of the allowed values listed above.");
		prompt.AppendLine("If a tool has no required parameters, use an empty arguments object {}.");

		return prompt.ToString();
	}

	private static IEnumerable<string> DescribeEnumParameters(AIFunction tool)
	{
		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(tool.JsonSchema.GetRawText());
		}
		catch (JsonException)
		{
			yield break;
		}

		using (document)
		{
			if (!document.RootElement.TryGetProperty("properties", out var properties))
				yield break;

			foreach (var property in properties.EnumerateObject())
			{
				if (!property.Value.TryGetProperty("enum", out var values))
					continue;

				var allowed = string.Join(", ", values.EnumerateArray().Select(v => v.GetString()));
				yield return $"{property.Name} must be EXACTLY one of: {allowed}";
			}
		}
	}

	private static JsonElement BuildToolCallSchema(List<AIFunction> tools, JsonElement? userSchema, bool isFollowUp)
	{
		var toolNames = string.Join(",", tools.Select(t => JsonSerializer.Serialize(t.Name)));

		string json;
		if (isFollowUp)
		{
			// Follow-up: the model has a tool result in hand, so a tool call is the only valid move.
			json = "{\"type\":\"object\",\"properties\":{"
				+ "\"type\":{\"type\":\"string\",\"enum\":[\"tool_call\"]},"
				+ "\"tool_name\":{\"type\":\"string\",\"enum\":[" + toolNames + "]},"
				+ "\"arguments\":{\"type\":\"object\"}"
				+ "},\"required\":[\"type\",\"tool_name\"]}";
		}
		else
		{
			// Only offer more_steps when chaining is actually possible.
			var moreSteps = tools.Count >= 2
				? ",\"more_steps\":{\"type\":\"boolean\",\"description\":\"Set true if you need to call another tool after this one\"}"
				: "";

			var (answerField, typeEnum) = userSchema is { } schema
				? (",\"response\":" + schema.GetRawText(), "[\"tool_call\",\"response\"]")
				: (",\"text\":{\"type\":\"string\",\"description\":\"Your text response\"}", "[\"tool_call\",\"text\"]");

			json = "{\"type\":\"object\",\"properties\":{"
				+ "\"type\":{\"type\":\"string\",\"enum\":" + typeEnum + "},"
				+ "\"tool_name\":{\"type\":\"string\",\"enum\":[" + toolNames + "]},"
				+ "\"arguments\":{\"type\":\"object\"}"
				+ answerField
				+ moreSteps
				+ "},\"required\":[\"type\"]}";
		}

		using var document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}

	// ═══════════════════════════════════════════════════════════
	// RESPONSE PARSING
	// ═══════════════════════════════════════════════════════════

	private static void ConvertToolCallResponse(ChatResponse response)
	{
		foreach (var message in response.Messages)
		{
			var text = string.Concat(message.Contents.OfType<TextContent>().Select(c => c.Text));
			if (string.IsNullOrWhiteSpace(text))
				continue;

			switch (Parse(text))
			{
				case ToolCall toolCall:
					message.Contents.Clear();
					message.Contents.Add(CreateFunctionCall(toolCall));
					break;

				case TextAnswer answer:
					message.Contents.Clear();
					message.Contents.Add(new TextContent(answer.Text));
					break;
			}
		}
	}

	private static FunctionCallContent CreateFunctionCall(ToolCall toolCall)
	{
#pragma warning disable IL3050, IL2026 // Sample code; the argument shape is not known at compile time.
		var arguments = toolCall.Arguments is { ValueKind: JsonValueKind.Object } element
			? JsonSerializer.Deserialize<Dictionary<string, object?>>(element.GetRawText())
			: null;

		var call = new FunctionCallContent(Guid.NewGuid().ToString("N")[..16], toolCall.Name, arguments);
#pragma warning restore IL3050, IL2026

		call.AdditionalProperties ??= [];

		if (toolCall.MoreSteps)
			call.AdditionalProperties[MoreStepsKey] = true;

		// Track which tools ran so the follow-up round can narrow the tool_name enum.
		call.AdditionalProperties[CalledToolsKey] = toolCall.Name;

		return call;
	}

	private static object? Parse(string text)
	{
		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(text);
		}
		catch (JsonException)
		{
			return null;
		}

		using (document)
		{
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
				return null;

			var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;

			switch (type)
			{
				case "tool_call":
					var name = root.TryGetProperty("tool_name", out var nameElement) ? nameElement.GetString() : null;
					if (string.IsNullOrEmpty(name))
						return null;

					var arguments = root.TryGetProperty("arguments", out var argumentsElement)
						? argumentsElement.Clone()
						: (JsonElement?)null;
					var moreSteps = root.TryGetProperty("more_steps", out var moreStepsElement)
						&& moreStepsElement.ValueKind == JsonValueKind.True;

					return new ToolCall(name!, arguments, moreSteps);

				case "text":
					return new TextAnswer(root.TryGetProperty("text", out var textElement) ? textElement.GetString() ?? "" : "");

				case "response" when root.TryGetProperty("response", out var responseElement):
					return new TextAnswer(responseElement.GetRawText());

				default:
					return null;
			}
		}
	}

	private sealed record ToolCall(string Name, JsonElement? Arguments, bool MoreSteps);

	private sealed record TextAnswer(string Text);
}
#endif
