// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Azure;
using Azure.AI.ContentUnderstanding;
using Azure.Core;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;
using Microsoft.Maui.Essentials.AI.ImageClassification.AzureContentUnderstanding;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.ImageClassification.AzureContentUnderstanding.Tests;

public class AzureContentUnderstandingImageClassificationClientTests
{
	private const string AnalyzerId = "product-classifier-v1";
	private const long DefaultMaximumInputBytes = 20 * 1024 * 1024;

	[Fact]
	public void PublicApi_HasExactNamespaceAndSurface()
	{
		Type clientType = typeof(AzureContentUnderstandingImageClassificationClient);
		Type optionsType = typeof(AzureContentUnderstandingImageClassificationOptions);
		string expectedNamespace = typeof(AzureContentUnderstandingImageClassificationClient).Namespace!;

		Assert.Equal(
			[
				"Microsoft.Maui.Essentials.AI.ImageClassification.AzureContentUnderstanding.AzureContentUnderstandingImageClassificationClient",
				"Microsoft.Maui.Essentials.AI.ImageClassification.AzureContentUnderstanding.AzureContentUnderstandingImageClassificationOptions"
			],
			clientType.Assembly.GetExportedTypes().OrderBy(type => type.FullName).Select(type => type.FullName));
		Assert.Equal("Microsoft.Maui.Essentials.AI.ImageClassification.AzureContentUnderstanding", expectedNamespace);
		Assert.Equal(expectedNamespace, optionsType.Namespace);

		PropertyInfo analyzerProperty = Assert.Single(
			optionsType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
		Assert.Equal(nameof(AzureContentUnderstandingImageClassificationOptions.AnalyzerId), analyzerProperty.Name);
		Assert.Equal(typeof(string), analyzerProperty.PropertyType);
		Assert.NotNull(analyzerProperty.GetCustomAttribute<RequiredMemberAttribute>());
		Assert.Null(optionsType.GetProperty("ClassificationFieldName"));

		ConstructorInfo[] publicConstructors = clientType.GetConstructors();
		Assert.DoesNotContain(
			publicConstructors.SelectMany(constructor => constructor.GetParameters()),
			parameter => parameter.ParameterType == typeof(string) ||
				parameter.ParameterType == typeof(AzureKeyCredential));
	}

	[Fact]
	public void Constructor_HasExactPublicTokenCredentialShape()
	{
		ConstructorInfo constructor = Assert.Single(
			typeof(AzureContentUnderstandingImageClassificationClient).GetConstructors(
				BindingFlags.Public | BindingFlags.Instance));

		Assert.Collection(
			constructor.GetParameters(),
			parameter =>
			{
				Assert.Equal("endpoint", parameter.Name);
				Assert.Equal(typeof(Uri), parameter.ParameterType);
				Assert.False(parameter.IsOptional);
			},
			parameter =>
			{
				Assert.Equal("credential", parameter.Name);
				Assert.Equal(typeof(TokenCredential), parameter.ParameterType);
				Assert.False(parameter.IsOptional);
			},
			parameter =>
			{
				Assert.Equal("options", parameter.Name);
				Assert.Equal(typeof(AzureContentUnderstandingImageClassificationOptions), parameter.ParameterType);
				Assert.False(parameter.IsOptional);
			});
	}

	[Fact]
	public void Constructor_HasExpectedInternalInjectionShape()
	{
		ConstructorInfo constructor = Assert.Single(
			typeof(AzureContentUnderstandingImageClassificationClient)
				.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance),
			constructor => constructor.IsAssembly);

		Assert.Collection(
			constructor.GetParameters(),
			parameter =>
			{
				Assert.Equal("client", parameter.Name);
				Assert.Equal(typeof(ContentUnderstandingClient), parameter.ParameterType);
				Assert.False(parameter.IsOptional);
			},
			parameter =>
			{
				Assert.Equal("options", parameter.Name);
				Assert.Equal(typeof(AzureContentUnderstandingImageClassificationOptions), parameter.ParameterType);
				Assert.False(parameter.IsOptional);
			},
			parameter =>
			{
				Assert.Equal("metadata", parameter.Name);
				Assert.Equal(typeof(ImageClassificationClientMetadata), parameter.ParameterType);
				Assert.True(parameter.IsOptional);
			});
	}

	[Fact]
	public void Client_ImplementsIImageClassificationClient()
	{
		var sdkClient = new RecordingContentUnderstandingClient();
		using var client = CreateClient(sdkClient);

		IImageClassificationClient contract = Assert.IsAssignableFrom<IImageClassificationClient>(client);

		Assert.Same(client, contract);
		Assert.Same(sdkClient, client.GetService(typeof(ContentUnderstandingClient)));
	}

	[Theory]
	[InlineData("endpoint")]
	[InlineData("credential")]
	[InlineData("options")]
	public void Constructor_NullEndpointCredentialOrOptions_Throws(string nullParameter)
	{
		var endpoint = new Uri("https://content-understanding.example.test");
		var credential = new RecordingTokenCredential();
		AzureContentUnderstandingImageClassificationOptions options = CreateProviderOptions();

		ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
			new AzureContentUnderstandingImageClassificationClient(
				nullParameter == "endpoint" ? null! : endpoint,
				nullParameter == "credential" ? null! : credential,
				nullParameter == "options" ? null! : options));

