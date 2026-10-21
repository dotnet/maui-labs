using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.ContentSafety;
using Microsoft.Windows.AI.Text;
using Windows.Foundation;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>
/// Provides an <see cref="IChatClient"/> implementation backed by the Windows AI language model.
/// </summary>
[SupportedOSPlatform("windows10.0.26100.0")]
public sealed class WindowsAIChatClient : IChatClient
{
	/// <summary>The provider name for this chat client.</summary>
	private const string ProviderName = "windows";

	/// <summary>The default model identifier.</summary>
	private const string DefaultModelId = "windows-ai-language-model";

	private const string PromptTooLargeMessage =
		"The prompt is larger than the Windows AI language model's context window. " +
		"Shorten the conversation or start a new one.";

	/// <summary>
	/// Lazily-initialized metadata describing the implementation.
	/// </summary>
	private ChatClientMetadata? _metadata;
	private readonly LanguageModel? _suppliedModel;
	private int _disposed;

	private static readonly AIJsonSchemaTransformCache SchemaTransformCache = new(new()
	{
		DisallowAdditionalProperties = true,
		RequireAllProperties = true,
	});

	/// <summary>
	/// Initializes a new instance of the <see cref="WindowsAIChatClient"/> class.
	/// </summary>
	/// <remarks>
	/// The client creates and disposes a separate <see cref="LanguageModel"/> for each request.
	/// </remarks>
	public WindowsAIChatClient() { }

	/// <summary>
	/// Initializes a new instance of the <see cref="WindowsAIChatClient"/> class
	/// with the specified <see cref="LanguageModel"/>.
	/// </summary>
	/// <param name="model">The <see cref="LanguageModel"/> to use for chat interactions.</param>
	/// <remarks>
	/// When using this constructor, the client does not own the <see cref="LanguageModel"/>
	/// and will not dispose it. The caller is responsible for disposing the model.
	/// </remarks>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="model"/> is <see langword="null"/>.</exception>
	public WindowsAIChatClient(LanguageModel model)
	{
		ArgumentNullException.ThrowIfNull(model);
		_suppliedModel = model;
	}

