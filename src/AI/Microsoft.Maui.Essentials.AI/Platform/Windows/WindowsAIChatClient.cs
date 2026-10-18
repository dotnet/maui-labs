using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Extensions.AI;
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

	private const int InvalidArgumentHResult = unchecked((int)0x80070057);

	private const string PromptTooLargeMessage =
		"The prompt is larger than the Windows AI language model's context window. " +
		"Shorten the conversation or start a new one.";

	/// <summary>Lazily-initialized task that creates the underlying <see cref="LanguageModel"/>.</summary>
	private readonly Lazy<Task<LanguageModel>> _modelTask;

	/// <summary>Whether this instance owns the <see cref="LanguageModel"/> and is responsible for disposing it.</summary>
	private readonly bool _ownsModel;

	/// <summary>
	/// Lazily-initialized metadata describing the implementation.
	/// </summary>
	private ChatClientMetadata? _metadata;

	private static readonly AIJsonSchemaTransformCache SchemaTransformCache = new(new()
	{
		DisallowAdditionalProperties = true,
		RequireAllProperties = true,
	});

	/// <summary>
	/// Initializes a new instance of the <see cref="WindowsAIChatClient"/> class.
	/// </summary>
	/// <remarks>
	/// The client will create a <see cref="LanguageModel"/> and reuse it for all requests.
	/// </remarks>
	public WindowsAIChatClient()
	{
		_modelTask = new(() => Task.Run(WindowsAIModelFactory.CreateModelAsync));
		_ownsModel = true;
	}

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
		_modelTask = new(() => Task.FromResult(model));
		_ownsModel = false;
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
		ArgumentNullException.ThrowIfNull(chatMessages);
		ValidateOptions(options);
		cancellationToken.ThrowIfCancellationRequested();

		var (systemPrompt, history) = NormalizeChatMessages(chatMessages, options);

		var prompt = ConvertToPrompt(history);
		if (history.Count == 0 && string.IsNullOrEmpty(systemPrompt))
			throw new ArgumentException("At least one message with content is required.", nameof(chatMessages));

		var modelOptions = ConvertToLanguageModelOptions(options);

		var jsonSchema = GetConstraintSchema(options);
		var model = await _modelTask.Value.WaitAsync(cancellationToken).ConfigureAwait(false);

		var updates = StreamResponseAsync(
			(handler, token) => jsonSchema is not null
				? GenerateStructuredResponseAsync(model, systemPrompt, prompt, jsonSchema, modelOptions, handler, token)
				: GenerateTextResponseAsync(model, systemPrompt, prompt, modelOptions, handler, token),
			cancellationToken);

		await foreach (var update in updates.ConfigureAwait(false))
		{
			yield return update;
		}
	}

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

		// The worker owns the context through completion or cancellation of the operation.
		await ForwardResponseAsync(operation, handler, cancellationToken, result =>
			GetResponseText(result.Status, result.Text, result.ExtendedError, string.IsNullOrEmpty(prompt))).ConfigureAwait(false);
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

		var operation = model.GenerateStructuredJsonResponseAsync(structuredPrompt, jsonSchema, modelOptions);

		await ForwardResponseAsync(operation, handler, cancellationToken, static result =>
			GetStructuredResponseText(result.Status, result.Text, result.ExtendedError)).ConfigureAwait(false);
	}

	internal static string? GetResponseText(
		LanguageModelResponseStatus status, string? text, Exception? extendedError, bool isEmptyPrompt) =>
		status switch
		{
			LanguageModelResponseStatus.Complete => text,

			// Native empty prompts report E_INVALIDARG rather than an empty completion.
			LanguageModelResponseStatus.Error when isEmptyPrompt &&
				(extendedError is null ||
				 extendedError is ArgumentException && extendedError.HResult == InvalidArgumentHResult) => text,

			LanguageModelResponseStatus.PromptLargerThanContext =>
				throw new InvalidOperationException(PromptTooLargeMessage, extendedError),

			_ => throw new InvalidOperationException($"Response generation failed: {status}", extendedError)
		};

	internal static string? GetStructuredResponseText(
		GenerateStructuredJsonResponseStatus status, string? text, Exception? extendedError) =>
		status switch
		{
			GenerateStructuredJsonResponseStatus.Complete => text,
			GenerateStructuredJsonResponseStatus.PromptLargerThanContext =>
				throw new InvalidOperationException(PromptTooLargeMessage, extendedError),
			_ => throw new InvalidOperationException($"Structured response generation failed: {status}", extendedError)
		};

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
	/// a success. It is always called, so the status is checked either way, but the text it returns
	/// is only emitted when the operation reported no incremental progress.
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
		var reportedProgress = false;

		operation.Progress = (_, progress) =>
		{
			if (!string.IsNullOrEmpty(progress))
			{
				reportedProgress = true;
				handler.ProcessContent(progress);
			}
		};

		// AsTask without IProgress preserves the synchronous callback above.
		var result = await operation.AsTask(cancellationToken).ConfigureAwait(false);

		// Always validate the final status, but do not duplicate text already streamed.
		var text = readResult(result);
		if (!reportedProgress)
			handler.ProcessContent(text);
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
		if (options?.ResponseFormat is not ChatResponseFormatJson { Schema: not null } format)
			return null;

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

	/// <inheritdoc />
	void IDisposable.Dispose()
	{
		if (_ownsModel && _modelTask.IsValueCreated)
			DisposeWhenReady(_modelTask.Value);
	}

	private static void DisposeWhenReady<T>(Task<T> task)
		where T : IDisposable
	{
		if (task.IsCompletedSuccessfully)
			task.Result.Dispose();
		else
			task.ContinueWith(
				t => { if (t.IsCompletedSuccessfully) t.Result.Dispose(); },
				TaskContinuationOptions.ExecuteSynchronously);
	}

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
					promptParts.Add(string.Join(separator, "Tool call:", call.Name, json));
				}
				else if (message.Role == ChatRole.Tool && content is FunctionResultContent result)
				{
					// Preserve an unmatched result's real call ID rather than inventing a function name.
					var name = functionNames.TryGetValue(result.CallId, out var functionName)
						? functionName
						: result.CallId;
#pragma warning disable IL3050, IL2026
					var json = JsonSerializer.Serialize(result.Result, AIJsonUtilities.DefaultOptions);
#pragma warning restore IL3050, IL2026
					promptParts.Add(string.Join(separator, "Tool result:", name, json));
				}
				// The native text API has no representation for other content.
			}

			if (message.Role == ChatRole.User && promptParts.Count == partCount)
				promptParts.Add(rolePrefix);
		}

		return string.Join(separator, promptParts);
	}

	private static LanguageModelOptions ConvertToLanguageModelOptions(ChatOptions? options)
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

	private static void ValidateOptions(ChatOptions? options)
	{
		if (options is null)
			return;

		if (options.MaxOutputTokens is <= 0)
			throw new ArgumentOutOfRangeException(nameof(options), "MaxOutputTokens must be greater than zero.");

		if (options.TopK is < 0)
			throw new ArgumentOutOfRangeException(nameof(options), "TopK must be non-negative.");

		if (options.Tools is { Count: > 0 })
			throw new NotSupportedException("The Windows AI language model does not support tool calling through the Windows App SDK.");

		if ((options.ToolMode is not null && options.ToolMode != ChatToolMode.Auto && options.ToolMode != ChatToolMode.None) || options.AllowMultipleToolCalls is not null)
			throw new NotSupportedException("The Windows AI language model does not support tool-calling options through the Windows App SDK.");
	}
}
