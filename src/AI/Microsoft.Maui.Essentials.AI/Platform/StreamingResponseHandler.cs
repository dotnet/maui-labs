using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>
/// Handles the channel, chunker, and update processing for streaming responses.
/// Extracted from the platform-specific chat client for testability.
/// </summary>
/// <remarks>
/// When a <see cref="StreamChunkerBase"/> is provided, <see cref="ProcessContent"/> expects
/// cumulative snapshots and the chunker computes deltas (used by Apple Intelligence).
/// When no chunker is provided, <see cref="ProcessContent"/> expects pre-chunked deltas
/// and passes them through directly (used by Windows Phi Silica).
/// </remarks>
internal sealed class StreamingResponseHandler
{
	private readonly Channel<ChatResponseUpdate> _channel;
	private readonly StreamChunkerBase? _chunker;
	private string? _modelId;
	private string? _contentMessageId;
	private readonly Dictionary<(string MessageId, string SegmentId), string> _reasoningSegments = [];
	private readonly Dictionary<string, string> _reasoningProtection = [];
	private readonly bool _includeReasoning = true;

	/// <summary>
	/// Creates a handler that passes content through directly (no chunking).
	/// Use when the AI model already provides incremental deltas.
	/// </summary>
	public StreamingResponseHandler()
	{
		_channel = Channel.CreateUnbounded<ChatResponseUpdate>(
			new UnboundedChannelOptions { SingleReader = true });
	}

	/// <summary>
	/// Creates a handler with a chunker for computing deltas from cumulative snapshots.
	/// Use when the AI model provides progressively longer complete responses.
	/// </summary>
	/// <param name="chunker">The chunker that computes content deltas.</param>
	/// <param name="modelId">Optional provider model identifier to include on every update.</param>
	/// <param name="includeReasoning">Whether returned reasoning is visible; does not control model computation.</param>
	public StreamingResponseHandler(StreamChunkerBase chunker, string? modelId = null, bool includeReasoning = true) : this()
	{
		_chunker = chunker;
		_modelId = modelId;
		_includeReasoning = includeReasoning;
	}

	/// <summary>
	/// Processes a content (text) streaming update.
	/// If a chunker is configured, <paramref name="text"/> should be the cumulative response.
	/// If no chunker, <paramref name="text"/> should be the incremental delta.
	/// </summary>
	public void ProcessContent(string? text, string? messageId = null)
	{
		if (text is null)
			return;

		if (messageId is not null && messageId != _contentMessageId)
		{
			FlushContent();
			_chunker?.Reset();
			_contentMessageId = messageId;
		}
		var delta = _chunker is not null ? _chunker.Process(text) : text;
		if (!string.IsNullOrEmpty(delta))
		{
			_channel.Writer.TryWrite(new ChatResponseUpdate
			{
				Role = ChatRole.Assistant,
				ModelId = _modelId,
				MessageId = _contentMessageId,
				Contents = { new TextContent(delta) }
			});
		}
	}

	/// <summary>Processes cumulative native reasoning segments, separate from answer chunking.</summary>
	public void ProcessReasoning(string? messageId, string? segmentId, string? text, string? protectedData)
	{
		if (!_includeReasoning)
			return;
		ArgumentException.ThrowIfNullOrEmpty(messageId);

		string? delta = null;
		if (text is not null)
		{
			ArgumentException.ThrowIfNullOrEmpty(segmentId);
			var key = (messageId, segmentId);
			var previous = _reasoningSegments.GetValueOrDefault(key) ?? string.Empty;
			if (!text.StartsWith(previous, StringComparison.Ordinal))
				throw new InvalidDataException("Native reasoning snapshots must extend the same entry/segment, not rewrite it.");
			delta = text[previous.Length..];
			_reasoningSegments[key] = text;
		}
		if (protectedData is not null)
		{
			if (_reasoningProtection.GetValueOrDefault(messageId) == protectedData)
				protectedData = null;
			else
				_reasoningProtection[messageId] = protectedData;
		}
		if (string.IsNullOrEmpty(delta) && protectedData is null)
			return;

		FlushContent();
		_channel.Writer.TryWrite(new ChatResponseUpdate
		{
			Role = ChatRole.Assistant,
			ModelId = _modelId,
			MessageId = messageId,
			Contents = { new TextReasoningContent(delta) { ProtectedData = protectedData } },
		});
	}

	/// <summary>
	/// Processes a tool call update. Flushes any pending content first.
	/// </summary>
	public void ProcessToolCall(string? toolCallId, string? toolCallName, string? toolCallArguments)
	{
		FlushContent();
		_chunker?.Reset();
		_contentMessageId = null;

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
	/// Flushes remaining chunker content and completes the channel successfully.
	/// </summary>
	public void SetModelId(string? modelId) => _modelId = modelId;

	public void Complete(UsageDetails? usage = null)
	{
		FlushContent();

		if (usage is not null)
		{
			_channel.Writer.TryWrite(new ChatResponseUpdate
			{
				Role = ChatRole.Assistant,
				ModelId = _modelId,
				Contents = { new UsageContent(usage) },
			});
		}
		_channel.Writer.TryComplete();
	}

	private void FlushContent()
	{
		var pending = _chunker?.Flush();
		if (!string.IsNullOrEmpty(pending))
		{
			_channel.Writer.TryWrite(new ChatResponseUpdate
			{
				Role = ChatRole.Assistant,
				ModelId = _modelId,
				MessageId = _contentMessageId,
				Contents = { new TextContent(pending) },
			});
		}
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