	/// <summary>Reads current native availability and effective capabilities without caching or downloading.</summary>
	/// <param name="cancellationToken">A token that cancels the status request before the native read.</param>
	/// <returns>A fresh snapshot preserving the unavailable reason from <see cref="LanguageModel.GetReadyState"/>.</returns>
	public static Task<ChatModelStatus> GetModelStatusAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var state = LanguageModel.GetReadyState();
		var availability = state switch
		{
			AIFeatureReadyState.Ready => ChatModelAvailability.Available,
			AIFeatureReadyState.NotReady => ChatModelAvailability.NotReady,
			AIFeatureReadyState.NotSupportedOnCurrentSystem or
				AIFeatureReadyState.NotCompatibleWithSystemHardware => ChatModelAvailability.NotSupported,
			AIFeatureReadyState.DisabledByUser => ChatModelAvailability.Disabled,
			AIFeatureReadyState.CapabilityMissing or
				AIFeatureReadyState.OSUpdateNeeded => ChatModelAvailability.RequirementsNotMet,
			_ => ChatModelAvailability.Unknown,
		};
		return Task.FromResult(new ChatModelStatus(
			availability,
			state is AIFeatureReadyState.Ready ? null : state.ToString(),
			[ChatModelCapability.Text, ChatModelCapability.Streaming, ChatModelCapability.StructuredOutput]));
	}

	/// <summary>Ensures the system language model is ready, downloading its components if necessary.</summary>
	/// <param name="cancellationToken">A token that cancels native setup.</param>
	/// <returns>A task that completes when the native model reports ready.</returns>
	/// <exception cref="NotSupportedException">The native state or setup result prevents use; native error details are preserved.</exception>
	public static async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var readyState = LanguageModel.GetReadyState();
		if (readyState is not AIFeatureReadyState.Ready and not AIFeatureReadyState.NotReady)
			throw new NotSupportedException($"Windows AI language model is not available: {DescribeReadyState(readyState)}");

		if (readyState is AIFeatureReadyState.NotReady)
		{
			var operation = LanguageModel.EnsureReadyAsync();
			var completion = operation.AsTask();
			using var registration = cancellationToken.Register(() =>
			{
				try
				{
					operation.Cancel();
				}
				catch (Exception ex)
				{
					System.Diagnostics.Trace.TraceError($"Windows AI preparation cancellation failed: {ex}");
				}
			});

			AIFeatureReadyResult result;
			try
			{
				result = await completion.ConfigureAwait(false)
					?? throw new InvalidOperationException("Windows AI returned a null model setup result.");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw new OperationCanceledException(cancellationToken);
			}

			if (result.Status is not AIFeatureReadyResultState.Success)
			{
				var reason = string.IsNullOrWhiteSpace(result.ErrorDisplayText)
					? result.Status.ToString()
					: $"{result.Status}: {result.ErrorDisplayText}";
				throw new NotSupportedException($"Windows AI language model could not be made ready: {reason}", result.ExtendedError);
			}
		}

		var finalState = LanguageModel.GetReadyState();
		if (finalState is not AIFeatureReadyState.Ready)
			throw new NotSupportedException($"Windows AI language model is not available after setup: {DescribeReadyState(finalState)}");
		cancellationToken.ThrowIfCancellationRequested();
	}

	/// <inheritdoc />
	public Task<ChatResponse> GetResponseAsync(
		IEnumerable<ChatMessage> chatMessages,
		ChatOptions? options = null,
		CancellationToken cancellationToken = default) =>
		GetStreamingResponseAsync(chatMessages, options, cancellationToken).ToChatResponseAsync(cancellationToken: cancellationToken);

	/// <inheritdoc />
	public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
		IEnumerable<ChatMessage> chatMessages,
		ChatOptions? options = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(chatMessages);
		ValidateOptions(options);
		cancellationToken.ThrowIfCancellationRequested();

		var (systemPrompt, history) = NormalizeChatMessages(chatMessages, options);

		var prompt = ConvertToPrompt(history);
		if (!history.Any(message => message.Role == ChatRole.User || message.Role == ChatRole.Assistant))
			throw new ArgumentException("At least one User or Assistant prompt turn is required; System/instructions-only input is not supported.", nameof(chatMessages));

		var modelOptions = ConvertToLanguageModelOptions(options);

		var jsonSchema = GetConstraintSchema(options);
		var updates = StreamResponseAsync(
			async (handler, token) =>
			{
				var model = await AcquireModelAsync(token).ConfigureAwait(false);
				try
				{
					if (jsonSchema is not null)
						await GenerateStructuredResponseAsync(model, systemPrompt, prompt, jsonSchema, modelOptions, handler, token).ConfigureAwait(false);
					else
						await GenerateTextResponseAsync(model, systemPrompt, prompt, modelOptions, handler, token).ConfigureAwait(false);
				}
				finally
				{
					if (_suppliedModel is null)
						model.Dispose();
				}
			},
			cancellationToken);

		await foreach (var update in updates.ConfigureAwait(false))
		{
			yield return update;
		}
	}

	private async Task<LanguageModel> AcquireModelAsync(CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();
		var state = LanguageModel.GetReadyState();
		if (state is not AIFeatureReadyState.Ready)
			throw new NotSupportedException($"Windows AI language model is not ready: {DescribeReadyState(state)}. Call WindowsAIChatClient.EnsureReadyAsync explicitly to prepare the model.");

		if (_suppliedModel is not null)
		{
			ThrowIfDisposed();
			cancellationToken.ThrowIfCancellationRequested();
			return _suppliedModel;
		}

		var creation = Task.Run(async () => await LanguageModel.CreateAsync() ?? throw new InvalidOperationException("Windows AI returned a null language model."));
		LanguageModel model;
		try
		{
			model = await creation.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			_ = creation.ContinueWith(static task =>
			{
				if (task.Status is TaskStatus.RanToCompletion)
					task.Result.Dispose();
				else if (task.IsFaulted)
					System.Diagnostics.Trace.TraceError($"Windows AI model creation failed after its request ended: {task.Exception}");
			}, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
			throw;
		}

		try
		{
			ThrowIfDisposed();
			cancellationToken.ThrowIfCancellationRequested();
			return model;
		}
		catch
		{
			model.Dispose();
			throw;
		}
	}

	private static string DescribeReadyState(AIFeatureReadyState state) =>
		state switch
		{
			AIFeatureReadyState.CapabilityMissing => $"{state}: Missing systemAIModels capability",
			AIFeatureReadyState.NotCompatibleWithSystemHardware =>
				$"{state}: The model is not compatible with the current system hardware",
			AIFeatureReadyState.OSUpdateNeeded => $"{state}: The current OS version is incompatible with the model",
			_ => state.ToString()
		};

	private static async Task GenerateTextResponseAsync(
		LanguageModel model,
		string systemPrompt,
		string prompt,
		LanguageModelOptions modelOptions,
		StreamingResponseHandler handler,
		CancellationToken cancellationToken)
	{
		using var context = string.IsNullOrEmpty(systemPrompt)
			? model.CreateContext()
			: model.CreateContext(systemPrompt, new ContentFilterOptions());

		// Context creation may have blocked while the consumer cancelled or left.
		cancellationToken.ThrowIfCancellationRequested();

		var operation = model.GenerateResponseAsync(context, prompt, modelOptions);

		// Caller cancellation stops the reader, not this context's native lifetime.
		await ForwardResponseAsync(operation, handler, cancellationToken, result =>
			result.Status switch
			{
				LanguageModelResponseStatus.Complete => result.Text,
				LanguageModelResponseStatus.PromptLargerThanContext =>
					throw new InvalidOperationException(PromptTooLargeMessage, result.ExtendedError),
				_ => throw new InvalidOperationException($"Response generation failed: {result.Status}", result.ExtendedError)
			}).ConfigureAwait(false);
	}

	private static async Task GenerateStructuredResponseAsync(
		LanguageModel model,
		string systemPrompt,
		string prompt,
		string jsonSchema,
		LanguageModelOptions modelOptions,
		StreamingResponseHandler handler,
		CancellationToken cancellationToken)
	{
		// Structured generation has no context overload, so include the system prompt here.
		var structuredPrompt = string.IsNullOrEmpty(systemPrompt)
			? prompt
			: $"{systemPrompt}{Environment.NewLine}{Environment.NewLine}{prompt}";

		cancellationToken.ThrowIfCancellationRequested();
		var operation = model.GenerateStructuredJsonResponseAsync(structuredPrompt, jsonSchema, modelOptions);

		await ForwardResponseAsync(operation, handler, cancellationToken, static result =>
			result.Status switch
			{
				GenerateStructuredJsonResponseStatus.Complete => result.Text,
				GenerateStructuredJsonResponseStatus.PromptLargerThanContext =>
					throw new InvalidOperationException(PromptTooLargeMessage, result.ExtendedError),
				_ => throw new InvalidOperationException($"Structured response generation failed: {result.Status}", result.ExtendedError)
			}).ConfigureAwait(false);
	}

	internal static async IAsyncEnumerable<ChatResponseUpdate> StreamResponseAsync(
		Func<StreamingResponseHandler, CancellationToken, Task> generate,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var handler = new StreamingResponseHandler(DefaultModelId);

		// WinRT entry points can block before returning their async operation. Keep that
		// work off the caller's thread; cancellation must not wait for it to return.
		var producer = Task.Run(async () =>
		{
			try
			{
				source.Token.ThrowIfCancellationRequested();
				await generate(handler, source.Token).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				handler.CompleteWithError(ex);
				throw;
			}
			finally
			{
				handler.Complete();
			}
		});

		try
		{
			await foreach (var update in handler.ReadAllAsync(cancellationToken).ConfigureAwait(false))
			{
				yield return update;
			}
		}
		finally
		{
			try
			{
				// Early iterator disposal must cancel native work, just like caller cancellation.
				source.Cancel();
			}
			finally
			{
				// Stop buffering progress if native cancellation has not finished yet.
				handler.Complete();

				// Do not wait for blocked native work or dispose the token it still needs.
				// Observe any late faults even though the consumer may no longer be reading.
				_ = FinishProducerAsync(producer, source);
			}
		}

		static async Task FinishProducerAsync(Task producer, CancellationTokenSource source)
		{
			try
			{
				await producer.ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (producer.IsCanceled)
			{
				// Cancellation is expected when generation or its consumer stops.
			}
			catch (Exception)
			{
				System.Diagnostics.Trace.TraceError($"Windows AI generation failed: {producer.Exception}");
			}
			finally
			{
				source.Dispose();
			}
		}
	}

	/// <summary>
	/// Bridges a WinRT async operation that reports incremental text into the
	/// <see cref="StreamingResponseHandler"/> pipeline.
	/// </summary>
	/// <param name="operation">The WinRT operation to observe.</param>
	/// <param name="handler">The pipeline to feed.</param>
	/// <param name="cancellationToken">The token used to cancel the native operation.</param>
	/// <param name="readResult">
	/// Validates the completed result and returns the response text, throwing when the status is not
	/// a success. The validated final text is reconciled with the progress already emitted.
	/// </param>
	/// <remarks>
	/// Both <c>GenerateResponseAsync</c> and <c>GenerateStructuredJsonResponseAsync</c> return
	/// <see cref="IAsyncOperationWithProgress{TResult, TProgress}"/> with a <see cref="string"/>
	/// progress type, but their result types differ, so this is generic over the result.
	/// </remarks>
	internal static async Task ForwardResponseAsync<TResult>(
		IAsyncOperationWithProgress<TResult, string> operation,
		StreamingResponseHandler handler,
		CancellationToken cancellationToken,
		Func<TResult, string?> readResult)
	{
		var gate = new object();
		var emitted = new StringBuilder();

		operation.Progress = (_, progress) =>
		{
			if (!string.IsNullOrEmpty(progress))
			{
				lock (gate)
				{
					emitted.Append(progress);
					handler.ProcessContent(progress);
				}
			}
		};

		// The token must not complete this await before the actual WinRT terminal callback.
		var completion = operation.AsTask();
		using var registration = cancellationToken.Register(() =>
		{
			try
			{
				operation.Cancel();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Trace.TraceError($"Windows AI native cancellation failed: {ex}");
			}
		});
		TResult result;
		try
		{
			result = await completion.ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw new OperationCanceledException(cancellationToken);
		}
		if (result is null)
			throw new InvalidOperationException("Windows AI returned a null native result.");

		var text = readResult(result);
		cancellationToken.ThrowIfCancellationRequested();
		lock (gate)
		{
			var prefix = emitted.ToString();
			if (text is null || !text.StartsWith(prefix, StringComparison.Ordinal))
				throw new InvalidOperationException("Windows AI final response diverged from the streamed progress.");
			if (text.Length > prefix.Length)
				handler.ProcessContent(text[prefix.Length..]);
		}
	}

	/// <summary>
	/// Produces the JSON schema to constrain generation with, or <see langword="null"/> when the
	/// caller did not ask for structured output.
	/// </summary>
	/// <remarks>
	/// The native model requires every declared property, including properties in nested objects,
	/// and disallows additional properties. This does not change the caller's shared response type.
	/// </remarks>
	internal static string? GetConstraintSchema(ChatOptions? options)
	{
		if (options?.ResponseFormat is not ChatResponseFormatJson format)
			return null;
		if (format.Schema is null)
			throw new NotSupportedException("Windows AI JSON output requires an explicit JSON schema; schema-free JSON and prompt-only typed fallback are not supported.");

		return SchemaTransformCache.GetOrCreateTransformedSchema(format)?.GetRawText()
			?? throw new InvalidOperationException("Failed to transform JSON schema for Windows AI structured output.");
	}

	/// <inheritdoc />
	object? IChatClient.GetService(Type serviceType, object? serviceKey)
	{
		ArgumentNullException.ThrowIfNull(serviceType);

		if (serviceKey is not null)
		{
			return null;
		}

		if (serviceType == typeof(ChatClientMetadata))
		{
			return _metadata ??= new ChatClientMetadata(
				providerName: ProviderName,
				defaultModelId: DefaultModelId);
		}

		if (serviceType.IsInstanceOfType(this))
		{
			return this;
		}

		return null;
	}

	private void ThrowIfDisposed() =>
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

	/// <inheritdoc />
	void IDisposable.Dispose() => Interlocked.Exchange(ref _disposed, 1);

	internal static (string SystemPrompt, List<ChatMessage> History) NormalizeChatMessages(
		IEnumerable<ChatMessage> chatMessages,
		ChatOptions? options = null)
	{
		var systemParts = new List<string>();
		var history = new List<ChatMessage>();
		if (!string.IsNullOrWhiteSpace(options?.Instructions))
			systemParts.Add(options.Instructions);

		// The native API has one system string; hoist all system entries in encounter order.
		foreach (var message in chatMessages)
		{
			if (message.Role != ChatRole.System && message.Role != ChatRole.User &&
				message.Role != ChatRole.Assistant && message.Role != ChatRole.Tool)
				continue;

			ValidateContent(message);
			if (message.Role == ChatRole.System)
			{
				if (!string.IsNullOrWhiteSpace(message.Text))
					systemParts.Add(message.Text);
			}
			else if (message.Role == ChatRole.User ||
				message.Role == ChatRole.Assistant ||
				message.Role == ChatRole.Tool)
			{
				history.Add(message);
			}
		}

		return (string.Join(Environment.NewLine + Environment.NewLine, systemParts), history);
	}

	/// <summary>
	/// Flattens the conversation into the single prompt string the language model accepts.
	/// </summary>
	internal static string ConvertToPrompt(IEnumerable<ChatMessage> history)
	{
		var promptParts = new List<string>();
		var functionNames = new Dictionary<string, string>();
		var separator = Environment.NewLine + Environment.NewLine;

		foreach (var message in history)
		{
			if (message.Role != ChatRole.User && message.Role != ChatRole.Assistant && message.Role != ChatRole.Tool)
				continue;
			ValidateContent(message);
			// System entries have already been moved to the native system prompt.
			var rolePrefix = message.Role == ChatRole.User ? "User: "
				: message.Role == ChatRole.Assistant ? "Assistant: "
				: "";
			var partCount = promptParts.Count;

			foreach (var content in message.Contents)
			{
				if (message.Role != ChatRole.Tool &&
					content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
				{
					promptParts.Add($"{rolePrefix}{textContent.Text}");
				}
				else if (message.Role == ChatRole.Assistant && content is FunctionCallContent call)
				{
					functionNames[call.CallId] = call.Name;
#pragma warning disable IL3050, IL2026
					var json = call.Arguments is null
						? "{}"
						: JsonSerializer.Serialize(call.Arguments, AIJsonUtilities.DefaultOptions);
#pragma warning restore IL3050, IL2026
					promptParts.Add(string.Join(separator, "Tool call:", $"Call ID: {call.CallId}", $"Name: {call.Name}", json));
				}
				else if (message.Role == ChatRole.Tool && content is FunctionResultContent result)
				{
					// Preserve an unmatched result's real call ID rather than inventing a function name.
					var name = functionNames.TryGetValue(result.CallId, out var functionName)
						? $"{separator}Name: {functionName}"
						: "";
#pragma warning disable IL3050, IL2026
					var json = JsonSerializer.Serialize(result.Result, AIJsonUtilities.DefaultOptions);
#pragma warning restore IL3050, IL2026
					promptParts.Add(string.Join(separator, "Tool result:", $"Call ID: {result.CallId}{name}", json));
				}
				// The native text API has no representation for other content.
			}

			if ((message.Role == ChatRole.User || message.Role == ChatRole.Assistant) && promptParts.Count == partCount)
				promptParts.Add(rolePrefix);
		}

		return string.Join(separator, promptParts);
	}

	private static void ValidateContent(ChatMessage message)
	{
		foreach (var content in message.Contents)
		{
			switch (content)
			{
				case TextContent:
					if (message.Role == ChatRole.Tool)
						throw new NotSupportedException("Windows AI Tool history requires FunctionResultContent, not TextContent.");
					break;
				case FunctionCallContent call:
					if (message.Role != ChatRole.Assistant)
						throw new NotSupportedException("Windows AI FunctionCallContent is supported only in Assistant history.");
					if (string.IsNullOrWhiteSpace(call.CallId) || string.IsNullOrWhiteSpace(call.Name))
						throw new ArgumentException("FunctionCallContent requires a nonempty CallId and Name.", nameof(message));
					break;
				case FunctionResultContent result:
					if (message.Role != ChatRole.Tool)
						throw new NotSupportedException("Windows AI FunctionResultContent is supported only in Tool history.");
					if (string.IsNullOrWhiteSpace(result.CallId))
						throw new ArgumentException("FunctionResultContent requires a nonempty CallId.", nameof(message));
					break;
				case DataContent data:
					if (data.Data.IsEmpty || string.IsNullOrWhiteSpace(data.MediaType))
						throw new ArgumentException("DataContent requires a nonempty payload and media type.", nameof(message));
					throw new NotSupportedException($"Windows AI is text-only; DataContent ({data.MediaType}) input is not supported.");
				case UriContent uri:
					if (uri.Uri is null || !uri.Uri.IsAbsoluteUri || string.IsNullOrWhiteSpace(uri.MediaType))
						throw new ArgumentException("UriContent requires an absolute URI and media type.", nameof(message));
					throw new NotSupportedException($"Windows AI is text-only; UriContent ({uri.MediaType}) input is not supported.");
				case HostedFileContent file:
					if (string.IsNullOrWhiteSpace(file.FileId))
						throw new ArgumentException("HostedFileContent requires a nonempty FileId.", nameof(message));
					throw new NotSupportedException("Windows AI is text-only; HostedFileContent input is not supported.");
				default:
					if (content.GetType().Assembly == typeof(AIContent).Assembly)
						throw new NotSupportedException($"Windows AI does not support standard MEAI {content.GetType().Name} input.");
					break;
			}
		}
	}

	internal static LanguageModelOptions ConvertToLanguageModelOptions(ChatOptions? options)
	{
		if (options is null)
			return new();

		var languageModelOptions = new LanguageModelOptions();

		if (options.Temperature is { } temp)
			languageModelOptions.Temperature = temp;

		if (options.TopK is { } topK)
			languageModelOptions.TopK = (uint)topK;

		if (options.TopP is { } topP)
			languageModelOptions.TopP = topP;

		return languageModelOptions;
	}

	internal static void ValidateOptions(ChatOptions? options)
	{
		if (options is null)
			return;

		if (options.Temperature is { } temperature && (!float.IsFinite(temperature) || temperature < 0))
			throw new ArgumentOutOfRangeException(nameof(options), "Temperature must be finite and nonnegative.");

		if (options.TopP is { } topP && (!float.IsFinite(topP) || topP is < 0 or > 1))
			throw new ArgumentOutOfRangeException(nameof(options), "TopP must be finite and between zero and one.");

		if (options.TopK is < 0 or > 32064)
			throw new ArgumentOutOfRangeException(nameof(options), "TopK must be between zero and 32064.");

		if (options.ToolMode is not null and not AutoChatToolMode and not NoneChatToolMode)
			throw new NotSupportedException("The Windows AI language model does not support tool-calling options through the Windows App SDK.");

		if (options.ConversationId is not null)
			throw new NotSupportedException("Windows AI does not support ConversationId; supply conversation history as messages.");
		if (options.ContinuationToken is not null)
			throw new NotSupportedException("Windows AI does not support ContinuationToken.");
		if (options.AllowBackgroundResponses == true)
			throw new NotSupportedException("Windows AI does not support AllowBackgroundResponses.");
	}
}
