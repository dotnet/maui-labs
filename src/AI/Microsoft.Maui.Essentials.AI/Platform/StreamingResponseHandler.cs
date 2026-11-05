using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>
/// Handles the channel and update processing for streaming responses.
/// Extracted from the platform-specific chat client for testability.
/// </summary>
/// <remarks>
/// <see cref="ProcessContent"/> expects append-only deltas. Provider-native bridges normalize
/// cumulative snapshots before forwarding them to managed code.
/// </remarks>
internal sealed class StreamingResponseHandler
{
	private readonly Channel<ChatResponseUpdate> _channel;
	private readonly string? _modelId;

	/// <summary>
	/// Creates a handler that passes provider-native deltas through directly.
	/// </summary>
	/// <param name="modelId">Optional provider model identifier to include on every update.</param>
	public StreamingResponseHandler(string? modelId = null)
	{
		_channel = Channel.CreateUnbounded<ChatResponseUpdate>(
			new UnboundedChannelOptions { SingleReader = true });
		_modelId = modelId;
	}

	/// <summary>
	/// Processes an append-only content delta.
	/// </summary>
	public void ProcessContent(string? text)
	{
		if (string.IsNullOrEmpty(text))
			return;

		_channel.Writer.TryWrite(new ChatResponseUpdate
		{
			Role = ChatRole.Assistant,
			ModelId = _modelId,
			Contents = { new TextContent(text) }
		});
	}

	/// <summary>
	/// Processes a tool call update.
	/// </summary>
	public void ProcessToolCall(string? toolCallId, string? toolCallName, string? toolCallArguments)
	{
		var args = toolCallArguments is null
			? null
#pragma warning disable IL3050, IL2026 // DefaultJsonTypeInfoResolver is only used when reflection-based serialization is enabled
			: JsonSerializer.Deserialize<AIFunctionArguments>(toolCallArguments, AIJsonUtilities.DefaultOptions);
#pragma warning restore IL3050, IL2026

		_channel.Writer.TryWrite(new ChatResponseUpdate
		{
			Role = ChatRole.Assistant,
			ModelId = _modelId,
			Contents = { new FunctionCallContent(toolCallId!, toolCallName!, args) { InformationalOnly = true } }
		});
	}

	/// <summary>
	/// Processes a tool result update.
	/// </summary>
	public void ProcessToolResult(string? toolCallId, string? toolCallResult)
	{
		_channel.Writer.TryWrite(new ChatResponseUpdate
		{
			Role = ChatRole.Tool,
			ModelId = _modelId,
			Contents = { new FunctionResultContent(toolCallId!, toolCallResult!) }
		});
	}

	/// <summary>
	/// Emits usage and completes the channel successfully.
	/// </summary>
	public void Complete(UsageDetails? usage = null)
	{
		if (usage is not null)
		{
			_channel.Writer.TryWrite(new ChatResponseUpdate
			{
				ModelId = _modelId,
				Contents = { new UsageContent(usage) }
			});
		}

		_channel.Writer.TryComplete();
	}

	/// <summary>
	/// Completes the channel with an error. Safe to call multiple times.
	/// </summary>
	public void CompleteWithError(Exception exception)
	{
		_channel.Writer.TryComplete(exception);
	}

	/// <summary>
	/// Returns an async enumerable that reads all updates from the channel.
	/// </summary>
	public IAsyncEnumerable<ChatResponseUpdate> ReadAllAsync(CancellationToken cancellationToken)
		=> _channel.Reader.ReadAllAsync(cancellationToken);
}
