// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class ImageClassificationContractTests
{
	[Fact]
	public async Task ClassifyAsync_DataContent_ForwardsSnapshotMediaTypeOptionsAndCancellation()
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

		ImageClassificationResult result = await client.ClassifyAsync(
			image,
			options,
			cancellationSource.Token);

		imageBytes[0] = 99;

		Assert.Same(client.Result, result);
		Assert.Equal([1, 2, 3, 4], client.ImageBytes);
		Assert.Equal("image/png", client.MediaType);
		Assert.Same(options, client.Options);
		Assert.Equal(cancellationSource.Token, client.CancellationToken);
		Assert.NotNull(client.InputStream);
		Assert.False(client.InputStream.CanRead);
	}

	[Theory]
	[InlineData("application/octet-stream")]
	[InlineData("text/plain")]
	public async Task ClassifyAsync_NonImageData_ThrowsBeforeInvokingClient(string mediaType)
	{
		var client = new RecordingClient();
		var image = new DataContent(new byte[] { 1 }, mediaType);

		ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
			() => client.ClassifyAsync(image));

		Assert.Equal("image", exception.ParamName);
		Assert.Equal(0, client.CallCount);
	}

	[Fact]
	public async Task ClassifyAsync_EmptyData_ThrowsBeforeInvokingClient()
	{
		var client = new RecordingClient();
		var image = new DataContent(ReadOnlyMemory<byte>.Empty, "image/png");

		ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
			() => client.ClassifyAsync(image));

		Assert.Equal("image", exception.ParamName);
		Assert.Equal(0, client.CallCount);
	}

	[Fact]
	public void Options_InvalidValues_Throw()
	{
		var options = new ImageClassificationOptions();

		Assert.Throws<ArgumentOutOfRangeException>(() => options.MaximumPredictions = 0);
		Assert.Throws<ArgumentOutOfRangeException>(() => options.MaximumPredictions = -1);
		Assert.Throws<ArgumentOutOfRangeException>(() => options.MinimumConfidence = -0.01f);
		Assert.Throws<ArgumentOutOfRangeException>(() => options.MinimumConfidence = 1.01f);
		Assert.Throws<ArgumentOutOfRangeException>(() => options.MinimumConfidence = float.NaN);
		Assert.Throws<ArgumentOutOfRangeException>(() => options.MinimumConfidence = float.PositiveInfinity);
	}

	[Fact]
	public void Options_Clone_CopiesNormalizedConstraints()
	{
		var options = new ImageClassificationOptions
		{
			MaximumPredictions = 3,
			MinimumConfidence = 0.5f
		};

		ImageClassificationOptions clone = options.Clone();

		Assert.NotSame(options, clone);
		Assert.Equal(3, clone.MaximumPredictions);
		Assert.Equal(0.5f, clone.MinimumConfidence);
	}

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	public void Prediction_EmptyLabel_Throws(string label)
	{
		Assert.Throws<ArgumentException>(() => new ImageClassificationPrediction(label, 0.5f));
	}

	[Theory]
	[InlineData(-0.01f)]
	[InlineData(1.01f)]
	[InlineData(float.NaN)]
	[InlineData(float.PositiveInfinity)]
	public void Prediction_InvalidConfidence_Throws(float confidence)
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new ImageClassificationPrediction("cat", confidence));
	}

	[Fact]
	public void Result_SnapshotsAndOrdersPredictions_WithModelProvenance()
	{
		var predictions = new List<ImageClassificationPrediction>
		{
			new("cat", 0.25f),
			new("dog", 0.9f)
		};

		var result = new ImageClassificationResult(predictions)
		{
			ModelId = "animals-v1"
		};
		predictions.Clear();

		Assert.Collection(
			result.Predictions,
			prediction =>
			{
				Assert.Equal("dog", prediction.Label);
				Assert.Equal(0.9f, prediction.Confidence);
			},
			prediction =>
			{
				Assert.Equal("cat", prediction.Label);
				Assert.Equal(0.25f, prediction.Confidence);
			});
		Assert.Equal("animals-v1", result.ModelId);
	}

	[Fact]
	public void Metadata_ExposesProviderAndDefaultModel()
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

		public async Task<ImageClassificationResult> ClassifyAsync(
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
