using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AndroidNative = Com.Microsoft.Maui.Essentials.AI;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>
/// Provides an <see cref="IChatClient"/> backed by Gemini Nano through the
/// Google ML Kit GenAI Prompt API.
/// </summary>
/// <remarks>
/// Requests are stateless and use independent native models. Images must contain
/// in-memory data. JSON output is prompt-guided and validated against a bounded
/// schema subset; it is not native typed generation. Model availability errors
/// are surfaced to the caller without a cloud fallback.
/// </remarks>
public sealed class GeminiNanoChatClient : IChatClient
{
	private const string ProviderName = "google";
	private const string DefaultModelId = "gemini-nano";
	private const string JsonOnlyInstruction =
		"Return only a valid JSON value. Do not include Markdown fences, commentary, or text outside the JSON value.";

	private readonly AndroidNative.GeminiNanoNativeClient _native = new();
	private ChatClientMetadata? _metadata;
	private int _disposed;

	/// <summary>Initializes a client using Gemini Nano.</summary>
	public GeminiNanoChatClient()
	{
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

		var messageList = messages.ToList();
		GeminiNanoPromptFormatter.ValidateMessages(messageList);
		GeminiNanoPromptFormatter.ValidateOptions(options);

		ThrowIfDisposed();
		using var request = CreateRequest(messageList, options);
		using var callback = new NativeCallback(cancellationToken: cancellationToken);
		using var cancellation = _native.Generate(request, streaming: false, callback);
		using var registration = cancellationToken.Register(() => cancellation.Cancel());

		var nativeResponse = await callback.Response.ConfigureAwait(false);
		var response = FromNative(nativeResponse, options);
		ValidateJsonResponse(response.Text, options?.ResponseFormat);
		return response;
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

		var messageList = messages.ToList();
		GeminiNanoPromptFormatter.ValidateMessages(messageList);
		GeminiNanoPromptFormatter.ValidateOptions(options);

		AndroidNative.NativeCancellation? nativeCancellation = null;
		NativeCallback? callback = null;
		try
		{
			ThrowIfDisposed();
			using var request = CreateRequest(messageList, options);
			var messageId = Guid.NewGuid().ToString("N");
			var responseId = Guid.NewGuid().ToString("N");
			var text = new StringBuilder();
			var channel = Channel.CreateUnbounded<ChatResponseUpdate>(new()
			{
				SingleReader = true,
				SingleWriter = false,
			});

			callback = new NativeCallback(
				onText: chunk =>
				{
					text.Append(chunk);
					channel.Writer.TryWrite(new ChatResponseUpdate(ChatRole.Assistant, chunk)
					{
						MessageId = messageId,
						ResponseId = responseId,
					});
				},
				onThought: options?.Reasoning?.Output == ReasoningOutput.Full
					? thought => channel.Writer.TryWrite(new ChatResponseUpdate(
						ChatRole.Assistant,
						[new TextReasoningContent(thought)])
					{
						MessageId = messageId,
						ResponseId = responseId,
					})
					: null,
				onComplete: response =>
				{
					try
					{
						ValidateJsonResponse(text.ToString(), options?.ResponseFormat);
						channel.Writer.TryWrite(CreateFinalUpdate(
							response,
							messageId,
							responseId));
						channel.Writer.TryComplete();
					}
					catch (Exception exception)
					{
						channel.Writer.TryComplete(exception);
					}
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

	private static AndroidNative.NativeChatRequest CreateRequest(
		IReadOnlyList<ChatMessage> messages,
		ChatOptions? options)
	{
		var responseInstruction = CreateResponseInstruction(options?.ResponseFormat);
		var systemInstruction = GeminiNanoPromptFormatter.GetSystemInstruction(
			messages,
			options,
			responseInstruction);
		var nativeMessages = new List<AndroidNative.NativeChatMessage>();

		foreach (var message in GeminiNanoPromptFormatter.GetConversationMessages(messages))
		{
			var role = message.Role == ChatRole.User ? 0 : 1;
			var parts = new List<AndroidNative.NativeContentPart>
			{
				new(
					kind: 0,
					text: GeminiNanoPromptFormatter.RoleStart(
						GeminiNanoPromptFormatter.FormatRole(message.Role)),
					image: null),
			};

			foreach (var content in message.Contents)
			{
				switch (content)
				{
					case TextContent { Text.Length: > 0 } text:
						parts.Add(new(0, text.Text, null));
						break;
					case TextContent:
						break;
					case TextReasoningContent:
						break;
					case DataContent data when data.HasTopLevelMediaType("image") && data.Data is { } bytes:
						parts.Add(new(1, null, bytes.ToArray()));
						break;
					case DataContent data when data.HasTopLevelMediaType("image"):
						throw new NotSupportedException("Gemini Nano image content must contain in-memory data.");
					default:
						throw new NotSupportedException(
							$"Content type '{content.GetType().Name}' is not supported by Gemini Nano.");
				}
			}

			parts.Add(new(0, GeminiNanoPromptFormatter.RoleEnd, null));
			nativeMessages.Add(new(role, [.. parts]));
		}

		var reasoning = options?.Reasoning;
		var nativeOptions = new AndroidNative.NativeChatOptions(
			systemInstruction,
			options?.Temperature is { } temperature ? Java.Lang.Float.ValueOf(temperature) : null,
			options?.TopK is { } topK ? Java.Lang.Integer.ValueOf(topK) : null,
			options?.Seed is { } seed ? Java.Lang.Integer.ValueOf(checked((int)seed)) : null,
			options?.MaxOutputTokens is { } maxOutputTokens ? Java.Lang.Integer.ValueOf(maxOutputTokens) : null,
			enableThinking: ShouldEnableThinking(reasoning));

		return new AndroidNative.NativeChatRequest([.. nativeMessages], nativeOptions);
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

	private static string? CreateResponseInstruction(ChatResponseFormat? responseFormat) =>
		responseFormat switch
		{
			null or ChatResponseFormatText => null,
			ChatResponseFormatJson json when json.Schema is null => JsonOnlyInstruction,
			ChatResponseFormatJson json =>
				$"{JsonOnlyInstruction}\nThe JSON value must conform to this schema:\n{StructuredOutputSchema.GetRequiredSchema(json).GetRawText()}",
			_ => throw new NotSupportedException(
				$"Response format '{responseFormat.GetType().Name}' is not supported by Gemini Nano."),
		};

	private static bool ShouldEnableThinking(ReasoningOptions? reasoning) =>
		reasoning is not null &&
		(reasoning.Effort is not null and not ReasoningEffort.None ||
		 reasoning.Output == ReasoningOutput.Full);

	private static void ValidateJsonResponse(string text, ChatResponseFormat? responseFormat)
	{
		if (responseFormat is not ChatResponseFormatJson)
			return;

		try
		{
			using var document = JsonDocument.Parse(text);
			if (responseFormat is ChatResponseFormatJson jsonFormat &&
				jsonFormat.Schema is not null)
			{
				StructuredOutputSchema.ValidateResponse(
					document.RootElement,
					StructuredOutputSchema.GetRequiredSchema(jsonFormat));
			}
		}
		catch (JsonException exception)
		{
			throw new InvalidOperationException("Gemini Nano returned invalid JSON.", exception);
		}
	}

	private void ThrowIfDisposed() =>
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

	private sealed class NativeCallback(
		Action<string>? onText = null,
		Action<string>? onThought = null,
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

		public void OnText(string? text)
		{
			if (!string.IsNullOrEmpty(text))
				onText?.Invoke(text);
		}

		public void OnThought(string? thought)
		{
			if (!string.IsNullOrEmpty(thought))
				onThought?.Invoke(thought);
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
