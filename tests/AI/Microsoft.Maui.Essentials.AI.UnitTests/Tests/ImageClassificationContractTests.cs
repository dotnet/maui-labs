// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class ImageClassificationContractTests
{
	[Fact]
	public async Task ClassifyImageAsync_DataContent_ForwardsSnapshotMediaTypeOptionsAndCancellation()
	{
		byte[] imageBytes = [1, 2, 3, 4];
		var image = new DataContent(imageBytes, "image/png");
		var options = new ImageClassificationOptions
		{
			MaximumPredictions = 2,
			MinimumConfidence = 0.25f
		};
		using var cancellationSource = new CancellationTokenSource();
		var client = new RecordingClient();

		ImageClassificationResult result = await client.ClassifyImageAsync(
			image,
			options,
			cancellationSource.Token);

		imageBytes[0] = 99;

		Assert.Same(client.Result, result);
		Assert.Equal(1, client.CallCount);
		Assert.Equal([1, 2, 3, 4], client.ImageBytes);
		Assert.Equal("image/png", client.MediaType);
		Assert.Same(options, client.Options);
		Assert.Equal(cancellationSource.Token, client.CancellationToken);
		Assert.NotNull(client.InputStream);
		Assert.False(client.InputStream.CanRead);
	}

	[Theory]
	[InlineData(true, "image/png")]
	[InlineData(false, "application/octet-stream")]
	[InlineData(false, "text/plain")]
	public async Task ClassifyImageAsync_EmptyOrNonImageData_RejectsBeforeClientCall(
		bool empty,
		string mediaType)
	{
		var client = new RecordingClient();
		var image = new DataContent(empty ? ReadOnlyMemory<byte>.Empty : new byte[] { 1 }, mediaType);

		ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
			() => client.ClassifyImageAsync(image));

		Assert.Equal("image", exception.ParamName);
		Assert.Equal(0, client.CallCount);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void Options_MaximumPredictions_RejectsNonPositiveValues(int maximumPredictions)
	{
		var options = new ImageClassificationOptions();

		ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
			() => options.MaximumPredictions = maximumPredictions);

		Assert.Equal("value", exception.ParamName);
		Assert.Equal(maximumPredictions, Assert.IsType<int>(exception.ActualValue));
	}

	[Fact]
	public void Options_Clone_PreservesValuesAndIndependence()
	{
		var options = new ImageClassificationOptions
		{
			MaximumInputBytes = 1024,
			MaximumPredictions = 3,
			MinimumConfidence = 0.5f
		};

		ImageClassificationOptions clone = options.Clone();
		options.MaximumInputBytes = 2048;
		options.MaximumPredictions = 5;
		options.MinimumConfidence = 0.75f;

		Assert.NotSame(options, clone);
		Assert.Equal(1024, clone.MaximumInputBytes);
		Assert.Equal(3, clone.MaximumPredictions);
		Assert.Equal(0.5f, clone.MinimumConfidence);
		Assert.Equal(2048, options.MaximumInputBytes);
		Assert.Equal(5, options.MaximumPredictions);
		Assert.Equal(0.75f, options.MinimumConfidence);
	}

	[Fact]
	public void Options_MaximumInputBytes_DefaultsToExactlyTwentyMiBAndAcceptsExplicitValue()
	{
		var options = new ImageClassificationOptions();

		Assert.Equal(20L * 1024 * 1024, options.MaximumInputBytes);

		options.MaximumInputBytes = 1234;

		Assert.Equal(1234, options.MaximumInputBytes);
	}

	[Theory]
	[InlineData(0L)]
	[InlineData(-1L)]
	public void Options_MaximumInputBytes_RejectsNonPositiveValues(long maximumInputBytes)
	{
		var options = new ImageClassificationOptions();

		ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
			() => options.MaximumInputBytes = maximumInputBytes);

		Assert.Equal("value", exception.ParamName);
		Assert.Equal(maximumInputBytes, Assert.IsType<long>(exception.ActualValue));
		Assert.Equal(20L * 1024 * 1024, options.MaximumInputBytes);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" ")]
	public void Prediction_NullOrEmptyLabel_Throws(string? label)
	{
		ArgumentException exception = Assert.ThrowsAny<ArgumentException>(
			() => new ImageClassificationPrediction(label!, 0.5f));

		Assert.Equal("label", exception.ParamName);
	}

	[Theory]
	[InlineData(-0.01f)]
	[InlineData(1.01f)]
	[InlineData(float.NegativeInfinity)]
	[InlineData(float.PositiveInfinity)]
	[InlineData(float.NaN)]
	public void Prediction_PresentConfidence_RejectsNonFiniteOrOutOfRange(float confidence)
	{
		ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
			() => new ImageClassificationPrediction("cat", confidence));

		Assert.Equal("confidence", exception.ParamName);
		Assert.Equal(confidence, Assert.IsType<float>(exception.ActualValue));
	}

	[Fact]
	public void Result_SnapshotsAndPreservesPredictionOrder_WithModelProvenance()
	{
		var predictions = new List<ImageClassificationPrediction>
		{
			new("cat", 0.25f),
			new("dog", 0.9f)
		};
		var rawRepresentation = new object();
		var additionalProperties = new AdditionalPropertiesDictionary
		{
			["taxonomy"] = "animals"
		};

		var result = new ImageClassificationResult(predictions)
		{
			ModelId = "animals-v1",
			RawRepresentation = rawRepresentation,
			AdditionalProperties = additionalProperties
		};
		predictions.Clear();

		Assert.Collection(
			result.Predictions,
			prediction =>
			{
				Assert.Equal("cat", prediction.Label);
				Assert.Equal(0.25f, prediction.Confidence);
			},
			prediction =>
			{
				Assert.Equal("dog", prediction.Label);
				Assert.Equal(0.9f, prediction.Confidence);
			});
		Assert.Equal("animals-v1", result.ModelId);
		Assert.Same(rawRepresentation, result.RawRepresentation);
		Assert.Same(additionalProperties, result.AdditionalProperties);
	}

	[Fact]
	public void Metadata_ExposesConfiguredValues()
	{
		var providerUri = new Uri("https://example.test");
		var metadata = new ImageClassificationClientMetadata(
			providerName: "fixture",
			providerUri: providerUri,
			defaultModelId: "animals-v1");

		Assert.Equal("fixture", metadata.ProviderName);
		Assert.Equal(providerUri, metadata.ProviderUri);
		Assert.Equal("animals-v1", metadata.DefaultModelId);
	}

	[Fact]
	public void GetService_ProvidesTypedEscapeHatch()
	{
		var metadata = new ImageClassificationClientMetadata("fixture");
		using var client = new RecordingClient(metadata);

		Assert.Same(metadata, client.GetService<ImageClassificationClientMetadata>());
		Assert.Null(client.GetService<string>());
		Assert.Null(client.GetService<ImageClassificationClientMetadata>("unknown"));
	}

	[Fact]
	public void PublicImageClassificationTypes_AreInMicrosoftMauiEssentialsAINamespace()
	{
		Type[] publicContractTypes =
		[
			typeof(IImageClassificationClient),
			typeof(ImageClassificationClientExtensions),
			typeof(ImageClassificationClientMetadata),
			typeof(ImageClassificationOptions),
			typeof(ImageClassificationPrediction),
			typeof(ImageClassificationResult)
		];

		Assert.All(
			publicContractTypes,
			type =>
			{
				Assert.True(type.IsPublic);
				Assert.Equal("Microsoft.Maui.Essentials.AI", type.Namespace);
			});
	}

	[Fact]
	public void Prediction_DefaultConfidence_IsNull()
	{
		var prediction = new ImageClassificationPrediction("snow leopard");

		Assert.Equal(
			typeof(float?),
			typeof(ImageClassificationPrediction).GetProperty(nameof(ImageClassificationPrediction.Confidence))!.PropertyType);
		Assert.Equal("snow leopard", prediction.Label);
		Assert.Null(prediction.Confidence);
	}

	[Theory]
	[InlineData(0f)]
	[InlineData(0.375f)]
	[InlineData(1f)]
	public void Prediction_PresentConfidence_AcceptsInclusiveFiniteBounds(float confidence)
	{
		var prediction = new ImageClassificationPrediction("snow leopard", confidence);

		Assert.Equal("snow leopard", prediction.Label);
		Assert.Equal(confidence, prediction.Confidence);
	}

	[Fact]
	public void Options_DefaultsAreNull()
	{
		var options = new ImageClassificationOptions();

		Assert.Equal(20L * 1024 * 1024, options.MaximumInputBytes);
		Assert.Null(options.MaximumPredictions);
		Assert.Null(options.MinimumConfidence);
	}

	[Theory]
	[InlineData(null)]
	[InlineData(0f)]
	[InlineData(0.375f)]
	[InlineData(1f)]
	public void Options_MinimumConfidence_AcceptsNullAndInclusiveFiniteBounds(float? minimumConfidence)
	{
		var options = new ImageClassificationOptions
		{
			MinimumConfidence = minimumConfidence
		};

		Assert.Equal(minimumConfidence, options.MinimumConfidence);
		Assert.Null(options.MaximumPredictions);
	}

	[Theory]
	[InlineData(-0.01f)]
	[InlineData(1.01f)]
	[InlineData(float.NegativeInfinity)]
	[InlineData(float.PositiveInfinity)]
	[InlineData(float.NaN)]
	public void Options_MinimumConfidence_RejectsNonFiniteOrOutOfRange(float minimumConfidence)
	{
		var options = new ImageClassificationOptions();

		ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
			() => options.MinimumConfidence = minimumConfidence);

		Assert.Equal("value", exception.ParamName);
		Assert.Equal(minimumConfidence, Assert.IsType<float>(exception.ActualValue));
		Assert.Null(options.MinimumConfidence);
	}

	[Fact]
	public void Metadata_DefaultsToNull()
	{
		var metadata = new ImageClassificationClientMetadata();

		Assert.Null(metadata.ProviderName);
		Assert.Null(metadata.ProviderUri);
		Assert.Null(metadata.DefaultModelId);
	}

	[Fact]
	public void Result_PreservesProviderPredictionOrder()
	{
		var first = new ImageClassificationPrediction("lynx", 0.2f);
		var second = new ImageClassificationPrediction("bobcat", 0.95f);
		var third = new ImageClassificationPrediction("cougar");
		var predictions = new List<ImageClassificationPrediction> { first, second, third };

		var result = new ImageClassificationResult(predictions);
		predictions.Clear();

		Assert.Collection(
			result.Predictions,
			prediction => Assert.Same(first, prediction),
			prediction => Assert.Same(second, prediction),
			prediction => Assert.Same(third, prediction));
		Assert.Equal(["lynx", "bobcat", "cougar"], result.Predictions.Select(prediction => prediction.Label));
	}

	[Fact]
	public void Result_RetainsModelIdRawRepresentationAndAdditionalProperties()
	{
		var prediction = new ImageClassificationPrediction("harbor seal", 0.8f);
		var rawRepresentation = new object();
		var additionalProperties = new AdditionalPropertiesDictionary
		{
			["providerRequestId"] = "request-42"
		};

		var result = new ImageClassificationResult([prediction])
		{
			ModelId = "wildlife-v2",
			RawRepresentation = rawRepresentation,
			AdditionalProperties = additionalProperties
		};

		Assert.Equal("wildlife-v2", result.ModelId);
		Assert.Same(rawRepresentation, result.RawRepresentation);
		Assert.Same(additionalProperties, result.AdditionalProperties);
		Assert.Same(prediction, Assert.Single(result.Predictions));
	}

	[Fact]
	public void Result_NullSequenceOrNullItem_Throws()
	{
		ArgumentNullException nullSequenceException = Assert.Throws<ArgumentNullException>(
			() => new ImageClassificationResult(null!));
		ArgumentException nullItemException = Assert.Throws<ArgumentException>(
			() => new ImageClassificationResult([new ImageClassificationPrediction("otter"), null!]));

		Assert.Equal("predictions", nullSequenceException.ParamName);
		Assert.Equal("predictions", nullItemException.ParamName);
	}

	[Fact]
	public void IImageClassificationClient_ClassifyImageAsync_HasExpectedTaskShape()
	{
		var method = Assert.Single(
			typeof(IImageClassificationClient).GetMethods(),
			method => method.Name == nameof(IImageClassificationClient.ClassifyImageAsync));

		Assert.Equal(typeof(Task<ImageClassificationResult>), method.ReturnType);
		Assert.Collection(
			method.GetParameters(),
			parameter =>
			{
				Assert.Equal("imageStream", parameter.Name);
				Assert.Equal(typeof(Stream), parameter.ParameterType);
				Assert.False(parameter.IsOptional);
			},
			parameter =>
			{
				Assert.Equal("imageMediaType", parameter.Name);
				Assert.Equal(typeof(string), parameter.ParameterType);
				Assert.False(parameter.IsOptional);
			},
			parameter =>
			{
				Assert.Equal("options", parameter.Name);
				Assert.Equal(typeof(ImageClassificationOptions), parameter.ParameterType);
				Assert.True(parameter.IsOptional);
				Assert.Null(parameter.DefaultValue);
			},
			parameter =>
			{
				Assert.Equal("cancellationToken", parameter.Name);
				Assert.Equal(typeof(CancellationToken), parameter.ParameterType);
				Assert.True(parameter.IsOptional);
				Assert.Null(parameter.DefaultValue);
			});
	}

	[Fact]
	public void ImageClassificationClientExtensions_ClassifyImageAsync_HasDataContentOverload()
	{
		var method = Assert.Single(
			typeof(ImageClassificationClientExtensions).GetMethods(),
			method => method.Name == nameof(ImageClassificationClientExtensions.ClassifyImageAsync));

		Assert.True(method.IsPublic);
		Assert.True(method.IsStatic);
		Assert.False(method.IsGenericMethod);
		Assert.True(method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), inherit: false));
		Assert.Equal(typeof(Task<ImageClassificationResult>), method.ReturnType);
		Assert.Collection(
			method.GetParameters(),
			parameter =>
			{
				Assert.Equal("client", parameter.Name);
				Assert.Equal(typeof(IImageClassificationClient), parameter.ParameterType);
				Assert.False(parameter.IsOptional);
			},
			parameter =>
			{
				Assert.Equal("image", parameter.Name);
				Assert.Equal(typeof(DataContent), parameter.ParameterType);
				Assert.False(parameter.IsOptional);
			},
			parameter =>
			{
				Assert.Equal("options", parameter.Name);
				Assert.Equal(typeof(ImageClassificationOptions), parameter.ParameterType);
				Assert.True(parameter.IsOptional);
				Assert.Null(parameter.DefaultValue);
			},
			parameter =>
			{
				Assert.Equal("cancellationToken", parameter.Name);
				Assert.Equal(typeof(CancellationToken), parameter.ParameterType);
				Assert.True(parameter.IsOptional);
				Assert.Null(parameter.DefaultValue);
			});
	}

	[Fact]
	public async Task ClassifyImageAsync_NullClientOrImage_Throws()
	{
		IImageClassificationClient nullClient = null!;
		var client = new RecordingClient();
		var image = new DataContent(new byte[] { 1 }, "image/png");

		ArgumentNullException clientException = await Assert.ThrowsAsync<ArgumentNullException>(
			() => nullClient.ClassifyImageAsync(image));
		ArgumentNullException imageException = await Assert.ThrowsAsync<ArgumentNullException>(
			() => client.ClassifyImageAsync(null!));

		Assert.Equal("client", clientException.ParamName);
		Assert.Equal("image", imageException.ParamName);
		Assert.Equal(0, client.CallCount);
	}

	[Fact]
	public async Task ClassifyImageAsync_Stream_CallerRetainsOwnership()
	{
		byte[] imageBytes = [8, 6, 7, 5, 3, 0, 9];
		using var imageStream = new MemoryStream(imageBytes);
		var options = new ImageClassificationOptions { MaximumPredictions = 3 };
		using var cancellationSource = new CancellationTokenSource();
		var client = new RecordingClient();

		ImageClassificationResult result = await client.ClassifyImageAsync(
			imageStream,
			"image/webp",
			options,
			cancellationSource.Token);

		Assert.Same(client.Result, result);
		Assert.Equal(1, client.CallCount);
		Assert.Same(imageStream, client.InputStream);
		Assert.Equal(imageBytes, client.ImageBytes);
		Assert.Equal("image/webp", client.MediaType);
		Assert.Same(options, client.Options);
		Assert.Equal(cancellationSource.Token, client.CancellationToken);
		Assert.True(imageStream.CanRead);
		imageStream.Position = 0;
		Assert.Equal(8, imageStream.ReadByte());
	}

	[Fact]
	public void GetService_TypedAndUntyped_ExposeSelfAndMetadata()
	{
		var metadata = new ImageClassificationClientMetadata(
			providerName: "fixture",
			defaultModelId: "wildlife-v2");
		using var client = new RecordingClient(metadata);

		Assert.Same(client, client.GetService<IImageClassificationClient>());
		Assert.Same(client, client.GetService(typeof(IImageClassificationClient)));
		Assert.Same(metadata, client.GetService<ImageClassificationClientMetadata>());
		Assert.Same(metadata, client.GetService(typeof(ImageClassificationClientMetadata)));
	}

	[Fact]
	public void GetService_KeyedOrUnknown_ReturnsNull()
	{
		using var client = new RecordingClient(new ImageClassificationClientMetadata("fixture"));
		var serviceKey = new object();

		Assert.Null(client.GetService<string>());
		Assert.Null(client.GetService(typeof(string)));
		Assert.Null(client.GetService<IImageClassificationClient>(serviceKey));
		Assert.Null(client.GetService(typeof(IImageClassificationClient), serviceKey));
	}

	private sealed class RecordingClient(ImageClassificationClientMetadata? metadata = null)
		: IImageClassificationClient
	{
		public ImageClassificationResult Result { get; } = new(
			[new ImageClassificationPrediction("fixture", 1)]);

		public int CallCount { get; private set; }

		public byte[]? ImageBytes { get; private set; }

		public string? MediaType { get; private set; }

		public ImageClassificationOptions? Options { get; private set; }

		public CancellationToken CancellationToken { get; private set; }

		public Stream? InputStream { get; private set; }

		public async Task<ImageClassificationResult> ClassifyImageAsync(
			Stream imageStream,
			string imageMediaType,
			ImageClassificationOptions? options = null,
			CancellationToken cancellationToken = default)
		{
			CallCount++;
			InputStream = imageStream;
			MediaType = imageMediaType;
			Options = options;
			CancellationToken = cancellationToken;

			using var copy = new MemoryStream();
			await imageStream.CopyToAsync(copy, cancellationToken);
			ImageBytes = copy.ToArray();

			return Result;
		}

		public object? GetService(Type serviceType, object? serviceKey = null)
		{
			ArgumentNullException.ThrowIfNull(serviceType);

			if (serviceKey is not null)
			{
				return null;
			}

			if (serviceType == typeof(ImageClassificationClientMetadata))
			{
				return metadata;
			}

			return serviceType.IsInstanceOfType(this) ? this : null;
		}

		public void Dispose()
		{
		}
	}
}
