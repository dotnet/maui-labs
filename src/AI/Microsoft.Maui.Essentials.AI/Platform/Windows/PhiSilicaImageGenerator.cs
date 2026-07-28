using System.Runtime.Versioning;
using Microsoft.Extensions.AI;
using Microsoft.Graphics.Imaging;
using WindowsImageGenerationOptions = Microsoft.Windows.AI.Imaging.ImageGenerationOptions;
using WindowsImageGenerator = Microsoft.Windows.AI.Imaging.ImageGenerator;
using WindowsImageGeneratorResult = Microsoft.Windows.AI.Imaging.ImageGeneratorResult;
using WindowsImageGeneratorResultStatus = Microsoft.Windows.AI.Imaging.ImageGeneratorResultStatus;

// CS8305: every Windows AI imaging type used below is marked [Experimental] in WinRT metadata.
// Windows App SDK 2.2.x exposes no stable equivalent, and this file exists purely to wrap those
// APIs, so the warning is acknowledged here rather than repeated at each member.
#pragma warning disable CS8305

namespace Microsoft.Maui.Essentials.AI;

/// <summary>
/// Provides an <see cref="IImageGenerator"/> implementation backed by the native Windows Copilot
/// Runtime image generation model.
/// </summary>
/// <remarks>
/// The number of images in <see cref="ImageGenerationRequest.OriginalImages"/> selects the operation:
/// <list type="bullet">
/// <item><description>none — text to image.</description></item>
/// <item><description>one — image to image, guided by the prompt.</description></item>
/// <item><description>two — inpainting, where the second image is the mask.</description></item>
/// </list>
/// </remarks>
[SupportedOSPlatform("windows10.0.26100.0")]
public sealed class PhiSilicaImageGenerator : IImageGenerator
{
	/// <summary>The provider name for this image generator.</summary>
	private const string ProviderName = "windows";

	/// <summary>The default model identifier.</summary>
	private const string DefaultModelId = "windows-image-generator";

	/// <summary>Lazily-initialized task that creates the underlying <see cref="WindowsImageGenerator"/>.</summary>
	private readonly Task<WindowsImageGenerator> _generatorTask;

	/// <summary>Whether this instance owns the <see cref="WindowsImageGenerator"/> and must dispose it.</summary>
	private readonly bool _ownsGenerator;

	/// <summary>Lazily-initialized metadata describing the implementation.</summary>
	private ImageGeneratorMetadata? _metadata;

