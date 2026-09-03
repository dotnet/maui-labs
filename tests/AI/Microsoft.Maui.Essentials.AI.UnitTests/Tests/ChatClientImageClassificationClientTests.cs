// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class ChatClientImageClassificationClientTests
{
	[Fact]
	public void Constructor_HasExpectedPublicShape()
	{
		var constructor = Assert.Single(typeof(ChatClientImageClassificationClient).GetConstructors());

		Assert.Collection(
			constructor.GetParameters(),
			parameter =>
			{
				Assert.Equal("chatClient", parameter.Name);
				Assert.Equal(typeof(IChatClient), parameter.ParameterType);
				Assert.False(parameter.IsOptional);
			},
			parameter =>
			{
				Assert.Equal("labels", parameter.Name);
				Assert.Equal(typeof(IEnumerable<string>), parameter.ParameterType);
				Assert.False(parameter.IsOptional);
			},
			parameter =>
			{
				Assert.Equal("metadata", parameter.Name);
				Assert.Equal(typeof(ImageClassificationClientMetadata), parameter.ParameterType);
				Assert.True(parameter.IsOptional);
				Assert.Null(parameter.DefaultValue);
			});
	}

	[Fact]
	public void Constructor_NullOrEmptyInputs_ThrowDocumentedExceptions()
	{
		var chatClient = new RecordingChatClient();

		ArgumentNullException nullClient = Assert.Throws<ArgumentNullException>(
			() => new ChatClientImageClassificationClient(null!, ["cat"]));
		ArgumentNullException nullLabels = Assert.Throws<ArgumentNullException>(
			() => new ChatClientImageClassificationClient(chatClient, null!));
		ArgumentException emptyLabels = Assert.Throws<ArgumentException>(
			() => new ChatClientImageClassificationClient(chatClient, []));

		Assert.Equal("chatClient", nullClient.ParamName);
		Assert.Equal("labels", nullLabels.ParamName);
		Assert.Equal("labels", emptyLabels.ParamName);
	}

	[Fact]
	public void Constructor_NullEmptyWhitespaceOrDuplicateLabel_Throws()
	{
		var chatClient = new RecordingChatClient();
		IEnumerable<string>[] invalidAllowlists =
		[
			new string[] { null! },
			[""],
			[" \t"],
			["cat", "cat"]
		];

		foreach (IEnumerable<string> labels in invalidAllowlists)
		{
			ArgumentException exception = Assert.Throws<ArgumentException>(
				() => new ChatClientImageClassificationClient(chatClient, labels));

			Assert.Equal("labels", exception.ParamName);
		}
	}

	[Fact]
	public async Task Constructor_SnapshotsLabelAllowlist()
	{
		var labels = new List<string> { "cat", "dog" };
		var chatClient = new RecordingChatClient(CreateResponse("""{"labels":["cat"]}"""));
		using var client = new ChatClientImageClassificationClient(chatClient, labels);
		labels.Clear();
		labels.Add("fox");

		ImageClassificationResult result = await ClassifyAsync(client);

		Assert.Equal("cat", Assert.Single(result.Predictions).Label);
		TextContent prompt = Assert.IsType<TextContent>(
			Assert.Single(chatClient.Messages).Contents[0]);
		Assert.Equal(
			"Classify the attached image. Return only a JSON object exactly matching {\"labels\":[...]}, with labels in descending relevance order. Use each label at most once and use only labels from this allowlist: [\"cat\",\"dog\"]",
			prompt.Text);
		Assert.DoesNotContain("fox", prompt.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void Constructor_DefaultMetadata_CopiesChatClientMetadata()
	{
		var providerUri = new Uri("https://provider.example.test");
		var chatMetadata = new ChatClientMetadata("provider", providerUri, "default-model");
		var chatClient = new RecordingChatClient(metadata: chatMetadata);
		using var client = new ChatClientImageClassificationClient(chatClient, ["cat"]);

		ImageClassificationClientMetadata metadata =
			Assert.IsType<ImageClassificationClientMetadata>(
				client.GetService(typeof(ImageClassificationClientMetadata)));

		Assert.Equal("provider", metadata.ProviderName);
		Assert.Equal(providerUri, metadata.ProviderUri);
		Assert.Equal("default-model", metadata.DefaultModelId);
		Assert.Collection(
			chatClient.ServiceRequests,
			request =>
			{
				Assert.Equal(typeof(ChatClientMetadata), request.ServiceType);
				Assert.Null(request.ServiceKey);
			});
	}

	[Fact]
	public async Task ClassifyImageAsync_ValidRequest_UsesStrictJsonSchemaAndExactImageContent()
	{
		byte[] imageBytes = [0x89, 0x50, 0x4E, 0x47];
		var chatClient = new RecordingChatClient(CreateResponse("""{"labels":["cat"]}"""));
		using var client = new ChatClientImageClassificationClient(chatClient, ["cat", "dog"]);
		using var imageStream = new MemoryStream(imageBytes);

		ImageClassificationResult result =
			await client.ClassifyImageAsync(imageStream, "image/png");

		Assert.Equal(1, chatClient.CallCount);
		ChatMessage message = Assert.Single(chatClient.Messages);
		Assert.Equal(ChatRole.User, message.Role);
		Assert.Collection(
			message.Contents,
			content =>
			{
				TextContent text = Assert.IsType<TextContent>(content);
				Assert.Equal(
					"Classify the attached image. Return only a JSON object exactly matching {\"labels\":[...]}, with labels in descending relevance order. Use each label at most once and use only labels from this allowlist: [\"cat\",\"dog\"]",
					text.Text);
			},
			content =>
			{
				DataContent image = Assert.IsType<DataContent>(content);
				Assert.Equal(imageBytes, image.Data.ToArray());
				Assert.Equal("image/png", image.MediaType);
			});

		ChatResponseFormatJson responseFormat = Assert.IsType<ChatResponseFormatJson>(
			Assert.IsType<ChatOptions>(chatClient.Options).ResponseFormat);
		Assert.True(responseFormat.Schema.HasValue);
		JsonElement schema = responseFormat.Schema.GetValueOrDefault();
		Assert.Equal("object", schema.GetProperty("type").GetString());
		Assert.Collection(
			schema.GetProperty("properties").EnumerateObject(),
			property =>
			{
				Assert.Equal("labels", property.Name);
				Assert.Equal("array", property.Value.GetProperty("type").GetString());
				Assert.Equal("string", property.Value.GetProperty("items").GetProperty("type").GetString());
			});
		Assert.Equal(
			["labels"],
			schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
		Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
		Assert.Equal("cat", Assert.Single(result.Predictions).Label);
	}

	[Fact]
	public async Task ClassifyImageAsync_IgnoredResponseFormatTopLevelArray_PreservesRanking()
	{
		var chatClient = new RecordingChatClient(CreateResponse("""["dog","cat"]"""));
		using var client = new ChatClientImageClassificationClient(chatClient, ["cat", "dog"]);

		ImageClassificationResult result = await ClassifyAsync(client);

		Assert.Equal(["dog", "cat"], result.Predictions.Select(prediction => prediction.Label));
		Assert.All(result.Predictions, prediction => Assert.Null(prediction.Confidence));
		Assert.IsType<ChatResponseFormatJson>(Assert.IsType<ChatOptions>(chatClient.Options).ResponseFormat);
	}

	[Theory]
	[InlineData("{not-json")]
	[InlineData("The labels are [\"cat\"].")]
	[InlineData("```json\n{\"labels\":[\"cat\"]}\n```")]
	public async Task ClassifyImageAsync_MalformedProseOrFencedJson_IsRejected(string responseText)
	{
		var chatClient = new RecordingChatClient(CreateResponse(responseText));
		using var client = new ChatClientImageClassificationClient(chatClient, ["cat"]);

		InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => ClassifyAsync(client));

		Assert.Equal("The chat client returned a malformed image classification response.", exception.Message);
		Assert.IsAssignableFrom<JsonException>(exception.InnerException);
		Assert.Equal(1, chatClient.CallCount);
	}

	[Fact]
	public async Task ClassifyImageAsync_MissingLabels_IsRejected()
	{
		var chatClient = new RecordingChatClient(CreateResponse("{}"));
		using var client = new ChatClientImageClassificationClient(chatClient, ["cat"]);

		InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => ClassifyAsync(client));

		Assert.Equal("The chat client returned a malformed image classification response.", exception.Message);
		Assert.IsType<JsonException>(exception.InnerException);
		Assert.Equal(1, chatClient.CallCount);
	}

	[Fact]
	public async Task ClassifyImageAsync_ExtraTopLevelProperty_IsRejected()
	{
		var chatClient = new RecordingChatClient(
			CreateResponse("""{"labels":["cat"],"extra":true}"""));
		using var client = new ChatClientImageClassificationClient(chatClient, ["cat"]);

		InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => ClassifyAsync(client));

		Assert.Equal("The chat client returned a malformed image classification response.", exception.Message);
		Assert.IsType<JsonException>(exception.InnerException);
		Assert.Equal(1, chatClient.CallCount);
	}

	[Fact]
	public async Task ClassifyImageAsync_DuplicateLabelsProperty_IsRejected()
	{
		var chatClient = new RecordingChatClient(
			CreateResponse("""{"labels":["lynx"],"labels":["cat"]}"""));
		using var client = new ChatClientImageClassificationClient(chatClient, ["cat"]);

		InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => ClassifyAsync(client));

		Assert.Equal("The chat client returned a malformed image classification response.", exception.Message);
		Assert.IsType<JsonException>(exception.InnerException);
		Assert.Equal(1, chatClient.CallCount);
	}

	[Theory]
	[InlineData("""{"labels":["lynx"]}""")]
	[InlineData("""["lynx"]""")]
	public async Task ClassifyImageAsync_UnknownLabel_IsRejected(string responseText)
	{
		var chatClient = new RecordingChatClient(CreateResponse(responseText));
		using var client = new ChatClientImageClassificationClient(chatClient, ["cat"]);

		InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => ClassifyAsync(client));

		Assert.Equal(
			"The chat client returned a label outside the allowlist or returned a duplicate label.",
			exception.Message);
		Assert.Null(exception.InnerException);
		Assert.Equal(1, chatClient.CallCount);
	}

	[Fact]
	public async Task ClassifyImageAsync_DuplicateLabel_IsRejected()
	{
		var chatClient = new RecordingChatClient(
			CreateResponse("""{"labels":["cat","cat"]}"""));
		using var client = new ChatClientImageClassificationClient(chatClient, ["cat"]);

		InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => ClassifyAsync(client));

		Assert.Equal(
			"The chat client returned a label outside the allowlist or returned a duplicate label.",
			exception.Message);
		Assert.Null(exception.InnerException);
		Assert.Equal(1, chatClient.CallCount);
	}

	[Fact]
	public async Task ClassifyImageAsync_LabelsMapInProviderOrderWithNullConfidence()
	{
		var chatClient = new RecordingChatClient(
			CreateResponse("""{"labels":["dog","cat","bird"]}"""));
		using var client = new ChatClientImageClassificationClient(
			chatClient,
			["cat", "dog", "bird"]);

		ImageClassificationResult result = await ClassifyAsync(client);

		Assert.Collection(
			result.Predictions,
			prediction =>
			{
				Assert.Equal("dog", prediction.Label);
				Assert.Null(prediction.Confidence);
			},
			prediction =>
			{
				Assert.Equal("cat", prediction.Label);
				Assert.Null(prediction.Confidence);
			},
			prediction =>
			{
				Assert.Equal("bird", prediction.Label);
				Assert.Null(prediction.Confidence);
			});
		Assert.Equal(1, chatClient.CallCount);
	}

	[Fact]
	public async Task ClassifyImageAsync_MaximumPredictions_TruncatesWithoutReordering()
	{
		var chatClient = new RecordingChatClient(
			CreateResponse("""{"labels":["dog","cat","bird"]}"""));
		using var client = new ChatClientImageClassificationClient(
			chatClient,
			["cat", "dog", "bird"]);
		var options = new ImageClassificationOptions { MaximumPredictions = 2 };

		ImageClassificationResult result = await ClassifyAsync(client, options);

		Assert.Equal(["dog", "cat"], result.Predictions.Select(prediction => prediction.Label));
		Assert.All(result.Predictions, prediction => Assert.Null(prediction.Confidence));
		Assert.Equal(1, chatClient.CallCount);
	}

	[Fact]
	public async Task ClassifyImageAsync_OptionsMutatedDuringPendingRequest_UsesEntrySnapshot()
	{
		var chatClient = new RecordingChatClient(
			CreateResponse("""{"labels":["dog","cat","bird"]}"""));
		using var client = new ChatClientImageClassificationClient(
			chatClient,
			["cat", "dog", "bird"]);
		var options = new ImageClassificationOptions
		{
			MaximumInputBytes = 3,
			MaximumPredictions = 1,
			MinimumConfidence = null
		};
		using var imageStream = new PendingReadStream([1, 2, 3]);

		Task<ImageClassificationResult> classificationTask =
			client.ClassifyImageAsync(imageStream, "image/png", options);
		await imageStream.ReadStarted;
		Assert.Equal(0, chatClient.CallCount);

		options.MaximumInputBytes = 1;
		options.MaximumPredictions = 3;
		options.MinimumConfidence = 0.5f;
		imageStream.ReleaseRead();

		ImageClassificationResult result = await classificationTask;

		Assert.Equal("dog", Assert.Single(result.Predictions).Label);
		Assert.Null(result.Predictions[0].Confidence);
		Assert.Equal(3, imageStream.BytesRead);
		Assert.Equal(1, chatClient.CallCount);
	}

	[Fact]
	public async Task ClassifyImageAsync_ExactMaximumInputBytes_Succeeds()
	{
		byte[] imageBytes = [1, 2, 3];
		var chatClient = new RecordingChatClient(CreateResponse("""{"labels":["cat"]}"""));
		using var client = new ChatClientImageClassificationClient(chatClient, ["cat"]);
		using var imageStream = new MemoryStream(imageBytes);
		var options = new ImageClassificationOptions { MaximumInputBytes = imageBytes.Length };

		ImageClassificationResult result =
			await client.ClassifyImageAsync(imageStream, "image/png", options);

		DataContent image = Assert.IsType<DataContent>(
			Assert.Single(chatClient.Messages).Contents[1]);
		Assert.Equal(imageBytes, image.Data.ToArray());
		Assert.Equal("cat", Assert.Single(result.Predictions).Label);
		Assert.Equal(1, chatClient.CallCount);
	}

	[Fact]
	public async Task ClassifyImageAsync_SeekableInputExceedsMaximum_RejectsBeforeReading()
	{
		var chatClient = new RecordingChatClient();
		using var client = new ChatClientImageClassificationClient(chatClient, ["cat"]);
		using var imageStream = new MemoryStream([1, 2, 3, 4]);
		var options = new ImageClassificationOptions { MaximumInputBytes = 3 };

		ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
			() => client.ClassifyImageAsync(imageStream, "image/png", options));

		Assert.Equal("imageStream", exception.ParamName);
		Assert.Contains("configured maximum of 3 bytes", exception.Message, StringComparison.Ordinal);
		Assert.Contains("MaximumInputBytes", exception.Message, StringComparison.Ordinal);
		Assert.Equal(0, imageStream.Position);
		Assert.Equal(0, chatClient.CallCount);
	}

	[Fact]
	public async Task ClassifyImageAsync_NonSeekableInputExceedsMaximum_ReadsAtMostLimitPlusOne()
	{
		var chatClient = new RecordingChatClient();
		using var client = new ChatClientImageClassificationClient(chatClient, ["cat"]);
		using var imageStream = new NonSeekableReadStream([1, 2, 3, 4, 5]);
		var options = new ImageClassificationOptions { MaximumInputBytes = 3 };

		ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
			() => client.ClassifyImageAsync(imageStream, "image/png", options));

		Assert.Equal("imageStream", exception.ParamName);
		Assert.Equal(4, imageStream.BytesRead);
		Assert.False(imageStream.IsDisposed);
		Assert.Equal(0, chatClient.CallCount);
	}

	[Fact]
	public async Task ClassifyImageAsync_CancellationDuringChunkedRead_StopsWithoutCallingClient()
	{
		var chatClient = new RecordingChatClient();
		using var client = new ChatClientImageClassificationClient(chatClient, ["cat"]);
		using var imageStream = new CancellableChunkedStream();
		using var cancellationSource = new CancellationTokenSource();

		Task<ImageClassificationResult> classificationTask = client.ClassifyImageAsync(
			imageStream,
			"image/png",
			new ImageClassificationOptions { MaximumInputBytes = 10 },
			cancellationSource.Token);
		await imageStream.FirstReadCompleted;

		cancellationSource.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => classificationTask);
		Assert.Equal(1, imageStream.BytesRead);
		Assert.False(imageStream.IsDisposed);
		Assert.Equal(0, chatClient.CallCount);
	}

	[Fact]
	public async Task ClassifyImageAsync_NonNullMinimumConfidence_ThrowsBeforeInnerCall()
	{
		var chatClient = new RecordingChatClient(CreateResponse("""{"labels":["cat"]}"""));
		using var client = new ChatClientImageClassificationClient(chatClient, ["cat"]);
		using var imageStream = new MemoryStream([1, 2, 3]);
		var options = new ImageClassificationOptions { MinimumConfidence = 0f };

		NotSupportedException exception = await Assert.ThrowsAsync<NotSupportedException>(
			() => client.ClassifyImageAsync(imageStream, "image/png", options));

		Assert.Equal(
			"ChatClientImageClassificationClient does not produce confidence values.",
			exception.Message);
		Assert.Equal(0, chatClient.CallCount);
		Assert.Equal(0, imageStream.Position);
	}

	[Fact]
	public async Task ClassifyImageAsync_ResultCopiesExactProviderResponseData()
	{
		var additionalProperties = new AdditionalPropertiesDictionary
		{
			["requestId"] = "request-42"
		};
		ChatResponse response = CreateResponse(
			"""{"labels":["cat"]}""",
			modelId: "provider-model",
			additionalProperties: additionalProperties);
		var chatClient = new RecordingChatClient(response);
		using var client = new ChatClientImageClassificationClient(chatClient, ["cat"]);

		ImageClassificationResult result = await ClassifyAsync(client);

		AdditionalPropertiesDictionary actualProperties =
			Assert.IsType<AdditionalPropertiesDictionary>(result.AdditionalProperties);
		Assert.Same(response, result.RawRepresentation);
		Assert.Equal("provider-model", result.ModelId);
		Assert.Same(additionalProperties, actualProperties);
		Assert.Equal("request-42", actualProperties["requestId"]);
		Assert.Equal("cat", Assert.Single(result.Predictions).Label);
	}

	[Fact]
	public async Task ClassifyImageAsync_ForwardsExactCancellationToken()
	{
		var chatClient = new RecordingChatClient(CreateResponse("""{"labels":["cat"]}"""));
		using var client = new ChatClientImageClassificationClient(chatClient, ["cat"]);
		using var cancellationSource = new CancellationTokenSource();

		ImageClassificationResult result = await ClassifyAsync(
			client,
			cancellationToken: cancellationSource.Token);

		Assert.Equal(cancellationSource.Token, chatClient.CancellationToken);
		Assert.Equal(1, chatClient.CallCount);
		Assert.Equal("cat", Assert.Single(result.Predictions).Label);
	}

	[Fact]
	public async Task ClassifyImageAsync_NullUnreadableOrEmptyStream_RejectsBeforeInnerCall()
	{
		var chatClient = new RecordingChatClient();
		using var client = new ChatClientImageClassificationClient(chatClient, ["cat"]);
		using var unreadableStream = new UnreadableStream();
		using var emptyStream = new MemoryStream();

		ArgumentNullException nullException = await Assert.ThrowsAsync<ArgumentNullException>(
			() => client.ClassifyImageAsync(null!, "image/png"));
		ArgumentException unreadableException = await Assert.ThrowsAsync<ArgumentException>(
			() => client.ClassifyImageAsync(unreadableStream, "image/png"));
		ArgumentException emptyException = await Assert.ThrowsAsync<ArgumentException>(
			() => client.ClassifyImageAsync(emptyStream, "image/png"));

		Assert.Equal("imageStream", nullException.ParamName);
		Assert.Equal("imageStream", unreadableException.ParamName);
		Assert.Equal("imageStream", emptyException.ParamName);
		Assert.Equal(0, chatClient.CallCount);
	}

	[Fact]
	public async Task ClassifyImageAsync_NullMalformedOrNonImageMediaType_RejectsBeforeInnerCall()
	{
		var chatClient = new RecordingChatClient();
		using var client = new ChatClientImageClassificationClient(chatClient, ["cat"]);
		using var nullMediaStream = new MemoryStream([1]);
		using var malformedMediaStream = new MemoryStream([1]);
		using var nonImageMediaStream = new MemoryStream([1]);

		ArgumentNullException nullException = await Assert.ThrowsAsync<ArgumentNullException>(
			() => client.ClassifyImageAsync(nullMediaStream, null!));
		ArgumentException malformedException = await Assert.ThrowsAsync<ArgumentException>(
			() => client.ClassifyImageAsync(malformedMediaStream, "not a media type"));
		ArgumentException nonImageException = await Assert.ThrowsAsync<ArgumentException>(
			() => client.ClassifyImageAsync(nonImageMediaStream, "text/plain"));

		Assert.Equal("imageMediaType", nullException.ParamName);
		Assert.Equal("mediaType", malformedException.ParamName);
		Assert.Equal("imageMediaType", nonImageException.ParamName);
		Assert.Equal(0, chatClient.CallCount);
		Assert.Equal(0, malformedMediaStream.Position);
		Assert.Equal(0, nonImageMediaStream.Position);
	}

	[Fact]
	public async Task ClassifyImageAsync_DirectCallerStreamRemainsOpen()
	{
		var chatClient = new RecordingChatClient(CreateResponse("""{"labels":["cat"]}"""));
		using var client = new ChatClientImageClassificationClient(chatClient, ["cat"]);
		using var imageStream = new MemoryStream([1, 2, 3]);

		ImageClassificationResult result =
			await client.ClassifyImageAsync(imageStream, "image/webp");

		Assert.True(imageStream.CanRead);
		Assert.Equal(3, imageStream.Position);
		imageStream.Position = 0;
		Assert.Equal(1, imageStream.ReadByte());
		Assert.Equal("cat", Assert.Single(result.Predictions).Label);
		Assert.Equal(1, chatClient.CallCount);
	}

	[Fact]
	public void GetService_TypedAndUntypedExposeAdapterSuppliedMetadataAndInnerClient()
	{
		var metadata = new ImageClassificationClientMetadata(
			"provider",
			new Uri("https://provider.example.test"),
			"model");
		var chatClient = new RecordingChatClient();
		using var client = new ChatClientImageClassificationClient(
			chatClient,
			["cat"],
			metadata);

		Assert.Same(client, client.GetService<ChatClientImageClassificationClient>());
		Assert.Same(client, client.GetService<IImageClassificationClient>());
		Assert.Same(client, client.GetService(typeof(ChatClientImageClassificationClient)));
		Assert.Same(client, client.GetService(typeof(IImageClassificationClient)));
		Assert.Same(metadata, client.GetService<ImageClassificationClientMetadata>());
		Assert.Same(metadata, client.GetService(typeof(ImageClassificationClientMetadata)));
		Assert.Same(chatClient, client.GetService<IChatClient>());
		Assert.Same(chatClient, client.GetService(typeof(IChatClient)));
		Assert.Same(chatClient, client.GetService(typeof(RecordingChatClient)));
		Assert.Empty(chatClient.ServiceRequests);
	}

	[Fact]
	public void GetService_UnknownAndKeyedRequestsDelegateExactlyToInnerClient()
	{
		var unknownService = new DelegatedService();
		var keyedMetadata = new ImageClassificationClientMetadata("keyed-provider");
		var serviceKey = new object();
		var chatClient = new RecordingChatClient
		{
			ServiceFactory = (serviceType, key) =>
				serviceType == typeof(DelegatedService) && key is null
					? unknownService
					: serviceType == typeof(ImageClassificationClientMetadata) &&
						ReferenceEquals(key, serviceKey)
						? keyedMetadata
						: null
		};
		using var client = new ChatClientImageClassificationClient(
			chatClient,
			["cat"],
			new ImageClassificationClientMetadata("supplied-provider"));

		DelegatedService? actualUnknown = client.GetService<DelegatedService>();
		object? actualUnknownUntyped = client.GetService(typeof(DelegatedService));
		ImageClassificationClientMetadata? actualKeyedTyped =
			client.GetService<ImageClassificationClientMetadata>(serviceKey);
		object? actualKeyed = client.GetService(
			typeof(ImageClassificationClientMetadata),
			serviceKey);

		Assert.Same(unknownService, actualUnknown);
		Assert.Same(unknownService, actualUnknownUntyped);
		Assert.Same(keyedMetadata, actualKeyedTyped);
		Assert.Same(keyedMetadata, actualKeyed);
		Assert.Collection(
			chatClient.ServiceRequests,
			request =>
			{
				Assert.Equal(typeof(DelegatedService), request.ServiceType);
				Assert.Null(request.ServiceKey);
			},
			request =>
			{
				Assert.Equal(typeof(DelegatedService), request.ServiceType);
				Assert.Null(request.ServiceKey);
			},
			request =>
			{
				Assert.Equal(typeof(ImageClassificationClientMetadata), request.ServiceType);
				Assert.Same(serviceKey, request.ServiceKey);
			},
			request =>
			{
				Assert.Equal(typeof(ImageClassificationClientMetadata), request.ServiceType);
				Assert.Same(serviceKey, request.ServiceKey);
			});
	}

	[Fact]
	public async Task Dispose_DoesNotDisposeInnerClientAndInnerRemainsCallable()
	{
		ChatResponse response = CreateResponse("""{"labels":["cat"]}""");
		var chatClient = new RecordingChatClient(response);
		var client = new ChatClientImageClassificationClient(chatClient, ["cat"]);

		client.Dispose();
		ChatResponse actualResponse = await chatClient.GetResponseAsync([]);

		Assert.False(chatClient.IsDisposed);
		Assert.Same(response, actualResponse);
		Assert.Equal(1, chatClient.CallCount);
	}

	private static async Task<ImageClassificationResult> ClassifyAsync(
		ChatClientImageClassificationClient client,
		ImageClassificationOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		using var imageStream = new MemoryStream([1, 2, 3]);
		return await client.ClassifyImageAsync(
			imageStream,
			"image/png",
			options,
			cancellationToken);
	}

	private static ChatResponse CreateResponse(
		string json,
		string? modelId = null,
		AdditionalPropertiesDictionary? additionalProperties = null) =>
		new(new ChatMessage(ChatRole.Assistant, json))
		{
			ModelId = modelId,
			AdditionalProperties = additionalProperties
		};

	private sealed class RecordingChatClient : IChatClient
	{
		public RecordingChatClient(
			ChatResponse? response = null,
			ChatClientMetadata? metadata = null)
		{
			Response = response ?? CreateResponse("""{"labels":[]}""");
			Metadata = metadata;
		}

		public ChatResponse Response { get; set; }

		public Task<ChatResponse>? ResponseTask { get; init; }

		public ChatClientMetadata? Metadata { get; }

		public int CallCount { get; private set; }

		public IReadOnlyList<ChatMessage> Messages { get; private set; } = [];

		public ChatOptions? Options { get; private set; }

		public CancellationToken CancellationToken { get; private set; }

		public bool IsDisposed { get; private set; }

		public Func<Type, object?, object?>? ServiceFactory { get; init; }

		public List<(Type ServiceType, object? ServiceKey)> ServiceRequests { get; } = [];

		public Task<ChatResponse> GetResponseAsync(
			IEnumerable<ChatMessage> messages,
			ChatOptions? options = null,
			CancellationToken cancellationToken = default)
		{
			if (IsDisposed)
			{
				throw new ObjectDisposedException(nameof(RecordingChatClient));
			}

			CallCount++;
			Messages = messages.ToArray();
			Options = options;
			CancellationToken = cancellationToken;
			return ResponseTask ?? Task.FromResult(Response);
		}

		public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
			IEnumerable<ChatMessage> messages,
			ChatOptions? options = null,
			[EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			await Task.CompletedTask;
			yield break;
		}

		public object? GetService(Type serviceType, object? serviceKey = null)
		{
			ServiceRequests.Add((serviceType, serviceKey));
			return ServiceFactory?.Invoke(serviceType, serviceKey) ??
				(serviceKey is null && serviceType == typeof(ChatClientMetadata) ? Metadata : null);
		}

		public void Dispose() => IsDisposed = true;
	}

	private sealed class DelegatedService
	{
	}

	private sealed class UnreadableStream : Stream
	{
		public override bool CanRead => false;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override void Flush() => throw new NotSupportedException();

		public override int Read(byte[] buffer, int offset, int count) =>
			throw new NotSupportedException();

		public override long Seek(long offset, SeekOrigin origin) =>
			throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) =>
			throw new NotSupportedException();
	}

	private class NonSeekableReadStream(byte[] bytes) : Stream
	{
		private int _position;

		public int BytesRead { get; private set; }

		public bool IsDisposed { get; private set; }

		public override bool CanRead => !IsDisposed;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			int bytesRead = Math.Min(count, bytes.Length - _position);
			bytes.AsSpan(_position, bytesRead).CopyTo(buffer.AsSpan(offset, bytesRead));
			_position += bytesRead;
			BytesRead += bytesRead;
			return bytesRead;
		}

		public override Task<int> ReadAsync(
			byte[] buffer,
			int offset,
			int count,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(Read(buffer, offset, count));
		}

		protected override void Dispose(bool disposing)
		{
			IsDisposed = true;
			base.Dispose(disposing);
		}

		public override void Flush() => throw new NotSupportedException();

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}

	private sealed class CancellableChunkedStream : NonSeekableReadStream
	{
		private int _readCount;

		public CancellableChunkedStream()
			: base([1])
		{
		}

		public Task FirstReadCompleted => _firstReadCompleted.Task;

		private readonly TaskCompletionSource _firstReadCompleted =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public override async Task<int> ReadAsync(
			byte[] buffer,
			int offset,
			int count,
			CancellationToken cancellationToken)
		{
			if (_readCount++ == 0)
			{
				int bytesRead = await base.ReadAsync(buffer, offset, Math.Min(count, 1), cancellationToken);
				_firstReadCompleted.SetResult();
				return bytesRead;
			}

			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			return 0;
		}
	}

	private sealed class PendingReadStream(byte[] bytes) : NonSeekableReadStream(bytes)
	{
		private readonly TaskCompletionSource _readStarted =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _releaseRead =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task ReadStarted => _readStarted.Task;

		public void ReleaseRead() => _releaseRead.TrySetResult();

		public override async Task<int> ReadAsync(
			byte[] buffer,
			int offset,
			int count,
			CancellationToken cancellationToken)
		{
			_readStarted.TrySetResult();
			await _releaseRead.Task.WaitAsync(cancellationToken);
			return await base.ReadAsync(buffer, offset, count, cancellationToken);
		}
	}
}
