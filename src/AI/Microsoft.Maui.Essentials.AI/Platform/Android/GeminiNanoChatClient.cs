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
/// system instructions. Native tool calling requires ML Kit model support;
/// use Microsoft.Extensions.AI function invocation middleware to execute returned calls.
/// Structured output requires an explicit object JSON schema supported by the native bridge.
/// Structured streaming emits a complete validated JSON update after inference, not token deltas.
/// Structured output cannot be combined with tools or full thinking output.
/// Structured requests default to 512 output tokens when MaxOutputTokens is unspecified.
/// Model download is explicit and observable; generation never downloads or falls back to a cloud provider.
/// ChatOptions.ModelId accepts gemini-nano, gemini-nano-fast, gemini-nano-full,
/// gemini-nano-preview-fast, or gemini-nano-preview-full. The default is stable/full.
/// </remarks>
[SupportedOSPlatform("android26.0")]
public sealed partial class GeminiNanoChatClient : IChatClient
{
	private const string ProviderName = "google";
	private const string DefaultModelId = "gemini-nano";
	private static readonly AIJsonSchemaTransformCache SchemaTransformCache = new(new()
	{
		DisallowAdditionalProperties = true,
		RequireAllProperties = true,
		MoveDefaultKeywordToDescription = true,
	});
	private readonly AndroidNative.GeminiNanoNativeClient _native;
	private ChatClientMetadata? _metadata;
	private int _disposed;

	/// <summary>Initializes a client using Gemini Nano.</summary>
	public GeminiNanoChatClient() => _native = new();

	/// <summary>Reads current ML Kit availability and effective capabilities without downloading model assets.</summary>
	/// <param name="cancellationToken">Cancels the native status request.</param>
	/// <returns>A fresh advisory snapshot; generation rechecks readiness.</returns>
	public static Task<ChatModelStatus> GetModelStatusAsync(CancellationToken cancellationToken = default) =>
		GetModelStatusAsync(DefaultModelId, cancellationToken);

	/// <summary>Checks a model profile without downloading its assets.</summary>
	/// <param name="modelId">A Gemini Nano model ID from the profiles supported by this client. Null uses gemini-nano.</param>
	/// <param name="cancellationToken">Cancels the native status request.</param>
	public static async Task<ChatModelStatus> GetModelStatusAsync(string? modelId, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		using var callback = new CheckStatusCallback(cancellationToken);
		using var cancellation = AndroidNative.GeminiNanoNative.CheckStatus(modelId, callback);
		using var registration = cancellationToken.Register(() => cancellation.Cancel());
		var status = await callback.Response.ConfigureAwait(false);
		return CreateModelStatus(status.Status, status.ThinkingAvailable, status.ToolCallingAvailable,
			status.StructuredOutputAvailable);
	}

	/// <summary>
	/// Downloads Gemini Nano model assets and waits for ML Kit's download Flow to complete.
	/// Check <see cref="GetModelStatusAsync(CancellationToken)"/> before starting and again after completion.
	/// </summary>
	/// <param name="cancellationToken">Cancels this download Flow collection.</param>
	public static Task PrepareModelAsync(CancellationToken cancellationToken = default) =>
		PrepareModelAsync(DefaultModelId, cancellationToken);

	/// <summary>Downloads the selected model profile and waits for ML Kit's download Flow to complete.</summary>
	/// <param name="modelId">A Gemini Nano model ID from the profiles supported by this client. Null uses gemini-nano.</param>
	/// <param name="cancellationToken">Cancels this download Flow collection.</param>
	public static async Task PrepareModelAsync(string? modelId, CancellationToken cancellationToken = default)
	{
		await foreach (var _ in DownloadModelAsync(modelId, cancellationToken).ConfigureAwait(false))
		{
		}
	}

	/// <summary>
	/// Downloads Gemini Nano model assets and streams ML Kit's native download progress.
	/// Check <see cref="GetModelStatusAsync(CancellationToken)"/> before starting and again after completion.
	/// </summary>
	/// <param name="cancellationToken">Cancels this download Flow collection.</param>
	public static IAsyncEnumerable<GeminiNanoDownloadUpdate> DownloadModelAsync(CancellationToken cancellationToken = default) =>
		DownloadModelAsync(DefaultModelId, cancellationToken);