		Assert.Equal(nullParameter, exception.ParamName);
		Assert.Equal(0, credential.TokenRequestCount);
		Assert.False(credential.IsDisposed);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" \t")]
	public void Constructor_NullEmptyOrWhitespaceAnalyzerId_Throws(string? analyzerId)
	{
		var credential = new RecordingTokenCredential();
		var options = new AzureContentUnderstandingImageClassificationOptions
		{
			AnalyzerId = analyzerId!
		};

		ArgumentException exception = Assert.Throws<ArgumentException>(() =>
			new AzureContentUnderstandingImageClassificationClient(
				new Uri("https://content-understanding.example.test"),
				credential,
				options));

		Assert.Equal("options", exception.ParamName);
		Assert.Contains("analyzer identifier must not be empty", exception.Message, StringComparison.Ordinal);
		Assert.Equal(0, credential.TokenRequestCount);
	}

	[Fact]
	public async Task Constructor_MutatedOptions_UsesConstructionSnapshot()
	{
		var sdkClient = new RecordingContentUnderstandingClient();
		AzureContentUnderstandingImageClassificationOptions providerOptions = CreateProviderOptions();
		using var client = new AzureContentUnderstandingImageClassificationClient(sdkClient, providerOptions);
		providerOptions.AnalyzerId = "mutated-analyzer";

		ImageClassificationResult result = await ClassifyAsync(client);

		AnalyzeCall call = Assert.Single(sdkClient.Calls);
		ImageClassificationClientMetadata metadata = Assert.IsType<ImageClassificationClientMetadata>(
			client.GetService(typeof(ImageClassificationClientMetadata)));
		Assert.Equal(AnalyzerId, call.AnalyzerId);
		Assert.Equal(AnalyzerId, result.ModelId);
		Assert.Equal(AnalyzerId, metadata.DefaultModelId);
		Assert.NotEqual(providerOptions.AnalyzerId, call.AnalyzerId);
	}

	[Fact]
	public void GetService_ExposesUnderlyingClientMetadataAndSelf()
	{
		var sdkClient = new RecordingContentUnderstandingClient();
		var suppliedMetadata = new ImageClassificationClientMetadata(
			"supplied provider",
			new Uri("https://injected.example.test"),
			AnalyzerId);
		using var injectedClient = new AzureContentUnderstandingImageClassificationClient(
			sdkClient,
			CreateProviderOptions(),
			suppliedMetadata);

		Assert.Same(sdkClient, injectedClient.GetService(typeof(ContentUnderstandingClient)));
		Assert.Same(suppliedMetadata, injectedClient.GetService(typeof(ImageClassificationClientMetadata)));
		Assert.Same(injectedClient, injectedClient.GetService(typeof(AzureContentUnderstandingImageClassificationClient)));
		Assert.Same(injectedClient, injectedClient.GetService(typeof(IImageClassificationClient)));

		var endpoint = new Uri("https://content-understanding.example.test");
		using var publicClient = new AzureContentUnderstandingImageClassificationClient(
			endpoint,
			new RecordingTokenCredential(),
			CreateProviderOptions());
		ImageClassificationClientMetadata generatedMetadata = Assert.IsType<ImageClassificationClientMetadata>(
			publicClient.GetService(typeof(ImageClassificationClientMetadata)));
		object underlyingClient = Assert.IsType<ContentUnderstandingClient>(
			publicClient.GetService(typeof(ContentUnderstandingClient)));

		Assert.Same(underlyingClient, publicClient.GetService(typeof(ContentUnderstandingClient)));
		Assert.Equal("Azure Content Understanding", generatedMetadata.ProviderName);
		Assert.Equal(endpoint, generatedMetadata.ProviderUri);
		Assert.Equal(AnalyzerId, generatedMetadata.DefaultModelId);
	}

	[Fact]
	public void GetService_KeyedOrUnknown_ReturnsNull()
	{
		var sdkClient = new RecordingContentUnderstandingClient();
		using var client = CreateClient(sdkClient);

		Assert.Null(client.GetService(typeof(ContentUnderstandingClient), "key"));
		Assert.Null(client.GetService(typeof(ImageClassificationClientMetadata), new object()));
		Assert.Null(client.GetService(typeof(UnknownService)));
		Assert.Equal(0, sdkClient.CallCount);
	}

	[Fact]
	public void Dispose_DoesNotDisposeCredentialOrInjectedClient()
	{
		var credential = new RecordingTokenCredential();
		var publicClient = new AzureContentUnderstandingImageClassificationClient(
			new Uri("https://content-understanding.example.test"),
			credential,
			CreateProviderOptions());
		var sdkClient = new RecordingContentUnderstandingClient();
		var injectedClient = CreateClient(sdkClient);

		publicClient.Dispose();
		publicClient.Dispose();
		injectedClient.Dispose();
		injectedClient.Dispose();

		Assert.False(credential.IsDisposed);
		Assert.False(sdkClient.IsDisposed);
		Assert.Equal(0, credential.TokenRequestCount);
		Assert.Equal(0, sdkClient.CallCount);
	}

	[Fact]
	public async Task ClassifyImageAsync_NullOptions_UsesContractDefaults()
	{
		byte[] imageBytes = new byte[DefaultMaximumInputBytes];
		imageBytes[0] = 0x89;
		imageBytes[^1] = 0x82;
		var sdkClient = new RecordingContentUnderstandingClient(
			CreateAnalysisResult("default-category"));
		using var client = CreateClient(sdkClient);
		using var image = new TrackingSeekableStream(imageBytes);

		ImageClassificationResult result = await client.ClassifyImageAsync(image, "image/png", options: null);

		AnalyzeCall call = Assert.Single(sdkClient.Calls);
		Assert.Equal(DefaultMaximumInputBytes, call.Bytes.LongLength);
		Assert.Equal(0x89, call.Bytes[0]);
		Assert.Equal(0x82, call.Bytes[^1]);
		Assert.Equal("default-category", Assert.Single(result.Predictions).Label);
		Assert.Null(result.Predictions[0].Confidence);
		Assert.False(image.IsDisposed);
	}

	[Fact]
	public async Task ClassifyImageAsync_MutatedPendingOptions_UsesEntrySnapshot()
	{
		var sdkClient = new RecordingContentUnderstandingClient(
			CreateAnalysisResult("snapshot-category"));
		using var client = CreateClient(sdkClient);
		var options = new ImageClassificationOptions
		{
			MaximumInputBytes = 3,
			MaximumPredictions = 1,
			MinimumConfidence = null
		};
		using var image = new PendingReadStream([1, 2, 3]);

		Task<ImageClassificationResult> classification =
			client.ClassifyImageAsync(image, "image/png", options);
		await image.ReadStarted;
		Assert.Equal(0, sdkClient.CallCount);

		options.MaximumInputBytes = 1;
		options.MaximumPredictions = 3;
		options.MinimumConfidence = 0.5f;
		image.ReleaseRead();

		ImageClassificationResult result = await classification;

		Assert.Equal([1, 2, 3], Assert.Single(sdkClient.Calls).Bytes);
		Assert.Equal("snapshot-category", Assert.Single(result.Predictions).Label);
		Assert.Null(result.Predictions[0].Confidence);
		Assert.Equal(3, image.BytesRead);
		Assert.Equal(1, sdkClient.CallCount);
		Assert.Equal(3, options.MaximumPredictions);
	}

	[Theory]
	[InlineData(0f)]
	[InlineData(0.42f)]
	[InlineData(1f)]
	public async Task ClassifyImageAsync_AnyMinimumConfidence_ThrowsBeforeReadOrSdk(float minimumConfidence)
	{
		var sdkClient = new RecordingContentUnderstandingClient();
		using var client = CreateClient(sdkClient);
		using var image = new TrackingSeekableStream([1, 2, 3], initialPosition: 1);
		var options = new ImageClassificationOptions { MinimumConfidence = minimumConfidence };

		NotSupportedException exception = await Assert.ThrowsAsync<NotSupportedException>(
			() => client.ClassifyImageAsync(image, "image/png", options));

		Assert.Equal(
			"MinimumConfidence is not supported because Azure Content Understanding whole-image classifier categories do not include confidence values.",
			exception.Message);
		Assert.Equal(1, image.Position);
		Assert.Equal(0, image.ReadCalls);
		Assert.Equal(0, sdkClient.CallCount);
	}

	[Theory]
	[InlineData("null")]
	[InlineData("unreadable")]
	[InlineData("empty")]
	public async Task ClassifyImageAsync_NullUnreadableOrEmptyStream_RejectsBeforeSdk(string streamCase)
	{
		var sdkClient = new RecordingContentUnderstandingClient();
		using var client = CreateClient(sdkClient);
		Stream? image = streamCase switch
		{
			"null" => null,
			"unreadable" => new UnreadableStream(),
			_ => new TrackingSeekableStream([])
		};

		ArgumentException exception = await Assert.ThrowsAnyAsync<ArgumentException>(
			() => client.ClassifyImageAsync(image!, "image/png"));

		if (streamCase == "null")
		{
			Assert.IsType<ArgumentNullException>(exception);
		}
		else
		{
			Assert.IsType<ArgumentException>(exception);
			Assert.False(((DisposalObservingStream)image!).IsDisposed);
		}

		Assert.Equal("image", exception.ParamName);
		Assert.Equal(0, sdkClient.CallCount);
		if (image is TrackingSeekableStream empty)
		{
			Assert.Equal(1, empty.ReadCalls);
		}

		image?.Dispose();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("not a media type")]
	[InlineData("text/plain")]
	public async Task ClassifyImageAsync_NullMalformedOrNonImageMediaType_RejectsBeforeReadOrSdk(
		string? mediaType)
	{
		var sdkClient = new RecordingContentUnderstandingClient();
		using var client = CreateClient(sdkClient);
		using var image = new TrackingSeekableStream([1, 2, 3]);

		ArgumentException exception = await Assert.ThrowsAnyAsync<ArgumentException>(
			() => client.ClassifyImageAsync(image, mediaType!));

		if (mediaType is null)
		{
			Assert.IsType<ArgumentNullException>(exception);
		}
		else
		{
			Assert.IsType<ArgumentException>(exception);
			Assert.Equal("The media type must identify image content. (Parameter 'imageMediaType')", exception.Message);
		}

		Assert.Equal("imageMediaType", exception.ParamName);
		Assert.Equal(0, image.ReadCalls);
		Assert.Equal(0, sdkClient.CallCount);
		Assert.False(image.IsDisposed);
	}

	[Theory]
	[InlineData("image/gif")]
	[InlineData("image/svg+xml")]
	[InlineData("image/webp")]
	public async Task ClassifyImageAsync_UnsupportedImageMediaType_ThrowsBeforeReadOrSdk(string mediaType)
	{
		var sdkClient = new RecordingContentUnderstandingClient();
		using var client = CreateClient(sdkClient);
		using var image = new TrackingSeekableStream([1, 2, 3]);

		NotSupportedException exception = await Assert.ThrowsAsync<NotSupportedException>(
			() => client.ClassifyImageAsync(image, mediaType));

		Assert.Contains(mediaType, exception.Message, StringComparison.Ordinal);
		Assert.Equal(0, image.ReadCalls);
		Assert.Equal(0, sdkClient.CallCount);
		Assert.False(image.IsDisposed);
	}

	[Fact]
	public async Task ClassifyImageAsync_ExactMaximumInputBytes_Succeeds()
	{
		var sdkClient = new RecordingContentUnderstandingClient(
			CreateAnalysisResult("exact-limit"));
		using var client = CreateClient(sdkClient);
		using var image = new TrackingSeekableStream([9, 8, 7, 6, 5], initialPosition: 2);
		var options = new ImageClassificationOptions { MaximumInputBytes = 3 };

		ImageClassificationResult result =
			await client.ClassifyImageAsync(image, "image/png", options);

		AnalyzeCall call = Assert.Single(sdkClient.Calls);
		Assert.Equal([7, 6, 5], call.Bytes);
		Assert.Equal("image/png", call.ContentType);
		Assert.Equal("exact-limit", Assert.Single(result.Predictions).Label);
		Assert.Equal(5, image.Position);
		Assert.False(image.IsDisposed);
	}

	[Fact]
	public async Task ClassifyImageAsync_SeekableOverLimit_RejectsWithoutRead()
	{
		var sdkClient = new RecordingContentUnderstandingClient();
		using var client = CreateClient(sdkClient);
		using var image = new TrackingSeekableStream([9, 8, 7, 6, 5, 4], initialPosition: 2);
		var options = new ImageClassificationOptions { MaximumInputBytes = 3 };

		ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
			() => client.ClassifyImageAsync(image, "image/png", options));

		Assert.Equal("image", exception.ParamName);
		Assert.Contains("configured maximum of 3 bytes", exception.Message, StringComparison.Ordinal);
		Assert.Contains(nameof(ImageClassificationOptions.MaximumInputBytes), exception.Message, StringComparison.Ordinal);
		Assert.Equal(0, image.ReadCalls);
		Assert.Equal(2, image.Position);
		Assert.False(image.IsDisposed);
		Assert.Equal(0, sdkClient.CallCount);
	}

	[Fact]
	public async Task ClassifyImageAsync_NonSeekableOverLimit_ReadsAtMostLimitPlusOne()
	{
		var sdkClient = new RecordingContentUnderstandingClient();
		using var client = CreateClient(sdkClient);
		using var image = new ChunkedNonSeekableStream([1, 2, 3, 4, 5, 6], maximumChunkSize: 2);
		var options = new ImageClassificationOptions { MaximumInputBytes = 3 };

		ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
			() => client.ClassifyImageAsync(image, "image/png", options));

		Assert.Equal("image", exception.ParamName);
		Assert.Equal(4, image.BytesRead);
		Assert.InRange(image.BytesRead, 0, 4);
		Assert.Equal(2, image.ReadCalls);
		Assert.False(image.IsDisposed);
		Assert.Equal(0, sdkClient.CallCount);
	}

	[Fact]
	public async Task ClassifyImageAsync_CancellationDuringRead_StopsBeforeSdk()
	{
		var sdkClient = new RecordingContentUnderstandingClient();
		using var client = CreateClient(sdkClient);
		using var image = new CancellationAwareStream();
		using var cancellationSource = new CancellationTokenSource();

		Task<ImageClassificationResult> classification = client.ClassifyImageAsync(
			image,
			"image/png",
			new ImageClassificationOptions { MaximumInputBytes = 10 },
			cancellationSource.Token);
		await image.SecondReadStarted;

		cancellationSource.Cancel();

		OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => classification);
		Assert.Equal(cancellationSource.Token, exception.CancellationToken);
		Assert.Equal(2, image.ReadCalls);
		Assert.Equal(1, image.BytesRead);
		Assert.False(image.IsDisposed);
		Assert.Equal(0, sdkClient.CallCount);
	}

	[Fact]
	public async Task ClassifyImageAsync_ValidImage_InvokesTypedAnalyzeBinaryAsyncWithExactArguments()
	{
		var sdkClient = new RecordingContentUnderstandingClient(
			CreateAnalysisResult("wildlife"));
		using var client = CreateClient(sdkClient);
		using var image = new TrackingSeekableStream([0, 1, 2, 3, 4], initialPosition: 2);
		using var cancellationSource = new CancellationTokenSource();

		ImageClassificationResult result = await client.ClassifyImageAsync(
			image,
			"image/jpeg",
			cancellationToken: cancellationSource.Token);

		AnalyzeCall call = Assert.Single(sdkClient.Calls);
		Assert.Equal(WaitUntil.Completed, call.WaitUntil);
		Assert.Equal(AnalyzerId, call.AnalyzerId);
		Assert.Equal([2, 3, 4], call.Bytes);
		Assert.Null(call.ContentRange);
		Assert.Equal("image/jpeg", call.ContentType);
		Assert.Null(call.ProcessingLocation);
		Assert.Equal(cancellationSource.Token, call.CancellationToken);
		Assert.Equal("wildlife", Assert.Single(result.Predictions).Label);
	}

	[Fact]
	public async Task ClassifyImageAsync_ExactlyOneContentWithCategory_MapsOneNullConfidencePrediction()
	{
		var misleadingFields = new Dictionary<string, ContentField>
		{
			["classification"] = ContentUnderstandingModelFactory.ContentStringField(
				value: "field-category",
				confidence: 0.99f)
		};
		AnalysisContent content = ContentUnderstandingModelFactory.AnalysisContent(
			kind: "document",
			mimeType: "image/png",
			analyzerId: "sdk-analyzer",
			category: "content-category",
			fields: misleadingFields);
		AnalysisResult analysisResult = ContentUnderstandingModelFactory.AnalysisResult(
			analyzerId: "sdk-analyzer",
			contents: [content]);
		var sdkClient = new RecordingContentUnderstandingClient(analysisResult);
		using var client = CreateClient(sdkClient);

		ImageClassificationResult result = await ClassifyAsync(client);

		ImageClassificationPrediction prediction = Assert.Single(result.Predictions);
		Assert.Equal("content-category", prediction.Label);
		Assert.Null(prediction.Confidence);
		Assert.NotEqual("field-category", prediction.Label);
		Assert.Same(analysisResult, result.RawRepresentation);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(2)]
	public async Task ClassifyImageAsync_ZeroOrMultipleContents_Throws(int contentCount)
	{
		AnalysisContent[] contents = Enumerable.Range(0, contentCount)
			.Select(index => CreateAnalysisContent($"category-{index}"))
			.ToArray();
		var sdkClient = new RecordingContentUnderstandingClient(
			ContentUnderstandingModelFactory.AnalysisResult(
				analyzerId: "sdk-analyzer",
				contents: contents));
		using var client = CreateClient(sdkClient);

		InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => ClassifyAsync(client));

		Assert.Equal(
			$"Azure Content Understanding analyzer '{AnalyzerId}' returned {contentCount} content results; exactly one is required for whole-image classification.",
			exception.Message);
		Assert.Equal(1, sdkClient.CallCount);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" \t")]
	public async Task ClassifyImageAsync_NullEmptyOrWhitespaceCategory_Throws(string? category)
	{
		var sdkClient = new RecordingContentUnderstandingClient(
			CreateAnalysisResult(category));
		using var client = CreateClient(sdkClient);

		InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => ClassifyAsync(client));

		Assert.Equal(
			$"Azure Content Understanding analyzer '{AnalyzerId}' did not return a non-empty content-level category.",
			exception.Message);
		Assert.Equal(1, sdkClient.CallCount);
	}

	[Fact]
	public async Task ClassifyImageAsync_MaximumPredictionsOne_ReturnsSinglePrediction()
	{
		var sdkClient = new RecordingContentUnderstandingClient(
			CreateAnalysisResult("single-category"));
		using var client = CreateClient(sdkClient);
		var options = new ImageClassificationOptions { MaximumPredictions = 1 };

		ImageClassificationResult result = await ClassifyAsync(client, options);

		ImageClassificationPrediction prediction = Assert.Single(result.Predictions);
		Assert.Equal("single-category", prediction.Label);
		Assert.Null(prediction.Confidence);
		Assert.Equal(1, sdkClient.CallCount);
	}

	[Fact]
	public async Task ClassifyImageAsync_ResultPreservesRawAnalysisResult()
	{
		AnalysisResult analysisResult = CreateAnalysisResult("raw-category");
		var sdkClient = new RecordingContentUnderstandingClient(analysisResult);
		using var client = CreateClient(sdkClient);

		ImageClassificationResult result = await ClassifyAsync(client);

		Assert.Same(analysisResult, result.RawRepresentation);
		Assert.Equal("raw-category", Assert.Single(result.Predictions).Label);
		Assert.Equal(AnalyzerId, result.ModelId);
	}

	[Fact]
	public async Task ClassifyImageAsync_ResultUsesConfiguredAnalyzerIdAndSafeAdditionalProperties()
	{
		AnalysisResult analysisResult = CreateAnalysisResult("category", sdkAnalyzerId: "different-sdk-analyzer");
		var sdkClient = new RecordingContentUnderstandingClient(analysisResult);
		using var client = CreateClient(sdkClient);

		ImageClassificationResult first = await ClassifyAsync(client);
		ImageClassificationResult second = await ClassifyAsync(client);

		AdditionalPropertiesDictionary firstProperties =
			Assert.IsType<AdditionalPropertiesDictionary>(first.AdditionalProperties);
		AdditionalPropertiesDictionary secondProperties =
			Assert.IsType<AdditionalPropertiesDictionary>(second.AdditionalProperties);
		KeyValuePair<string, object?> property = Assert.Single(firstProperties);
		Assert.Equal("analyzerId", property.Key);
		Assert.IsType<string>(property.Value);
		Assert.Equal(AnalyzerId, property.Value);
		Assert.Equal(AnalyzerId, first.ModelId);
		Assert.Equal(AnalyzerId, second.ModelId);
		Assert.NotEqual(analysisResult.AnalyzerId, first.ModelId);
		Assert.NotSame(firstProperties, secondProperties);
		Assert.Equal(AnalyzerId, secondProperties["analyzerId"]);
	}

	[Fact]
	public async Task ClassifyImageAsync_AuthenticationFailure_PropagatesUnchanged()
	{
		var failure = new RequestFailedException(
			status: 401,
			message: "authentication failed",
			errorCode: "AuthenticationFailed",
			innerException: null);
		var sdkClient = new RecordingContentUnderstandingClient
		{
			Handler = _ => Task.FromException<Operation<AnalysisResult>>(failure)
		};
		using var client = CreateClient(sdkClient);

		RequestFailedException actual = await Assert.ThrowsAsync<RequestFailedException>(
			() => ClassifyAsync(client));

		Assert.Same(failure, actual);
		Assert.Equal(401, actual.Status);
		Assert.Equal("AuthenticationFailed", actual.ErrorCode);
		Assert.Equal("authentication failed", actual.Message);
		Assert.Equal(1, sdkClient.CallCount);
	}

	[Fact]
	public async Task ClassifyImageAsync_ServiceFailure_PropagatesUnchanged()
	{
		var failure = new RequestFailedException(
			status: 503,
			message: "service unavailable",
			errorCode: "ServiceUnavailable",
			innerException: null);
		var sdkClient = new RecordingContentUnderstandingClient
		{
			Handler = _ => Task.FromResult<Operation<AnalysisResult>>(new CompletedOperation(failure))
		};
		using var client = CreateClient(sdkClient);

		RequestFailedException actual = await Assert.ThrowsAsync<RequestFailedException>(
			() => ClassifyAsync(client));

		Assert.Same(failure, actual);
		Assert.Equal(503, actual.Status);
		Assert.Equal("ServiceUnavailable", actual.ErrorCode);
		Assert.Equal(1, sdkClient.CallCount);
	}

	[Fact]
	public async Task ClassifyImageAsync_SdkCancellation_PropagatesUnchanged()
	{
		using var cancellationSource = new CancellationTokenSource();
		var failure = new OperationCanceledException(
			"sdk operation canceled",
			innerException: null,
			cancellationSource.Token);
		var sdkClient = new RecordingContentUnderstandingClient
		{
			Handler = _ => Task.FromResult<Operation<AnalysisResult>>(new CompletedOperation(failure))
		};
		using var client = CreateClient(sdkClient);

		OperationCanceledException actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => ClassifyAsync(client, cancellationToken: cancellationSource.Token));

		Assert.Same(failure, actual);
		Assert.Equal(cancellationSource.Token, actual.CancellationToken);
		Assert.Equal(cancellationSource.Token, Assert.Single(sdkClient.Calls).CancellationToken);
	}

	[Fact]
	public async Task ClassifyImageAsync_SuccessFailureAndCancellation_LeaveInputOpen()
	{
		using var successfulStream = new TrackingSeekableStream([1, 2]);
		var successSdkClient = new RecordingContentUnderstandingClient(CreateAnalysisResult("success"));
		using var successClient = CreateClient(successSdkClient);
		ImageClassificationResult success =
			await successClient.ClassifyImageAsync(successfulStream, "image/png");

		using var oversizedStream = new TrackingSeekableStream([1, 2, 3]);
		var sizeSdkClient = new RecordingContentUnderstandingClient();
		using var sizeClient = CreateClient(sizeSdkClient);
		await Assert.ThrowsAsync<ArgumentException>(() =>
			sizeClient.ClassifyImageAsync(
				oversizedStream,
				"image/png",
				new ImageClassificationOptions { MaximumInputBytes = 2 }));

		var sdkFailure = new RequestFailedException(500, "sdk failure");
		using var sdkFailureStream = new TrackingSeekableStream([4, 5]);
		var failureSdkClient = new RecordingContentUnderstandingClient
		{
			Handler = _ => Task.FromException<Operation<AnalysisResult>>(sdkFailure)
		};
		using var failureClient = CreateClient(failureSdkClient);
		RequestFailedException actualFailure = await Assert.ThrowsAsync<RequestFailedException>(
			() => failureClient.ClassifyImageAsync(sdkFailureStream, "image/jpeg"));

		using var canceledStream = new CancellationAwareStream();
		var cancellationSdkClient = new RecordingContentUnderstandingClient();
		using var cancellationClient = CreateClient(cancellationSdkClient);
		using var cancellationSource = new CancellationTokenSource();
		Task<ImageClassificationResult> canceledClassification = cancellationClient.ClassifyImageAsync(
			canceledStream,
			"image/png",
			new ImageClassificationOptions { MaximumInputBytes = 10 },
			cancellationSource.Token);
		await canceledStream.SecondReadStarted;
		cancellationSource.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledClassification);

		Assert.Equal("success", Assert.Single(success.Predictions).Label);
		Assert.Same(sdkFailure, actualFailure);
		Assert.All(
			new Stream[] { successfulStream, oversizedStream, sdkFailureStream, canceledStream },
			stream => Assert.True(stream.CanRead));
		successfulStream.Position = 0;
		Assert.Equal(1, successfulStream.ReadByte());
		Assert.Equal(0, sizeSdkClient.CallCount);
		Assert.Equal(0, cancellationSdkClient.CallCount);
	}

	[Fact]
	public async Task ClassifyImageAsync_RepeatedCalls_DoNotLeakState()
	{
		var sdkClient = new RecordingContentUnderstandingClient
		{
			Handler = call => Task.FromResult<Operation<AnalysisResult>>(
				new CompletedOperation(CreateAnalysisResult(
					call.Bytes[0] == 1 ? "first-category" : "second-category",
					sdkAnalyzerId: call.Bytes[0] == 1 ? "sdk-one" : "sdk-two")))
		};
		using var client = CreateClient(sdkClient);
		using var firstTokenSource = new CancellationTokenSource();
		using var secondTokenSource = new CancellationTokenSource();
		using var firstStream = new TrackingSeekableStream([0, 1, 2], initialPosition: 1);
		using var secondStream = new TrackingSeekableStream([9, 8, 7, 6], initialPosition: 1);

		ImageClassificationResult first = await client.ClassifyImageAsync(
			firstStream,
			"image/png",
			new ImageClassificationOptions { MaximumInputBytes = 2, MaximumPredictions = 1 },
			firstTokenSource.Token);
		ImageClassificationResult second = await client.ClassifyImageAsync(
			secondStream,
			"image/jpeg",
			new ImageClassificationOptions { MaximumInputBytes = 3 },
			secondTokenSource.Token);

		Assert.Collection(
			sdkClient.Calls,
			call =>
			{
				Assert.Equal([1, 2], call.Bytes);
				Assert.Equal("image/png", call.ContentType);
				Assert.Equal(firstTokenSource.Token, call.CancellationToken);
			},
			call =>
			{
				Assert.Equal([8, 7, 6], call.Bytes);
				Assert.Equal("image/jpeg", call.ContentType);
				Assert.Equal(secondTokenSource.Token, call.CancellationToken);
			});
		Assert.Equal("first-category", Assert.Single(first.Predictions).Label);
		Assert.Equal("second-category", Assert.Single(second.Predictions).Label);
		Assert.NotSame(first.RawRepresentation, second.RawRepresentation);
		Assert.Equal(AnalyzerId, first.ModelId);
		Assert.Equal(AnalyzerId, second.ModelId);
	}

	[Fact]
	public async Task ClassifyImageAsync_ConcurrentCalls_KeepBytesOptionsResultsAndTokensIsolated()
	{
		var firstCompletion = new TaskCompletionSource<Operation<AnalysisResult>>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		var secondCompletion = new TaskCompletionSource<Operation<AnalysisResult>>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		var sdkClient = new RecordingContentUnderstandingClient
		{
			Handler = call => call.Bytes[0] == 1 ? firstCompletion.Task : secondCompletion.Task
		};
		using var client = CreateClient(sdkClient);
		using var firstTokenSource = new CancellationTokenSource();
		using var secondTokenSource = new CancellationTokenSource();
		using var firstStream = new TrackingSeekableStream([1, 2]);
		using var secondStream = new TrackingSeekableStream([9, 8, 7]);

		Task<ImageClassificationResult> firstTask = client.ClassifyImageAsync(
			firstStream,
			"image/png",
			new ImageClassificationOptions { MaximumInputBytes = 2, MaximumPredictions = 1 },
			firstTokenSource.Token);
		Task<ImageClassificationResult> secondTask = client.ClassifyImageAsync(
			secondStream,
			"image/bmp",
			new ImageClassificationOptions { MaximumInputBytes = 3 },
			secondTokenSource.Token);
		await sdkClient.TwoCallsRecorded;

		AnalysisResult secondAnalysisResult = CreateAnalysisResult("second-category", "sdk-second");
		AnalysisResult firstAnalysisResult = CreateAnalysisResult("first-category", "sdk-first");
		secondCompletion.SetResult(new CompletedOperation(secondAnalysisResult));
		ImageClassificationResult second = await secondTask;
		Assert.False(firstTask.IsCompleted);
		firstCompletion.SetResult(new CompletedOperation(firstAnalysisResult));
		ImageClassificationResult first = await firstTask;

		Dictionary<byte, AnalyzeCall> calls = sdkClient.Calls.ToDictionary(call => call.Bytes[0]);
		Assert.Equal([1, 2], calls[1].Bytes);
		Assert.Equal("image/png", calls[1].ContentType);
		Assert.Equal(firstTokenSource.Token, calls[1].CancellationToken);
		Assert.Equal([9, 8, 7], calls[9].Bytes);
		Assert.Equal("image/bmp", calls[9].ContentType);
		Assert.Equal(secondTokenSource.Token, calls[9].CancellationToken);
		Assert.Equal("first-category", Assert.Single(first.Predictions).Label);
		Assert.Equal("second-category", Assert.Single(second.Predictions).Label);
		Assert.Same(firstAnalysisResult, first.RawRepresentation);
		Assert.Same(secondAnalysisResult, second.RawRepresentation);
		Assert.False(firstStream.IsDisposed);
		Assert.False(secondStream.IsDisposed);
	}

	[Theory]
	[InlineData("client")]
	[InlineData("options")]
	public void Constructor_InternalNullClientOrOptions_ThrowsExactParameter(string nullParameter)
	{
		var sdkClient = new RecordingContentUnderstandingClient();
		AzureContentUnderstandingImageClassificationOptions options = CreateProviderOptions();

		ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
			new AzureContentUnderstandingImageClassificationClient(
				nullParameter == "client" ? null! : sdkClient,
				nullParameter == "options" ? null! : options));

		Assert.Equal(nullParameter, exception.ParamName);
		Assert.Equal(0, sdkClient.CallCount);
		Assert.False(sdkClient.IsDisposed);
	}

	[Fact]
	public void GetService_NullServiceType_ThrowsBeforeKeyHandling()
	{
		var sdkClient = new RecordingContentUnderstandingClient();
		using var client = CreateClient(sdkClient);

		ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
			() => client.GetService(null!, new object()));

		Assert.Equal("serviceType", exception.ParamName);
		Assert.Equal(0, sdkClient.CallCount);
		Assert.False(sdkClient.IsDisposed);
	}

	[Fact]
	public async Task ClassifyImageAsync_UppercaseImageMediaType_IsAcceptedAndPreserved()
	{
		AnalysisResult analysisResult = CreateAnalysisResult("case-insensitive-category");
		var sdkClient = new RecordingContentUnderstandingClient(analysisResult);
		using var client = CreateClient(sdkClient);
		using var image = new TrackingSeekableStream([0x00, 0x10, 0x20, 0x30], initialPosition: 1);

		ImageClassificationResult result = await client.ClassifyImageAsync(image, "IMAGE/PNG");

		AnalyzeCall call = Assert.Single(sdkClient.Calls);
		Assert.Equal([0x10, 0x20, 0x30], call.Bytes);
		Assert.Equal("IMAGE/PNG", call.ContentType);
		Assert.Equal("case-insensitive-category", Assert.Single(result.Predictions).Label);
		Assert.Same(analysisResult, result.RawRepresentation);
		Assert.Equal(4, image.Position);
		Assert.False(image.IsDisposed);
	}

	private static AzureContentUnderstandingImageClassificationClient CreateClient(
		ContentUnderstandingClient sdkClient) =>
		new(sdkClient, CreateProviderOptions());

	private static AzureContentUnderstandingImageClassificationOptions CreateProviderOptions() =>
		new() { AnalyzerId = AnalyzerId };

	private static async Task<ImageClassificationResult> ClassifyAsync(
		AzureContentUnderstandingImageClassificationClient client,
		ImageClassificationOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		using var image = new MemoryStream([1, 2, 3]);
		return await client.ClassifyImageAsync(image, "image/png", options, cancellationToken);
	}

	private static AnalysisContent CreateAnalysisContent(string? category) =>
		ContentUnderstandingModelFactory.AnalysisContent(
			kind: "document",
			mimeType: "image/png",
			analyzerId: "sdk-analyzer",
			category: category);

	private static AnalysisResult CreateAnalysisResult(
		string? category = "category",
		string? sdkAnalyzerId = "sdk-analyzer") =>
		ContentUnderstandingModelFactory.AnalysisResult(
			analyzerId: sdkAnalyzerId,
			apiVersion: "2025-05-01-preview",
			createdAt: new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
			stringEncoding: "utf16",
			contents: [CreateAnalysisContent(category)]);

	private sealed record AnalyzeCall(
		WaitUntil WaitUntil,
		string AnalyzerId,
		byte[] Bytes,
		ContentRange? ContentRange,
		string? ContentType,
		ProcessingLocation? ProcessingLocation,
		CancellationToken CancellationToken);

	private sealed class RecordingContentUnderstandingClient : ContentUnderstandingClient, IDisposable
	{
		private readonly ConcurrentQueue<AnalyzeCall> _calls = new();
		private readonly TaskCompletionSource _twoCallsRecorded =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int _callCount;

		public RecordingContentUnderstandingClient(AnalysisResult? result = null)
		{
			AnalysisResult configuredResult = result ?? CreateAnalysisResult();
			Handler = _ => Task.FromResult<Operation<AnalysisResult>>(
				new CompletedOperation(configuredResult));
		}

		public Func<AnalyzeCall, Task<Operation<AnalysisResult>>> Handler { get; init; }

		public IReadOnlyList<AnalyzeCall> Calls => _calls.ToArray();

		public int CallCount => Volatile.Read(ref _callCount);

		public Task TwoCallsRecorded => _twoCallsRecorded.Task;

		public bool IsDisposed { get; private set; }

		public override Task<Operation<AnalysisResult>> AnalyzeBinaryAsync(
			WaitUntil waitUntil,
			string analyzerId,
			BinaryData binaryInput,
			ContentRange? contentRange = null,
			string? contentType = null,
			ProcessingLocation? processingLocation = null,
			CancellationToken cancellationToken = default)
		{
			var call = new AnalyzeCall(
				waitUntil,
				analyzerId,
				binaryInput.ToArray(),
				contentRange,
				contentType,
				processingLocation,
				cancellationToken);
			_calls.Enqueue(call);
			if (Interlocked.Increment(ref _callCount) >= 2)
			{
				_twoCallsRecorded.TrySetResult();
			}

			return Handler(call);
		}

		public void Dispose() => IsDisposed = true;
	}

	private sealed class CompletedOperation : Operation<AnalysisResult>
	{
		private readonly AnalysisResult? _value;
		private readonly Exception? _failure;
		private readonly Response _response = new TestResponse();

		public CompletedOperation(AnalysisResult value)
		{
			_value = value;
		}

		public CompletedOperation(Exception failure)
		{
			_failure = failure;
		}

		public override string Id => "completed-test-operation";

		public override AnalysisResult Value => _failure is null ? _value! : throw _failure;

		public override bool HasCompleted => true;

		public override bool HasValue => _failure is null;

		public override Response GetRawResponse() => _response;

		public override Response UpdateStatus(CancellationToken cancellationToken = default) => _response;

		public override ValueTask<Response> UpdateStatusAsync(
			CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(_response);
	}

	private sealed class TestResponse : Response
	{
		public override int Status => 200;

		public override string ReasonPhrase => "OK";

		public override Stream? ContentStream { get; set; }

		public override string ClientRequestId { get; set; } = "test-request";

		public override void Dispose()
		{
		}

		protected override bool ContainsHeader(string name) => false;

		protected override IEnumerable<HttpHeader> EnumerateHeaders() => [];

		protected override bool TryGetHeader(string name, out string value)
		{
			value = string.Empty;
			return false;
		}

		protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values)
		{
			values = [];
			return false;
		}
	}

	private sealed class RecordingTokenCredential : TokenCredential, IDisposable
	{
		private int _tokenRequestCount;

		public int TokenRequestCount => Volatile.Read(ref _tokenRequestCount);

		public bool IsDisposed { get; private set; }

		public override AccessToken GetToken(
			TokenRequestContext requestContext,
			CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref _tokenRequestCount);
			return new AccessToken("test-token", new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
		}

		public override ValueTask<AccessToken> GetTokenAsync(
			TokenRequestContext requestContext,
			CancellationToken cancellationToken) =>
			ValueTask.FromResult(GetToken(requestContext, cancellationToken));

		public void Dispose() => IsDisposed = true;
	}

	private abstract class DisposalObservingStream : Stream
	{
		public bool IsDisposed { get; private set; }

		protected override void Dispose(bool disposing)
		{
			IsDisposed = true;
			base.Dispose(disposing);
		}

		public override void Flush() => throw new NotSupportedException();

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		public override bool CanWrite => false;
	}

	private sealed class TrackingSeekableStream : DisposalObservingStream
	{
		private readonly MemoryStream _inner;

		public TrackingSeekableStream(byte[] bytes, int initialPosition = 0)
		{
			_inner = new MemoryStream(bytes, writable: false);
			_inner.Position = initialPosition;
		}

		public int ReadCalls { get; private set; }

		public override bool CanRead => !IsDisposed;

		public override bool CanSeek => !IsDisposed;

		public override long Length => _inner.Length;

		public override long Position
		{
			get => _inner.Position;
			set => _inner.Position = value;
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			ReadCalls++;
			return _inner.Read(buffer, offset, count);
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
			if (disposing)
			{
				_inner.Dispose();
			}

			base.Dispose(disposing);
		}
	}

	private sealed class UnreadableStream : DisposalObservingStream
	{
		public override bool CanRead => false;

		public override bool CanSeek => false;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count) =>
			throw new NotSupportedException();
	}

	private class ChunkedNonSeekableStream(byte[] bytes, int maximumChunkSize = int.MaxValue)
		: DisposalObservingStream
	{
		private int _position;

		public int BytesRead { get; private set; }

		public int ReadCalls { get; private set; }

		public override bool CanRead => !IsDisposed;

		public override bool CanSeek => false;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			ReadCalls++;
			int bytesRead = Math.Min(Math.Min(count, maximumChunkSize), bytes.Length - _position);
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
	}

	private sealed class PendingReadStream(byte[] bytes) : ChunkedNonSeekableStream(bytes)
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

	private sealed class CancellationAwareStream : DisposalObservingStream
	{
		private readonly TaskCompletionSource _secondReadStarted =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _neverCompletes =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public int BytesRead { get; private set; }

		public int ReadCalls { get; private set; }

		public Task SecondReadStarted => _secondReadStarted.Task;

		public override bool CanRead => !IsDisposed;

		public override bool CanSeek => false;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count) =>
			throw new NotSupportedException();

		public override async Task<int> ReadAsync(
			byte[] buffer,
			int offset,
			int count,
			CancellationToken cancellationToken)
		{
			ReadCalls++;
			if (ReadCalls == 1)
			{
				buffer[offset] = 0x42;
				BytesRead = 1;
				return 1;
			}

			_secondReadStarted.TrySetResult();
			await _neverCompletes.Task.WaitAsync(cancellationToken);
			return 0;
		}
	}

	private sealed class UnknownService
	{
	}
}
