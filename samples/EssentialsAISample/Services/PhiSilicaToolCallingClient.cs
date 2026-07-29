#if WINDOWS
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace EssentialsAISample.Services;

/// <summary>
/// Adds function calling to Phi Silica.
/// </summary>
/// <remarks>
/// <para>
/// Windows App SDK exposes schema-constrained JSON generation but no function-calling API, so tool
/// calling has to be built on top of constrained decoding. This middleware does that in two phases,
/// then emits <see cref="FunctionCallContent"/> so the standard <c>UseFunctionInvocation</c>
/// middleware can execute the call.
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Selection.</b> One constrained call against a schema whose only property is a
/// <c>tool_name</c> enum listing the tools plus <c>none</c>.
/// </description></item>
/// <item><description>
/// <b>Arguments.</b> If a tool was chosen, a second constrained call against that tool's own
/// parameter schema. If <c>none</c> was chosen, the request is passed through so the model answers
/// normally.
/// </description></item>
/// </list>
/// <para>
/// The split matters. Probing the on-device model showed that a single combined schema — one object
/// carrying a <c>tool_call</c>/<c>text</c> discriminator, the tool name, the arguments and the
/// answer text — makes the model unreliable: it skips prerequisite calls, invents placeholder
/// argument values such as <c>"USER_ID"</c>, and abandons multi-step chains by asking the user for
/// data it should have fetched. Asking one small question at a time is both more accurate and
/// faster, and giving the argument phase the tool's real schema means required parameters are
/// actually filled in.
/// </para>
/// <para>
/// Every schema sent to the model sets <c>additionalProperties: false</c>. Without it the model
/// invents property names — it produced <c>body</c>, <c>response</c> and <c>message</c> in place of
/// a declared <c>text</c> property — and constrained decoding permits them, which silently yields
/// empty results.
/// </para>
/// <para>Usage: <c>new PhiSilicaToolCallingClient(new PhiSilicaChatClient())</c></para>
/// </remarks>
public sealed class PhiSilicaToolCallingClient : DelegatingChatClient
{
	/// <summary>Sentinel choice meaning the model wants to answer without a tool.</summary>
	private const string NoToolName = "none";

	/// <summary>
	/// Upper bound on tool calls in a single chain, counted from the conversation history.
	/// </summary>
	/// <remarks>
	/// The model decides for itself when to stop by choosing <see cref="NoToolName"/>, but a small
	/// model does not always take that exit. Since the function invocation middleware feeds each
	/// result back and asks again, an indecisive model would otherwise keep selecting tools until
	/// that middleware hits its own iteration cap, which on device means minutes of stalling. This
	/// bounds the work instead.
	/// </remarks>
	private const int MaxToolCallsPerChain = 5;

	public PhiSilicaToolCallingClient(IChatClient inner) : base(inner) { }

	public override async Task<ChatResponse> GetResponseAsync(
		IEnumerable<ChatMessage> messages,
		ChatOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		var tools = GetFunctions(options);
		if (tools is null)
			return await base.GetResponseAsync(messages, options, cancellationToken);

		var conversation = messages as IReadOnlyList<ChatMessage> ?? [.. messages];

		var selected = await SelectToolAsync(conversation, tools, options, cancellationToken);
		if (selected is null)
			return await AnswerAsync(conversation, options, cancellationToken);

		var call = await BuildToolCallAsync(conversation, selected, options, cancellationToken);

		// Repeating a call that has already been answered would loop forever, so treat an exact
		// repeat as the model having nothing left to ask for.
		if (GetCompletedCalls(conversation).Contains(Signature(call.Name, call.Arguments)))
			return await AnswerAsync(conversation, options, cancellationToken);

		return new ChatResponse(new ChatMessage(ChatRole.Assistant, [call]));
	}

	public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
		IEnumerable<ChatMessage> messages,
		ChatOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		// Without tools there is nothing to rewrite, so stream straight through.
		if (GetFunctions(options) is null)
			return base.GetStreamingResponseAsync(messages, options, cancellationToken);

