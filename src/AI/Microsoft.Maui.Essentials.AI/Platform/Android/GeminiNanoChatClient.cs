using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
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
/// Requests use independent native models. Supplied user/assistant/tool history is
/// included in the prompt by the Kotlin bridge, with in-memory images and native
/// system instructions. Tool registration and structured output are not supported.
/// Model download is explicit and observable; generation never downloads or falls back to a cloud provider.
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
	public GeminiNanoChatClient() => _native = new();

	/// <summary>Reads current ML Kit availability and effective capabilities without downloading model assets.</summary>
	/// <param name="cancellationToken">Cancels the native status request.</param>
	/// <returns>A fresh advisory snapshot; generation rechecks readiness.</returns>
	public static async Task<ChatModelStatus> GetModelStatusAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		using var callback = new CheckStatusCallback(cancellationToken);
		using var cancellation = AndroidNative.GeminiNanoNative.CheckStatus(callback);
		using var registration = cancellationToken.Register(() => cancellation.Cancel());
		var status = await callback.Response.ConfigureAwait(false);
		return CreateModelStatus(status.Status, status.ThinkingAvailable);
	}

	/// <summary>
	/// Downloads Gemini Nano model assets and streams ML Kit's native download progress.
	/// Check <see cref="GetModelStatusAsync"/> before starting and again after completion.
	/// </summary>
	/// <param name="cancellationToken">Cancels this download Flow collection.</param>
	public static async IAsyncEnumerable<GeminiNanoDownloadUpdate> DownloadModelAsync(
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var channel = Channel.CreateUnbounded<GeminiNanoDownloadUpdate>(new()
		{
			SingleReader = true,
			SingleWriter = true,
		});
		using var callback = new DownloadCallback(channel.Writer, cancellationToken);
		using var cancellation = AndroidNative.GeminiNanoNative.DownloadModel(callback);
		try
		{
			using var registration = cancellationToken.Register(() => cancellation.Cancel());
			await foreach (var update in channel.Reader.ReadAllAsync().ConfigureAwait(false))
				yield return update;
		}
		finally
		{
			cancellation.Cancel();
			await callback.Terminated.ConfigureAwait(false);
		}
	}

	internal static ChatModelStatus CreateModelStatus(int nativeStatus, bool thinkingAvailable)
	{
		// ML Kit beta4 FeatureStatus: UNAVAILABLE=0, DOWNLOADABLE=1, DOWNLOADING=2, AVAILABLE=3.
		// UNAVAILABLE conflates unsupported devices and missing AICore configuration.
		var availability = nativeStatus switch
		{
			3 => ChatModelAvailability.Available,
			1 or 2 => ChatModelAvailability.NotReady,
			_ => ChatModelAvailability.Unknown,
		};
		List<ChatModelCapability> capabilities =
		[
			ChatModelCapability.Text, ChatModelCapability.Streaming,
			ChatModelCapability.ImageInput, ChatModelCapability.TokenUsage,
		];
		if (thinkingAvailable)
			capabilities.Add(ChatModelCapability.Reasoning);
		return new(availability, availability == ChatModelAvailability.Available
			? null : GetUnavailableReason(nativeStatus), capabilities);
	}

	private static string GetUnavailableReason(int nativeStatus) => nativeStatus switch
	{
		0 => "UNAVAILABLE (0): Gemini Nano is unsupported or AICore configuration is not ready.",
		1 => "DOWNLOADABLE (1)",
		2 => "DOWNLOADING (2)",
		_ => $"Unknown ML Kit FeatureStatus ({nativeStatus})",
	};

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
						ModelId = modelName,
					});
				},
				onThought: options?.Reasoning?.Output == ReasoningOutput.Full
					? (thought, modelName) => channel.Writer.TryWrite(new ChatResponseUpdate(
						ChatRole.Assistant,
						[new TextReasoningContent(thought)])
					{
						ModelId = modelName,
					})
					: null,
				onComplete: response =>
				{
					channel.Writer.TryWrite(CreateFinalUpdate(response));
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
		ArgumentNullException.ThrowIfNull(messages);
		var messageList = new List<ChatMessage>();
		foreach (var message in messages)
		{
			if (message.Role == ChatRole.System || message.Role == ChatRole.User ||
				message.Role == ChatRole.Assistant || message.Role == ChatRole.Tool)
				messageList.Add(message);
		}
		if (!messageList.Any(message => message.Role == ChatRole.User || message.Role == ChatRole.Assistant))
			throw new ArgumentException("At least one user or assistant message is required.", nameof(messages));
		ValidateOptions(options);
		if (!string.IsNullOrWhiteSpace(options?.Instructions))
		{
			var instructions = options.Instructions;
			messageList.Insert(0, new ChatMessage(ChatRole.System, instructions));
		}

		var nativeMessages = messageList.Select(message => ToNative(message)).ToArray();
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
		return new(ToNativeRole(message.Role), message.Contents.SelectMany(content => ToNative(content, message.Role)).ToArray());
	}

	private static string ToNativeRole(ChatRole role) =>
		role == ChatRole.System ? "system" :
		role == ChatRole.User ? "user" :
		role == ChatRole.Assistant ? "assistant" :
		role == ChatRole.Tool ? "tool" :
		throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported Gemini Nano chat role.");

	private static IEnumerable<AndroidNative.NativeContentPart> ToNative(AIContent content, ChatRole role)
	{
		ArgumentNullException.ThrowIfNull(content);
		if ((content is FunctionCallContent && role != ChatRole.Assistant) ||
			(content is FunctionResultContent && role != ChatRole.Tool) ||
			(content is DataContent or UriContent && (role == ChatRole.System || role == ChatRole.Tool)))
		{
			var mediaType = content switch { DataContent data => data.MediaType, UriContent uri => uri.MediaType, _ => null };
			throw new NotSupportedException($"Gemini Nano does not support {content.GetType().Name} with media type '{mediaType}' in the '{role}' role.");
		}
		return content switch
		{
			TextContent text => [new(0, text.Text ?? "", null)],
			DataContent data when data.HasTopLevelMediaType("image") && data.Data is { Length: > 0 } bytes =>
				[new(1, null, ValidateImage(bytes.ToArray()))],
			DataContent data when data.HasTopLevelMediaType("image") =>
				throw new ArgumentException("Gemini Nano images require non-empty in-memory data.", nameof(content)),
			DataContent data => throw new NotSupportedException($"Gemini Nano does not support DataContent media type '{data.MediaType}'."),
			UriContent uri => throw new NotSupportedException($"Gemini Nano does not support UriContent media type '{uri.MediaType}'; supply in-memory images."),
			FunctionCallContent call => [new(0, SerializeToolHistory(call), null)],
			FunctionResultContent result => [new(0, SerializeToolHistory(result), null)],
			_ when content.GetType().Assembly == typeof(AIContent).Assembly =>
				throw new NotSupportedException($"Content type '{content.GetType().Name}' is not supported by Gemini Nano."),
			_ => [],
		};
	}

	private static byte[] ValidateImage(byte[] bytes)
	{
		using var bitmap = global::Android.Graphics.BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
		if (bitmap is null)
			throw new ArgumentException("Gemini Nano image payload is not a decodable image.", nameof(bytes));
		return bytes;
	}

	// Textual replay is history, not native tool registration or execution. Results have no invented name.
	private static string SerializeToolHistory(AIContent content)
	{
		using var buffer = new MemoryStream();
		using (var writer = new Utf8JsonWriter(buffer))
		{
			writer.WriteStartObject();
			if (content is FunctionCallContent call)
			{
				writer.WriteString("type", "function_call");
				writer.WriteString("callId", call.CallId);
				writer.WriteString("name", call.Name);
				writer.WritePropertyName("arguments");
				WriteReplayValue(writer, call.Arguments);
			}
			else if (content is FunctionResultContent result)
			{
				writer.WriteString("type", "function_result");
				writer.WriteString("callId", result.CallId);
				writer.WritePropertyName("result");
				WriteReplayValue(writer, result.Result);
			}
			else
				throw new ArgumentException("Expected tool history.", nameof(content));
			writer.WriteEndObject();
		}
		return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
	}

	private static void WriteReplayValue(Utf8JsonWriter writer, object? value)
	{
		if (writer.CurrentDepth >= 64)
			throw new ArgumentException("Gemini Nano tool history is cyclic or exceeds the JSON nesting limit.");
		switch (value)
		{
			case null: writer.WriteNullValue(); break;
			case JsonElement element: element.WriteTo(writer); break;
			case JsonDocument document: document.RootElement.WriteTo(writer); break;
			case string text: writer.WriteStringValue(text); break;
			case char character: writer.WriteStringValue(character.ToString()); break;
			case bool boolean: writer.WriteBooleanValue(boolean); break;
			case byte number: writer.WriteNumberValue(number); break;
			case sbyte number: writer.WriteNumberValue(number); break;
			case short number: writer.WriteNumberValue(number); break;
			case ushort number: writer.WriteNumberValue(number); break;
			case int number: writer.WriteNumberValue(number); break;
			case uint number: writer.WriteNumberValue(number); break;
			case long number: writer.WriteNumberValue(number); break;
			case ulong number: writer.WriteNumberValue(number); break;
			case float number: writer.WriteNumberValue(number); break;
			case double number: writer.WriteNumberValue(number); break;
			case decimal number: writer.WriteNumberValue(number); break;
			case DateTime date: writer.WriteStringValue(date); break;
			case DateTimeOffset date: writer.WriteStringValue(date); break;
			case Guid guid: writer.WriteStringValue(guid); break;
			case Uri uri: writer.WriteStringValue(uri.OriginalString); break;
			case byte[] bytes: writer.WriteBase64StringValue(bytes); break;
			case IEnumerable<KeyValuePair<string, object?>> properties:
				writer.WriteStartObject();
				foreach (var (name, item) in properties)
				{
					writer.WritePropertyName(name);
					WriteReplayValue(writer, item);
				}
				writer.WriteEndObject();
				break;
			case System.Collections.IDictionary dictionary:
				writer.WriteStartObject();
				foreach (System.Collections.DictionaryEntry entry in dictionary)
				{
					if (entry.Key is not string name)
						throw new NotSupportedException("Gemini Nano tool history requires JSON object keys to be strings.");
					writer.WritePropertyName(name);
					WriteReplayValue(writer, entry.Value);
				}
				writer.WriteEndObject();
				break;
			case System.Collections.IEnumerable sequence:
				writer.WriteStartArray();
				foreach (var item in sequence)
					WriteReplayValue(writer, item);
				writer.WriteEndArray();
				break;
			default:
				throw new NotSupportedException(
					$"Gemini Nano tool history cannot serialize '{value.GetType().Name}'. Supply JSON values or a JsonElement created with the caller's JSON serializer context.");
		}
	}

	internal static ChatResponse FromNative(
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
			FinishReason = ToFinishReason(nativeResponse.FinishReason),
			ModelId = nativeResponse.ModelName,
			RawRepresentation = nativeResponse,
			Usage = new UsageDetails { InputTokenCount = nativeResponse.InputTokens },
		};
	}

	internal static ChatResponseUpdate CreateFinalUpdate(AndroidNative.NativeChatResponse nativeResponse) =>
		new()
		{
			Role = ChatRole.Assistant,
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

	private static ChatFinishReason? ToFinishReason(int finishReason) =>
		finishReason switch
		{
			0 => ChatFinishReason.Stop,
			1 => ChatFinishReason.Length,
			-100 => new ChatFinishReason("other"),
			int.MinValue => null,
			_ => new ChatFinishReason($"mlkit:{finishReason}"),
		};

	private static void ValidateOptions(ChatOptions? options)
	{
		if (options is null)
			return;

		if (options.ResponseFormat is not null and not ChatResponseFormatText)
			throw new NotSupportedException("Gemini Nano does not support ChatOptions JSON or structured response formats.");
		if (options.ToolMode is not null and not NoneChatToolMode and not AutoChatToolMode)
			throw new NotSupportedException("The ML Kit GenAI Prompt API cannot honor required ToolMode invocation.");
		if (options.ConversationId is not null)
			throw new NotSupportedException("Gemini Nano does not support ConversationId.");
		if (options.ContinuationToken is not null)
			throw new NotSupportedException("Gemini Nano does not support ContinuationToken.");
		if (options.AllowBackgroundResponses == true)
			throw new NotSupportedException("Gemini Nano does not support AllowBackgroundResponses.");
		if (options.Temperature is { } temperature && (!float.IsFinite(temperature) || temperature is < 0 or > 1))
			throw new ArgumentOutOfRangeException(nameof(options.Temperature), "ML Kit temperature must be between 0 and 1.");
		if (options.TopK is <= 0)
			throw new ArgumentOutOfRangeException(nameof(options.TopK), "ML Kit TopK must be positive.");
		if (options.MaxOutputTokens is <= 0)
			throw new ArgumentOutOfRangeException(nameof(options.MaxOutputTokens), "ML Kit MaxOutputTokens must be positive.");
		if (options.Reasoning is { } reasoning &&
			(reasoning.Effort is not null and not ReasoningEffort.None ||
			 reasoning.Output is not null and not ReasoningOutput.None and not ReasoningOutput.Full ||
			 reasoning.Effort == ReasoningEffort.None && reasoning.Output == ReasoningOutput.Full))
			throw new NotSupportedException("Gemini Nano supports full thinking output, not effort levels or summaries. Set Reasoning.Output to Full without an effort level.");
	}

	private void ThrowIfDisposed() =>
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

	internal static Exception CreateNativeException(AndroidNative.NativeChatError? error, CancellationToken cancellationToken)
	{
		Exception exception = error?.Code == 7
			? new OperationCanceledException(error.Message, cancellationToken)
			: new InvalidOperationException(error is null ? "Gemini Nano operation failed."
				: $"{error.Message} (ML Kit error {error.Code})");
		if (error is not null)
		{
			exception.Data["MLKitErrorCode"] = error.Code;
			exception.Data["MLKitErrorMessage"] = error.Message;
			if (error.RetryDelayMilliseconds is { } delay)
				exception.Data["RetryDelayMilliseconds"] = delay.LongValue();
		}
		return exception;
	}

	private sealed class CheckStatusCallback(CancellationToken cancellationToken) : Java.Lang.Object, AndroidNative.INativeCheckStatusCallback
	{
		private readonly TaskCompletionSource<AndroidNative.NativeModelStatus> _response =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		internal Task<AndroidNative.NativeModelStatus> Response => _response.Task;
		public void OnComplete(AndroidNative.NativeModelStatus status) => _response.TrySetResult(status);
		public void OnError(AndroidNative.NativeChatError? error) =>
			_response.TrySetException(CreateNativeException(error, cancellationToken));
	}

	private sealed class DownloadCallback(
		ChannelWriter<GeminiNanoDownloadUpdate> writer,
		CancellationToken cancellationToken) : Java.Lang.Object, AndroidNative.INativeDownloadCallback
	{
		private readonly TaskCompletionSource _terminated =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		internal Task Terminated => _terminated.Task;

		public void OnStarted(long bytesToDownload) =>
			writer.TryWrite(new(GeminiNanoDownloadState.Started, bytesToDownload));

		public void OnProgress(long bytesDownloaded) =>
			writer.TryWrite(new(GeminiNanoDownloadState.Progress, bytesDownloaded));

		public void OnComplete()
		{
			writer.TryWrite(new(GeminiNanoDownloadState.Completed, 0));
			writer.TryComplete();
			_terminated.TrySetResult();
		}

		public void OnError(AndroidNative.NativeChatError? error)
		{
			writer.TryComplete(CreateNativeException(error, cancellationToken));
			_terminated.TrySetResult();
		}
	}

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
			var exception = CreateNativeException(error, cancellationToken);
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