	/// <summary>
	/// Initializes a new instance of the <see cref="PhiSilicaImageGenerator"/> class.
	/// </summary>
	public PhiSilicaImageGenerator()
	{
		_generatorTask = PhiSilicaModelFactory.CreateImageGeneratorAsync();
		_ownsGenerator = true;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="PhiSilicaImageGenerator"/> class with the
	/// specified <see cref="WindowsImageGenerator"/>.
	/// </summary>
	/// <param name="generator">The <see cref="WindowsImageGenerator"/> to use.</param>
	/// <remarks>
	/// When using this constructor, the caller remains responsible for disposing the generator.
	/// </remarks>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="generator"/> is <see langword="null"/>.</exception>
	public PhiSilicaImageGenerator(WindowsImageGenerator generator)
	{
		ArgumentNullException.ThrowIfNull(generator);

		_generatorTask = Task.FromResult(generator);
		_ownsGenerator = false;
	}

	/// <inheritdoc />
	public async Task<ImageGenerationResponse> GenerateAsync(
		ImageGenerationRequest request,
		ImageGenerationOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (string.IsNullOrWhiteSpace(request.Prompt))
			throw new ArgumentException("A prompt is required.", nameof(request));

		ValidateOptions(options);

		var generator = await _generatorTask.ConfigureAwait(false);
		var sources = await DecodeSourceImagesAsync(request.OriginalImages).ConfigureAwait(false);

		try
		{
			var mediaType = options?.MediaType ?? PhiSilicaImageBuffers.DefaultMediaType;
			var count = options?.Count ?? 1;
			var contents = new List<AIContent>(count);

			for (var i = 0; i < count; i++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var generatorOptions = ConvertToImageGenerationOptions(options, i);
				var result = Generate(generator, request.Prompt, sources, generatorOptions);

				if (result.Status is not WindowsImageGeneratorResultStatus.Success)
					throw new InvalidOperationException($"Image generation failed: {result.Status}", result.ExtendedError);

				using var image = result.Image;
				var bytes = await PhiSilicaImageBuffers.EncodeAsync(image, mediaType).ConfigureAwait(false);

				contents.Add(new DataContent(bytes, mediaType));
			}

			return new ImageGenerationResponse(contents);
		}
		finally
		{
			foreach (var source in sources)
				source.Dispose();
		}
	}

	/// <inheritdoc />
	object? IImageGenerator.GetService(Type serviceType, object? serviceKey)
	{
		ArgumentNullException.ThrowIfNull(serviceType);

		if (serviceKey is not null)
		{
			return null;
		}

		if (serviceType == typeof(ImageGeneratorMetadata))
		{
			return _metadata ??= new ImageGeneratorMetadata(
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
		if (!_ownsGenerator)
			return;

		if (_generatorTask.IsCompletedSuccessfully)
			_generatorTask.Result.Dispose();
		else
			_generatorTask.ContinueWith(
				t => { if (t.IsCompletedSuccessfully) t.Result.Dispose(); },
				TaskContinuationOptions.ExecuteSynchronously);
	}

	private static WindowsImageGeneratorResult Generate(
		WindowsImageGenerator generator,
		string prompt,
		IReadOnlyList<ImageBuffer> sources,
		WindowsImageGenerationOptions options) => sources.Count switch
		{
			0 => generator.GenerateImageFromTextPrompt(prompt, options),
			1 => generator.GenerateImageFromImageBuffer(sources[0], prompt, options),
			2 => generator.GenerateImageFromImageBufferAndMask(sources[0], sources[1], prompt, options),
			_ => throw new NotSupportedException(
				"Windows image generation accepts at most two images: a source image and an optional mask.")
		};

	private static async Task<IReadOnlyList<ImageBuffer>> DecodeSourceImagesAsync(IEnumerable<AIContent>? originalImages)
	{
		if (originalImages is null)
			return [];

		var buffers = new List<ImageBuffer>();
		try
		{
			foreach (var content in originalImages)
			{
				if (content is not DataContent data)
					throw new NotSupportedException(
						$"Only {nameof(DataContent)} images are supported. Unsupported content: {content.GetType().Name}.");

				buffers.Add(await PhiSilicaImageBuffers.DecodeAsync(data.Data).ConfigureAwait(false));
			}

			return buffers;
		}
		catch
		{
			foreach (var buffer in buffers)
				buffer.Dispose();

			throw;
		}
	}

	private static WindowsImageGenerationOptions ConvertToImageGenerationOptions(ImageGenerationOptions? options, int index)
	{
		var generatorOptions = new WindowsImageGenerationOptions();

		if (options?.AdditionalProperties is not { } properties)
			return generatorOptions;

		if (properties.TryGetValue(nameof(WindowsImageGenerationOptions.Creativity), out var creativity) &&
			creativity is double creativityValue)
		{
			generatorOptions.Creativity = creativityValue;
		}

		if (properties.TryGetValue(nameof(WindowsImageGenerationOptions.MaxInferenceSteps), out var steps) &&
			steps is int stepsValue)
		{
			generatorOptions.MaxInferenceSteps = stepsValue;
		}

		// Offset the seed per image so that requesting several images does not return duplicates.
		if (properties.TryGetValue(nameof(WindowsImageGenerationOptions.Seed), out var seed) &&
			seed is int seedValue)
		{
			generatorOptions.Seed = unchecked(seedValue + index);
		}

		return generatorOptions;
	}

	private static void ValidateOptions(ImageGenerationOptions? options)
	{
		if (options is null)
			return;

		if (options.Count is <= 0)
			throw new ArgumentOutOfRangeException(nameof(options), "Count must be greater than zero.");

		// The model returns raw pixels, so there is no hosted URI to hand back.
		if (options.ResponseFormat is ImageGenerationResponseFormat.Uri)
			throw new NotSupportedException(
				"Windows image generation runs on-device and cannot return hosted image URIs.");

		// GenerateImageFromTextPrompt has no size parameter — the model picks the output size.
		if (options.ImageSize is not null)
			throw new NotSupportedException(
				"Windows image generation does not support a requested image size.");
	}
}
