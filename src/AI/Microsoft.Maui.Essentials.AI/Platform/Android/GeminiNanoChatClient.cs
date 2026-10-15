using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Threading.Channels;
using AndroidNative = Com.Microsoft.Maui.Essentials.AI;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>
/// Provides an <see cref="IChatClient"/> backed by Gemini Nano through the
/// Google ML Kit GenAI Prompt API.
/// </summary>
/// <remarks>
/// Requests use independent native models. Supplied user/assistant history is
/// included in the prompt by the Kotlin bridge, with in-memory images and native
/// system instructions. Tools and structured output are not supported. Model availability
/// errors are surfaced to the caller without a cloud fallback.
/// </remarks>
[SupportedOSPlatform("android26.0")]
public sealed class GeminiNanoChatClient : IChatClient
{
	private const string ProviderName = "google";
	private const string DefaultModelId = "gemini-nano";
	private readonly AndroidNative.GeminiNanoNativeClient _native;
	private ChatClientMetadata? _metadata;
	private int _disposed;

	/// <summary>Initializes a client using Gemini Nano.</summary>
	public GeminiNanoChatClient()
	{
		if (!OperatingSystem.IsAndroidVersionAtLeast(26))
			throw new PlatformNotSupportedException("Gemini Nano requires Android 8.0 (API 26) or later.");

		_native = new();
	}

	/// <inheritdoc />
	public async Task<ChatResponse> GetResponseAsync(
		IEnumerable<ChatMessage> messages,
		ChatOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(messages);
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();

		using var request = ToNative(messages, options);
		using var callback = new NativeCallback(cancellationToken: cancellationToken);
		using var cancellation = _native.Generate(request, streaming: false, callback);
		using var registration = cancellationToken.Register(() => cancellation.Cancel());

		var nativeResponse = await callback.Response.ConfigureAwait(false);
		return FromNative(nativeResponse, options);
	}

	/// <inheritdoc />
	public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
		IEnumerable<ChatMessage> messages,
		ChatOptions? options = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(messages);
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();

