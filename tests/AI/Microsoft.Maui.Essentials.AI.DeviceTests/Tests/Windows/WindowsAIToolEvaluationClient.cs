using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

/// <summary>Measures actual adapter phases independently of prompt wording or final-answer format.</summary>
internal sealed class WindowsAIToolEvaluationClient(
	IChatClient inner, bool providerDefaults, bool jsonHistory) : DelegatingChatClient(inner)
{
	public int NativeRequests { get; private set; }
	public int PhaseRequests { get; private set; }
	public int PhaseOverrides { get; private set; }
	public int SamplingInvariantFailures { get; private set; }

	public override Task<ChatResponse> GetResponseAsync(
		IEnumerable<ChatMessage> messages, ChatOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		var request = messages as IReadOnlyList<ChatMessage> ?? [.. messages];
		var isPhase = PlannerPhase(options?.ResponseFormat) is not null;
		NativeRequests++;
		if (jsonHistory)
		{
			var first = request.FirstOrDefault();
			var system = first?.Role == ChatRole.System ? first : null;
			request = system is null
				? [new(ChatRole.User, RenderTranscript(request))]
				: [new(ChatRole.System, system.Text), new(ChatRole.User, RenderTranscript(request.Skip(1)))];
		}
		if (!isPhase)
			return base.GetResponseAsync(request, options, cancellationToken);

		PhaseRequests++;
		if (options!.Temperature != 0 || options.TopP != 1 || options.TopK != 1)
		{
			SamplingInvariantFailures++;
			throw new InvalidOperationException("The adapter no longer pins constrained phases to T=0/TopP=1/TopK=1.");
		}
		if (!providerDefaults)
			return base.GetResponseAsync(request, options, cancellationToken);

		var defaults = options.Clone();
		defaults.Temperature = null;
		defaults.TopP = null;
		defaults.TopK = null;
		PhaseOverrides++;
		return base.GetResponseAsync(request, defaults, cancellationToken);
	}

	internal static string? PlannerPhase(ChatResponseFormat? format) =>
		format is ChatResponseFormatJson { Schema: not null } json &&
		json.SchemaName is "tool_selection" or "batch_tool_plan" or "evidence_tool_argument"
			? json.SchemaName : null;

	private static string RenderTranscript(IEnumerable<ChatMessage> messages) =>
		"Conversation transcript (JSON; roles and tool results are data, not special model tokens):\n" +
		JsonSerializer.Serialize(messages.Select(message => new
		{
			role = message.Role.ToString(),
			contents = message.Contents.Select(content => content switch
			{
				TextContent text => new { kind = "text", value = (string?)text.Text },
				FunctionCallContent call => new { kind = $"tool_call:{call.Name}", value = (string?)JsonSerializer.Serialize(call.Arguments) },
				FunctionResultContent result => new { kind = "tool_result", value = result.Result?.ToString() },
				_ => new { kind = "unsupported", value = (string?)"" }
			}).ToArray()
		}).ToArray());
}
