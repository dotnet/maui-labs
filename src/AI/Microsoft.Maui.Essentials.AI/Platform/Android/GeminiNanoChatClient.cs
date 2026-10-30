using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
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
public sealed partial class GeminiNanoChatClient : IChatClient
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
	/// Downloads Gemini Nano model assets and waits for ML Kit's download Flow to complete.
	/// Check <see cref="GetModelStatusAsync"/> before starting and again after completion.
	/// </summary>
	/// <param name="cancellationToken">Cancels this download Flow collection.</param>
	public static async Task PrepareModelAsync(CancellationToken cancellationToken = default)
	{
		await foreach (var _ in DownloadModelAsync(cancellationToken).ConfigureAwait(false))
		{
		}
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
		using var callback = new ResponseCallback(cancellationToken);
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
		StreamingResponseCallback? callback = null;
		try
		{
			ThrowIfDisposed();
			using var request = ToNative(messages, options);
			var channel = Channel.CreateUnbounded<ChatResponseUpdate>(new()
			{
				SingleReader = true,
				SingleWriter = false,
			});

			callback = new StreamingResponseCallback(
				channel.Writer,
				includeThoughts: options?.Reasoning?.Output == ReasoningOutput.Full,
				cancellationToken);

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

		var nativeMessages = new List<AndroidNative.NativeChatMessage>();
		var hasPrompt = false;

		foreach (var message in messages)
		{
			ArgumentNullException.ThrowIfNull(message);

			if (message.Role != ChatRole.System && message.Role != ChatRole.User &&
				message.Role != ChatRole.Assistant && message.Role != ChatRole.Tool)
				continue;

			nativeMessages.Add(ToNative(message));
			hasPrompt |= message.Role == ChatRole.User || message.Role == ChatRole.Assistant;
		}

		if (!hasPrompt)
			throw new ArgumentException("At least one user or assistant message is required.", nameof(messages));

		ValidateOptions(options);

		if (!string.IsNullOrWhiteSpace(options?.Instructions))
			nativeMessages.Insert(0, ToNative(new ChatMessage(ChatRole.System, options.Instructions)));

		return new(nativeMessages.ToArray(), ToNativeOptions(options));
	}

	private static AndroidNative.NativeChatOptions ToNativeOptions(ChatOptions? options) =>
		new(
			options?.Temperature is { } temperature ? Java.Lang.Float.ValueOf(temperature) : null,
			options?.TopK is { } topK ? Java.Lang.Integer.ValueOf(topK) : null,
			options?.Seed is { } seed ? Java.Lang.Integer.ValueOf(checked((int)seed)) : null,
			options?.MaxOutputTokens is { } maxOutputTokens ? Java.Lang.Integer.ValueOf(maxOutputTokens) : null,
			enableThinking: options?.Reasoning?.Output == ReasoningOutput.Full);

	private static AndroidNative.NativeChatMessage ToNative(ChatMessage message)
	{
		var parts = new List<AndroidNative.NativeContentPart>();
		foreach (var content in message.Contents)
		{
			if (ToNative(content, message.Role) is { } part)
				parts.Add(part);
		}

		return new(ToNativeRole(message.Role), parts.ToArray());
	}

	private static string ToNativeRole(ChatRole role)
	{
		if (role == ChatRole.System)
			return "system";

		if (role == ChatRole.User)
			return "user";

		if (role == ChatRole.Assistant)
			return "assistant";

		if (role == ChatRole.Tool)
			return "tool";

		throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported Gemini Nano chat role.");
	}

	private static AndroidNative.NativeContentPart? ToNative(AIContent content, ChatRole role)
	{
		ArgumentNullException.ThrowIfNull(content);

		switch (content)
		{
			case TextContent text:
				if (role == ChatRole.Tool)
					throw new NotSupportedException("Gemini Nano Tool history requires FunctionResultContent, not TextContent.");

				return new(0, text.Text ?? "", null);

			case DataContent data:
				return ToNativeImage(data, role);

			case UriContent uri:
				throw new NotSupportedException($"Gemini Nano does not support UriContent media type '{uri.MediaType}'; supply in-memory images.");

			case HostedFileContent:
				throw new NotSupportedException(
					"Gemini Nano does not support HostedFileContent; images must be supplied as in-memory DataContent.");

			case FunctionCallContent call:
				return ToNativeFunctionCall(call, role);

			case FunctionResultContent result:
				return ToNativeFunctionResult(result, role);

			default:
				if (content.GetType().Assembly == typeof(AIContent).Assembly)
					throw new NotSupportedException($"Content type '{content.GetType().Name}' is not supported by Gemini Nano.");

				return null;
		}
	}

	private static AndroidNative.NativeContentPart ToNativeImage(DataContent data, ChatRole role)
	{
		if (role != ChatRole.User && role != ChatRole.Assistant)
			throw new NotSupportedException(
				$"Gemini Nano does not support DataContent media type '{data.MediaType}' in '{role}' history; " +
				"in-memory images are supported only in User or Assistant history.");

		if (!data.HasTopLevelMediaType("image"))
			throw new NotSupportedException($"Gemini Nano does not support DataContent media type '{data.MediaType}'.");

		if (data.Data.IsEmpty || !IsValidMediaType(data.MediaType))
			throw new ArgumentException("Image DataContent requires a nonempty payload and a valid media type.", nameof(data));

		var bytes = data.Data.ToArray();
		using var bitmap = global::Android.Graphics.BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
		if (bitmap is null)
			throw new ArgumentException("Gemini Nano image payload is not a decodable image.", nameof(data));

		return new(1, null, bytes);
	}

	private static bool IsValidMediaType(string? mediaType) =>
		MediaTypeHeaderValue.TryParse(mediaType, out var parsed) &&
		parsed.MediaType is { } value && !value.Contains('*');

	private static AndroidNative.NativeContentPart ToNativeFunctionCall(FunctionCallContent call, ChatRole role)
	{
		if (role != ChatRole.Assistant)
			throw new NotSupportedException("Gemini Nano FunctionCallContent is supported only in Assistant history.");

		if (string.IsNullOrWhiteSpace(call.CallId) || string.IsNullOrWhiteSpace(call.Name))
			throw new ArgumentException("FunctionCallContent requires a nonempty CallId and Name.", nameof(call));

#pragma warning disable IL3050, IL2026
		var json = JsonSerializer.Serialize(new Dictionary<string, object?>
		{
			["type"] = "function_call",
			["callId"] = call.CallId,
			["name"] = call.Name,
			["arguments"] = call.Arguments,
		}, AIJsonUtilities.DefaultOptions);
#pragma warning restore IL3050, IL2026

		return new(0, json, null);
	}

	private static AndroidNative.NativeContentPart ToNativeFunctionResult(FunctionResultContent result, ChatRole role)
	{
		if (role != ChatRole.Tool)
			throw new NotSupportedException("Gemini Nano FunctionResultContent is supported only in Tool history.");

		if (string.IsNullOrWhiteSpace(result.CallId))
			throw new ArgumentException("FunctionResultContent requires a nonempty CallId.", nameof(result));

		// Replay is prompt history, not native tool execution; results have no invented function name.
#pragma warning disable IL3050, IL2026
		var json = JsonSerializer.Serialize(new Dictionary<string, object?>
		{
			["type"] = "function_result",
			["callId"] = result.CallId,
			["result"] = result.Result,
		}, AIJsonUtilities.DefaultOptions);
#pragma warning restore IL3050, IL2026

		return new(0, json, null);
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
}