		return StreamToolResponseAsync(messages, options, cancellationToken);
	}

	private async IAsyncEnumerable<ChatResponseUpdate> StreamToolResponseAsync(
		IEnumerable<ChatMessage> messages,
		ChatOptions? options,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		// A partial tool call is not actionable, so the response is resolved before streaming.
		var response = await GetResponseAsync(messages, options, cancellationToken);

		foreach (var message in response.Messages)
			yield return new ChatResponseUpdate { Role = message.Role, Contents = [.. message.Contents] };
	}

	private static List<AIFunction>? GetFunctions(ChatOptions? options)
	{
		if (options?.Tools is not { Count: > 0 })
			return null;

		var functions = options.Tools.OfType<AIFunction>().ToList();

		return functions.Count > 0 ? functions : null;
	}

	// ═══════════════════════════════════════════════════════════
	// PHASE 1: SELECTION
	// ═══════════════════════════════════════════════════════════

	/// <summary>
	/// Asks the model which tool to call, if any.
	/// </summary>
	/// <returns>The chosen tool, or <see langword="null"/> to answer without a tool.</returns>
	private async Task<AIFunction?> SelectToolAsync(
		IReadOnlyList<ChatMessage> messages,
		List<AIFunction> tools,
		ChatOptions? options,
		CancellationToken cancellationToken)
	{
		var completed = GetCompletedCalls(messages);

		// Stop once the chain has done enough work, so an indecisive model cannot spin.
		if (completed.Count >= MaxToolCallsPerChain)
			return null;

		// Offering the model a tool it has already used is fine — the same tool with different
		// arguments is legitimate, for example fetching the weather for a second city. Exact
		// repeats are caught once the arguments are known.
		var candidates = tools;

		var instructions = new StringBuilder();
		instructions.AppendLine("Decide which tool is needed to answer the user's request.");
		instructions.AppendLine();

		foreach (var tool in candidates)
			instructions.AppendLine($"- {tool.Name}: {tool.Description}");

		instructions.AppendLine();
		instructions.AppendLine(
			$"Choose {NoToolName} if no tool is needed, or if the tool results above already answer the request.");

		var names = candidates.Select(t => t.Name).Append(NoToolName);
		var schema = BuildSelectionSchema(names);

		var response = await RequestAsync(
			messages, instructions.ToString(), schema, "tool_selection", options, cancellationToken);

		var chosen = ReadString(response, "tool_name");
		if (string.IsNullOrEmpty(chosen) || chosen.Equals(NoToolName, StringComparison.OrdinalIgnoreCase))
			return null;

		return candidates.FirstOrDefault(t => t.Name.Equals(chosen, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Signatures of the tool calls already present in the conversation, used to detect repeats.
	/// </summary>
	private static HashSet<string> GetCompletedCalls(IReadOnlyList<ChatMessage> messages)
	{
		var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var message in messages)
		{
			foreach (var content in message.Contents)
			{
				if (content is FunctionCallContent call && !string.IsNullOrEmpty(call.Name))
					completed.Add(Signature(call.Name, call.Arguments));
			}
		}

		return completed;
	}

	private static string Signature(string name, IDictionary<string, object?>? arguments)
	{
		if (arguments is not { Count: > 0 })
			return name;

		var parts = arguments
			.OrderBy(a => a.Key, StringComparer.Ordinal)
			.Select(a => $"{a.Key}={a.Value}");

		return $"{name}({string.Join(",", parts)})";
	}

	private static JsonElement BuildSelectionSchema(IEnumerable<string> toolNames)
	{
		var values = new JsonArray();
		foreach (var name in toolNames)
			values.Add(JsonValue.Create(name));

		var schema = new JsonObject
		{
			["type"] = "object",
			["additionalProperties"] = false,
			["properties"] = new JsonObject
			{
				["tool_name"] = new JsonObject
				{
					["type"] = "string",
					["enum"] = values
				}
			},
			["required"] = new JsonArray { "tool_name" }
		};

		return ToElement(schema);
	}

	// ═══════════════════════════════════════════════════════════
	// PHASE 2: ARGUMENTS
	// ═══════════════════════════════════════════════════════════

	/// <summary>
	/// Fills in the chosen tool's parameters using the tool's own schema.
	/// </summary>
	private async Task<FunctionCallContent> BuildToolCallAsync(
		IReadOnlyList<ChatMessage> messages,
		AIFunction tool,
		ChatOptions? options,
		CancellationToken cancellationToken)
	{
		var callId = Guid.NewGuid().ToString("N")[..16];

		var schema = CloseSchema(tool.JsonSchema);
		if (!HasProperties(schema))
			return new FunctionCallContent(callId, tool.Name, new Dictionary<string, object?>());

		var instructions =
			$"Work out the arguments for the {tool.Name} tool ({tool.Description}). " +
			"Use the request and any tool results above. Fill in every required argument.";

		var response = await RequestAsync(messages, instructions, schema, tool.Name, options, cancellationToken);

		return new FunctionCallContent(callId, tool.Name, ReadArguments(response));
	}

	private static Dictionary<string, object?>? ReadArguments(string? response)
	{
		if (string.IsNullOrWhiteSpace(response))
			return null;

		try
		{
#pragma warning disable IL3050, IL2026 // Sample code; the argument shape is not known at compile time.
			return JsonSerializer.Deserialize<Dictionary<string, object?>>(response);
#pragma warning restore IL3050, IL2026
		}
		catch (JsonException)
		{
			return null;
		}
	}

	// ═══════════════════════════════════════════════════════════
	// ANSWERING
	// ═══════════════════════════════════════════════════════════

	/// <summary>
	/// Produces the final answer once no further tool is needed. The caller's own
	/// <see cref="ChatOptions.ResponseFormat"/> is preserved so structured output still works, and
	/// the tools are removed so this middleware is not re-entered.
	/// </summary>
	private async Task<ChatResponse> AnswerAsync(
		IReadOnlyList<ChatMessage> messages,
		ChatOptions? options,
		CancellationToken cancellationToken)
	{
		var answerOptions = options?.Clone() ?? new ChatOptions();
		answerOptions.Tools = null;

		return await base.GetResponseAsync(messages, answerOptions, cancellationToken);
	}

	// ═══════════════════════════════════════════════════════════
	// MODEL ACCESS
	// ═══════════════════════════════════════════════════════════

	/// <summary>
	/// Runs one schema-constrained request, prepending <paramref name="instructions"/> as a system
	/// message and dropping the caller's tools and response format for the duration.
	/// </summary>
	private async Task<string?> RequestAsync(
		IReadOnlyList<ChatMessage> messages,
		string instructions,
		JsonElement schema,
		string schemaName,
		ChatOptions? options,
		CancellationToken cancellationToken)
	{
		var request = new List<ChatMessage>(messages.Count + 1)
		{
			new(ChatRole.System, instructions)
		};
		request.AddRange(messages);

		var requestOptions = options?.Clone() ?? new ChatOptions();
		requestOptions.Tools = null;
		requestOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, schemaName);

		var response = await base.GetResponseAsync(request, requestOptions, cancellationToken);

		return response.Text;
	}

	// ═══════════════════════════════════════════════════════════
	// SCHEMA HELPERS
	// ═══════════════════════════════════════════════════════════

	/// <summary>
	/// Returns a copy of <paramref name="schema"/> with <c>additionalProperties: false</c> applied
	/// to every object, which stops the model inventing property names.
	/// </summary>
	private static JsonElement CloseSchema(JsonElement schema)
	{
		var node = JsonNode.Parse(schema.GetRawText());
		if (node is null)
			return schema;

		Close(node);

		return ToElement(node);

		static void Close(JsonNode? node)
		{
			switch (node)
			{
				case JsonObject obj:
					if (obj.TryGetPropertyValue("type", out var type) &&
						type?.GetValueKind() == JsonValueKind.String &&
						type.GetValue<string>() == "object")
					{
						obj["additionalProperties"] = false;
					}

					foreach (var property in obj.ToList())
						Close(property.Value);
					break;

				case JsonArray array:
					foreach (var item in array)
						Close(item);
					break;
			}
		}
	}

	/// <summary>Whether a parameter schema declares any properties to fill in.</summary>
	private static bool HasProperties(JsonElement schema) =>
		schema.ValueKind == JsonValueKind.Object &&
		schema.TryGetProperty("properties", out var properties) &&
		properties.ValueKind == JsonValueKind.Object &&
		properties.EnumerateObject().Any();

	private static string? ReadString(string? json, string propertyName)
	{
		if (string.IsNullOrWhiteSpace(json))
			return null;

		try
		{
			using var document = JsonDocument.Parse(json);

			return document.RootElement.ValueKind == JsonValueKind.Object &&
				document.RootElement.TryGetProperty(propertyName, out var value) &&
				value.ValueKind == JsonValueKind.String
					? value.GetString()
					: null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static JsonElement ToElement(JsonNode node)
	{
		using var document = JsonDocument.Parse(node.ToJsonString());
		return document.RootElement.Clone();
	}
}
#endif