		AndroidNative.NativeCancellation? nativeCancellation = null;
		NativeCallback? callback = null;
		try
		{
			ThrowIfDisposed();
			using var request = ToNative(messages, options);
			var messageId = Guid.NewGuid().ToString("N");
			var responseId = Guid.NewGuid().ToString("N");
			var channel = Channel.CreateUnbounded<ChatResponseUpdate>(new()
			{
				SingleReader = true,
				SingleWriter = false,
			});

			callback = new NativeCallback(
				onText: (chunk, modelName) =>
				{
					channel.Writer.TryWrite(new ChatResponseUpdate(ChatRole.Assistant, chunk)
					{
						MessageId = messageId,
						ResponseId = responseId,
						ModelId = modelName,
					});
				},
				onThought: options?.Reasoning?.Output == ReasoningOutput.Full
					? (thought, modelName) => channel.Writer.TryWrite(new ChatResponseUpdate(
						ChatRole.Assistant,
						[new TextReasoningContent(thought)])
					{
						MessageId = messageId,
						ResponseId = responseId,
						ModelId = modelName,
					})
					: null,
				onComplete: response =>
				{
					channel.Writer.TryWrite(CreateFinalUpdate(response, messageId, responseId));
					channel.Writer.TryComplete();
				},
				onError: exception => channel.Writer.TryComplete(exception),
				cancellationToken: cancellationToken);

			nativeCancellation = _native.Generate(request, streaming: true, callback);
			using var registration = cancellationToken.Register(() =>
			{
				nativeCancellation.Cancel();
			});

			await foreach (var update in channel.Reader.ReadAllAsync().ConfigureAwait(false))
				yield return update;
		}
		finally
		{
			if (nativeCancellation is not null && callback is not null)
			{
				nativeCancellation.Cancel();
				await callback.Terminated.ConfigureAwait(false);
			}

			nativeCancellation?.Dispose();
			callback?.Dispose();
		}
	}

	object? IChatClient.GetService(Type serviceType, object? serviceKey)
	{
		ArgumentNullException.ThrowIfNull(serviceType);

		if (serviceKey is not null)
			return null;

		if (serviceType == typeof(ChatClientMetadata))
			return _metadata ??= new ChatClientMetadata(
				providerName: ProviderName,
				defaultModelId: DefaultModelId);
		if (serviceType.IsInstanceOfType(this))
			return this;

		return null;
	}

	void IDisposable.Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
			return;

		_native.Close();
		// Keep the Java peer alive for requests racing with Dispose; the bridge
		// rejects them after Close and its peer is released by normal GC.
	}

	internal static AndroidNative.NativeChatRequest ToNative(
		IEnumerable<ChatMessage> messages,
		ChatOptions? options)
	{
		ValidateOptions(options);
		var messageList = messages.ToList();
		if (messageList.Count == 0)
			throw new ArgumentException("At least one chat message is required.", nameof(messages));
		if (options?.Instructions is { } instructions)
			messageList.Insert(0, new ChatMessage(ChatRole.System, instructions));

		var nativeMessages = messageList.Select(ToNative).ToArray();
		var nativeOptions = new AndroidNative.NativeChatOptions(
			options?.Temperature is { } temperature ? Java.Lang.Float.ValueOf(temperature) : null,
			options?.TopK is { } topK ? Java.Lang.Integer.ValueOf(topK) : null,
			options?.Seed is { } seed ? Java.Lang.Integer.ValueOf(checked((int)seed)) : null,
			options?.MaxOutputTokens is { } maxOutputTokens ? Java.Lang.Integer.ValueOf(maxOutputTokens) : null,
			enableThinking: options?.Reasoning?.Output == ReasoningOutput.Full);

		return new AndroidNative.NativeChatRequest(nativeMessages, nativeOptions);
	}

	private static AndroidNative.NativeChatMessage ToNative(ChatMessage message)
	{
		ArgumentNullException.ThrowIfNull(message);
		return new(message.Role.Value, message.Contents.Select(ToNative).ToArray());
	}

	private static AndroidNative.NativeContentPart ToNative(AIContent content)
	{
		ArgumentNullException.ThrowIfNull(content);
		return content switch
		{
			TextContent text => new(0, text.Text, null),
			DataContent data when data.HasTopLevelMediaType("image") && data.Data is { Length: > 0 } bytes =>
				new(1, null, bytes.ToArray()),
			DataContent data when data.HasTopLevelMediaType("image") =>
				throw new NotSupportedException("Gemini Nano images require non-empty in-memory data."),
			_ => throw new NotSupportedException($"Content type '{content.GetType().Name}' is not supported by Gemini Nano."),
		};
	}

	private static ChatResponse FromNative(
		AndroidNative.NativeChatResponse nativeResponse,
		ChatOptions? options)
	{
		var contents = new List<AIContent>();
		if (options?.Reasoning?.Output == ReasoningOutput.Full)
		{
			foreach (var thought in nativeResponse.GetThoughts())
			{
				if (!string.IsNullOrEmpty(thought))
					contents.Add(new TextReasoningContent(thought));
			}
		}

		contents.Add(new TextContent(nativeResponse.Text));
		return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents)
		{
			RawRepresentation = nativeResponse,
		})
		{
			CreatedAt = DateTimeOffset.UtcNow,
			FinishReason = ToFinishReason(nativeResponse.FinishReason),
			ModelId = nativeResponse.ModelName,
			RawRepresentation = nativeResponse,
			Usage = new UsageDetails { InputTokenCount = nativeResponse.InputTokens },
		};
	}

	private static ChatResponseUpdate CreateFinalUpdate(
		AndroidNative.NativeChatResponse nativeResponse,
		string messageId,
		string responseId) =>
		new()
		{
			MessageId = messageId,
			ResponseId = responseId,
			ModelId = nativeResponse.ModelName,
			FinishReason = ToFinishReason(nativeResponse.FinishReason),
			Contents =
			[
				new UsageContent(new UsageDetails
				{
					InputTokenCount = nativeResponse.InputTokens,
				}),
			],
			RawRepresentation = nativeResponse,
		};

	private static ChatFinishReason ToFinishReason(int finishReason) =>
		finishReason switch
		{
			0 => ChatFinishReason.Stop,
			1 => ChatFinishReason.Length,
			_ => new ChatFinishReason("other"),
		};

	private static void ValidateOptions(ChatOptions? options)
	{
		if (options is null)
			return;

		if (options.ResponseFormat is not null and not ChatResponseFormatText)
			throw new NotSupportedException("Gemini Nano does not support ChatOptions JSON or structured response formats.");
		if (options.Tools is { Count: > 0 } ||
			options.ToolMode is { } toolMode && toolMode != ChatToolMode.None ||
			options.AllowMultipleToolCalls is not null)
			throw new NotSupportedException("The ML Kit GenAI Prompt API does not support tool calling.");
		if (options.TopP is not null || options.FrequencyPenalty is not null ||
			options.PresencePenalty is not null || options.ConversationId is not null ||
			options.ContinuationToken is not null || options.ModelId is not null ||
			options.RawRepresentationFactory is not null || options.AllowBackgroundResponses == true ||
			options.StopSequences is { Count: > 0 } || options.AdditionalProperties is { Count: > 0 })
			throw new NotSupportedException("Gemini Nano received chat options without a native ML Kit mapping.");
		if (options.Reasoning is { } reasoning &&
			(reasoning.Effort is not null and not ReasoningEffort.None ||
			 reasoning.Output is not null and not ReasoningOutput.None and not ReasoningOutput.Full ||
			 reasoning.Effort == ReasoningEffort.None && reasoning.Output == ReasoningOutput.Full))
			throw new NotSupportedException("Gemini Nano supports full thinking output, not effort levels or summaries. Set Reasoning.Output to Full without an effort level.");
	}

	private void ThrowIfDisposed() =>
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

	private sealed class NativeCallback(
		Action<string, string>? onText = null,
		Action<string, string>? onThought = null,
		Action<AndroidNative.NativeChatResponse>? onComplete = null,
		Action<Exception>? onError = null,
		CancellationToken cancellationToken = default) : Java.Lang.Object, AndroidNative.INativeChatCallback
	{
		private readonly TaskCompletionSource<AndroidNative.NativeChatResponse>? _response =
			onComplete is null && onError is null
				? new(TaskCreationOptions.RunContinuationsAsynchronously)
				: null;
		private readonly TaskCompletionSource _terminated =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		internal Task<AndroidNative.NativeChatResponse> Response =>
			_response?.Task ?? throw new InvalidOperationException("This native callback does not produce a response task.");

		internal Task Terminated => _terminated.Task;

		public void OnText(string? text, string modelName)
		{
			if (!string.IsNullOrEmpty(text))
				onText?.Invoke(text, modelName);
		}

		public void OnThought(string? thought, string modelName)
		{
			if (!string.IsNullOrEmpty(thought))
				onThought?.Invoke(thought, modelName);
		}

		public void OnComplete(AndroidNative.NativeChatResponse? response)
		{
			if (response is null)
			{
				OnError(new AndroidNative.NativeChatError(
					"Gemini Nano returned no response.",
					0,
					null));
				return;
			}

			try
			{
				onComplete?.Invoke(response);
				_response?.TrySetResult(response);
			}
			finally
			{
				_terminated.TrySetResult();
			}
		}

		public void OnError(AndroidNative.NativeChatError? error)
		{
			Exception exception = error?.Code == 7
				? new OperationCanceledException(error.Message, cancellationToken)
				: new InvalidOperationException(
					error is null
						? "Gemini Nano inference failed."
						: $"{error.Message} (ML Kit error {error.Code})");
			try
			{
				onError?.Invoke(exception);
				_response?.TrySetException(exception);
			}
			finally
			{
				_terminated.TrySetResult();
			}
		}
	}
}