	/// <summary>Downloads the selected model profile and streams ML Kit's native progress.</summary>
	/// <param name="modelId">A Gemini Nano model ID from the profiles supported by this client. Null uses gemini-nano.</param>
	/// <param name="cancellationToken">Cancels this download Flow collection.</param>
	public static async IAsyncEnumerable<GeminiNanoDownloadUpdate> DownloadModelAsync(
		string? modelId,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var channel = Channel.CreateUnbounded<GeminiNanoDownloadUpdate>(new()
		{
			SingleReader = true,
			SingleWriter = true,
		});
		using var callback = new DownloadCallback(channel.Writer, cancellationToken);
		using var cancellation = AndroidNative.GeminiNanoNative.DownloadModel(modelId, callback);
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

	internal static ChatModelStatus CreateModelStatus(int nativeStatus, bool thinkingAvailable,
		bool toolCallingAvailable = false, bool structuredOutputAvailable = false)
	{
		// ML Kit beta4 FeatureStatus: UNAVAILABLE=0, DOWNLOADABLE=1, DOWNLOADING=2, AVAILABLE=3.
		var availability = nativeStatus switch
		{
			3 => ChatModelAvailability.Available,
			1 => ChatModelAvailability.NotReady,
			2 => ChatModelAvailability.Downloading,
			0 => ChatModelAvailability.Unavailable,
			_ => ChatModelAvailability.Unknown,
		};
		List<ChatModelCapability> capabilities =
		[
			ChatModelCapability.Text, ChatModelCapability.Streaming,
			ChatModelCapability.ImageInput, ChatModelCapability.TokenUsage,
		];
		if (thinkingAvailable)
			capabilities.Add(ChatModelCapability.Reasoning);
		if (toolCallingAvailable)
			capabilities.Add(ChatModelCapability.ToolCalling);
		if (structuredOutputAvailable)
			capabilities.Add(ChatModelCapability.StructuredOutput);
		return new(availability, capabilities);
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
				options,
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
		var nativeOptions = ToNativeOptions(options);
		var useNativeTools = nativeOptions.ToolsJson is not null;

		foreach (var message in messages)
		{
			ArgumentNullException.ThrowIfNull(message);

			if (message.Role != ChatRole.System && message.Role != ChatRole.User &&
				message.Role != ChatRole.Assistant && message.Role != ChatRole.Tool)
				continue;

			nativeMessages.Add(ToNative(message, useNativeTools));
			hasPrompt |= message.Role == ChatRole.User || message.Role == ChatRole.Assistant;
		}

		if (!hasPrompt)
			throw new ArgumentException("At least one user or assistant message is required.", nameof(messages));

		ValidateOptions(options);

		if (!string.IsNullOrWhiteSpace(options?.Instructions))
			nativeMessages.Insert(0, ToNative(new ChatMessage(ChatRole.System, options.Instructions)));

		return new(nativeMessages.ToArray(), nativeOptions);
	}

	private static AndroidNative.NativeChatOptions ToNativeOptions(ChatOptions? options) =>
		new(
			options?.Temperature is { } temperature ? Java.Lang.Float.ValueOf(temperature) : null,
			options?.TopK is { } topK ? Java.Lang.Integer.ValueOf(topK) : null,
			options?.Seed is { } seed ? Java.Lang.Integer.ValueOf(checked((int)seed)) : null,
			options?.MaxOutputTokens is { } maxOutputTokens ? Java.Lang.Integer.ValueOf(maxOutputTokens) : null,
			enableThinking: options?.Reasoning?.Output == ReasoningOutput.Full,
			modelId: options?.ModelId)
		{
			ToolsJson = SerializeTools(options),
			ResponseSchemaJson = GetConstraintSchema(options),
		};

	internal static string? GetConstraintSchema(ChatOptions? options)
	{
		if (options?.ResponseFormat is not ChatResponseFormatJson format)
			return null;
		if (format.Schema is null)
			throw new NotSupportedException("Gemini Nano structured output requires an explicit JSON schema; schema-free JSON and typed prompt fallback are not supported.");
		if (options.ToolMode is not NoneChatToolMode && options.Tools?.OfType<AIFunction>().Any() == true)
			throw new NotSupportedException("Gemini Nano structured output cannot be combined with native tool calling.");
		if (options.Reasoning?.Output == ReasoningOutput.Full)
			throw new NotSupportedException("Gemini Nano structured output cannot return full thinking output.");

		var schema = SchemaTransformCache.GetOrCreateTransformedSchema(format)?.GetRawText()
			?? throw new InvalidOperationException("Failed to transform JSON schema for Gemini Nano structured output.");
		try
		{
			AndroidNative.NativeStructuredOutput.ValidateSchema(schema);
		}
		catch (Java.Lang.IllegalArgumentException exception)
		{
			throw new NotSupportedException($"Gemini Nano cannot represent this JSON schema: {exception.Message}", exception);
		}
		return schema;
	}

	private static string? SerializeTools(ChatOptions? options)
	{
		if (options?.ToolMode is NoneChatToolMode || options?.Tools is not { Count: > 0 } tools)
			return null;

		var functions = tools.OfType<AIFunction>().ToArray();
		if (functions.Length == 0)
			return null;
		if (functions.Any(function => string.IsNullOrWhiteSpace(function.Name)))
			throw new ArgumentException("Gemini Nano tool names must be nonempty.", nameof(options));
		if (functions.Select(function => function.Name).Distinct(StringComparer.Ordinal).Count() != functions.Length)
			throw new ArgumentException("Gemini Nano tool names must be unique.", nameof(options));

		using var buffer = new MemoryStream();
		using (var writer = new Utf8JsonWriter(buffer))
		{
			writer.WriteStartArray();
			foreach (var function in functions)
			{
				writer.WriteStartObject();
				writer.WriteString("name", function.Name);
				writer.WriteString("description", function.Description ?? "");
				writer.WritePropertyName("parameters");
				function.JsonSchema.WriteTo(writer);
				writer.WriteEndObject();
			}
			writer.WriteEndArray();
		}
		return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
	}

	private static AndroidNative.NativeChatMessage ToNative(ChatMessage message, bool useNativeTools = false)
	{
		var parts = new List<AndroidNative.NativeContentPart>();
		foreach (var content in message.Contents)
		{
			if (ToNative(content, message.Role, useNativeTools) is { } part)
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

	private static AndroidNative.NativeContentPart? ToNative(AIContent content, ChatRole role, bool useNativeTools)
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
				return ToNativeFunctionCall(call, role, useNativeTools);

			case FunctionResultContent result:
				return ToNativeFunctionResult(result, role, useNativeTools);

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

	private static AndroidNative.NativeContentPart ToNativeFunctionCall(FunctionCallContent call, ChatRole role, bool useNativeTools)
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

		return new(useNativeTools ? 2 : 0, json, null);
	}

	private static AndroidNative.NativeContentPart ToNativeFunctionResult(FunctionResultContent result, ChatRole role, bool useNativeTools)
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

		return new(useNativeTools ? 3 : 0, json, null);
	}

	internal static ChatResponse FromNative(
		AndroidNative.NativeChatResponse nativeResponse,
		ChatOptions? options)
	{
		ValidateNativeCalls(nativeResponse, options);
		var contents = new List<AIContent>();
		if (options?.Reasoning?.Output == ReasoningOutput.Full)
		{
			foreach (var thought in nativeResponse.GetThoughts())
			{
				if (!string.IsNullOrEmpty(thought))
					contents.Add(new TextReasoningContent(thought));
			}
		}

		if (!string.IsNullOrEmpty(nativeResponse.Text))
			contents.Add(new TextContent(nativeResponse.Text));
		contents.AddRange(FromNativeCalls(nativeResponse));
		return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents)
		{
			RawRepresentation = nativeResponse,
		})
		{
			FinishReason = nativeResponse.GetFunctionCalls().Length > 0
				? ChatFinishReason.ToolCalls : ToFinishReason(nativeResponse.FinishReason),
			ModelId = nativeResponse.ModelName,
			RawRepresentation = nativeResponse,
			Usage = new UsageDetails { InputTokenCount = nativeResponse.InputTokens },
		};
	}

	internal static ChatResponseUpdate CreateFinalUpdate(AndroidNative.NativeChatResponse nativeResponse, ChatOptions? options = null)
	{
		ValidateNativeCalls(nativeResponse, options);
		return new()
		{
			Role = ChatRole.Assistant,
			ModelId = nativeResponse.ModelName,
			FinishReason = nativeResponse.GetFunctionCalls().Length > 0
				? ChatFinishReason.ToolCalls : ToFinishReason(nativeResponse.FinishReason),
			Contents =
			[
				.. FromNativeCalls(nativeResponse),
				new UsageContent(new UsageDetails
				{
					InputTokenCount = nativeResponse.InputTokens,
				}),
			],
			RawRepresentation = nativeResponse,
		};
	}

	private static void ValidateNativeCalls(AndroidNative.NativeChatResponse response, ChatOptions? options)
	{
		var calls = response.GetFunctionCalls();
		if (calls.Length == 0)
			return;
		if (options?.AllowMultipleToolCalls == false && calls.Length > 1)
			throw new InvalidOperationException("Gemini Nano returned multiple function calls when AllowMultipleToolCalls was false.");
		var names = options?.ToolMode is not NoneChatToolMode && options?.Tools is { } tools
			? tools.OfType<AIFunction>().Select(function => function.Name).ToHashSet(StringComparer.Ordinal)
			: [];
		var ids = new HashSet<string>(StringComparer.Ordinal);
		foreach (var call in calls)
		{
			if (string.IsNullOrWhiteSpace(call.Id) || !ids.Add(call.Id) || !names.Contains(call.Name))
				throw new InvalidOperationException("Gemini Nano returned a duplicate/empty call ID or an unregistered function name.");
		}
	}

	private static IEnumerable<FunctionCallContent> FromNativeCalls(AndroidNative.NativeChatResponse response)
	{
		foreach (var call in response.GetFunctionCalls())
		{
			if (string.IsNullOrWhiteSpace(call.Id) || string.IsNullOrWhiteSpace(call.Name))
				throw new InvalidOperationException("Gemini Nano returned a function call without an ID or name.");
			using var json = JsonDocument.Parse(call.ArgumentsJson);
			yield return new FunctionCallContent(call.Id, call.Name,
				json.RootElement.EnumerateObject().ToDictionary(
					property => property.Name, property => (object?)property.Value.Clone(), StringComparer.Ordinal));
		}
	}

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

		if (options.ResponseFormat is not null and not ChatResponseFormatText and not ChatResponseFormatJson)
			throw new NotSupportedException("Gemini Nano supports text or explicit JSON schema response formats.");

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
