#if IOS || MACCATALYST
using CoreGraphics;
using Foundation;
using ImageIO;
using Microsoft.Extensions.AI;
using UIKit;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

/// <summary>
/// Tests for multimodal image input on the Apple Intelligence chat client.
/// The conversion tests are model-free (they exercise only the managed ↔ native marshalling)
/// and run in CI; the end-to-end description test requires an on-device model.
/// </summary>
[Collection(nameof(AppleIntelligenceCollection))]
public class AppleIntelligenceChatClientImageTests
{
	static CGImage CreateTestImage(int width = 2, int height = 2)
	{
		var bytesPerRow = width * 4;
		var buffer = new byte[bytesPerRow * height];
		using var colorSpace = CGColorSpace.CreateDeviceRGB();
		using var context = new CGBitmapContext(buffer, width, height, 8, bytesPerRow, colorSpace, CGImageAlphaInfo.PremultipliedLast);
		context.SetFillColor(new CGColor(1f, 0f, 0f, 1f));
		context.FillRect(new CGRect(0, 0, width, height));
		return context.ToImage()!;
	}

	static byte[] CreatePngBytes()
	{
		using var image = CreateTestImage();
		using var png = new NSMutableData();
		using var destination = CGImageDestination.Create(png, "public.png", 1)!;
		destination.AddImage(image);
		Assert.True(destination.Close());
		return png.ToArray();
	}

	static void WithImageFile(byte[] bytes, Action<string> action)
	{
		var path = Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, $"content-boundary-{Guid.NewGuid():N}.png");
		try
		{
			File.WriteAllBytes(path, bytes);
			action(path);
		}
		finally
		{
			File.Delete(path);
		}
	}

	// Tests the Apple image marshaller's MIME dispatch helper, not a public IChatClient content contract.
	[Fact]
	public void IsImage_DetectsImageMediaTypes()
	{
		Assert.True(AppleIntelligenceChatClient.IsImage("image/png"));
		Assert.True(AppleIntelligenceChatClient.IsImage("IMAGE/JPEG"));
		Assert.False(AppleIntelligenceChatClient.IsImage("application/pdf"));
		Assert.False(AppleIntelligenceChatClient.IsImage(null));
	}

	// Inspects NSData and MIME fields on Apple's ImageContentNative; other providers have different image envelopes.
	[Fact]
	public void ToNative_DataContentImage_CarriesBytesAndMediaType()
	{
		var bytes = CreatePngBytes();
		var content = new DataContent(bytes, "image/png");

		var native = AppleIntelligenceChatClient.ToNative(content);

		Assert.NotNull(native.Data);
		Assert.Equal(bytes, native.Data!.ToArray());
		Assert.Equal("image/png", native.MimeType);
		Assert.Null(native.CgImage);
	}

	// Exercises ImageIO EXIF extraction and CGImage pixel normalization during Apple's encoded-image round trip.
	[Fact]
	public void ToNative_EncodedImage_PreservesExifOrientation()
	{
		using var image = CreateTestImage(2, 3);
		using var jpeg = new NSMutableData();
		using var destination = CGImageDestination.Create(jpeg, "public.jpeg", 1)!;
		using var properties = NSDictionary<NSString, NSObject>.FromObjectsAndKeys(
			[NSNumber.FromInt32(6)], [ImageIO.CGImageProperties.Orientation]);
		destination.AddImage(image, properties);
		Assert.True(destination.Close());

		var native = AppleIntelligenceChatClient.ToNative(new DataContent(jpeg.ToArray(), "image/jpeg"));

		Assert.Equal(CGImagePropertyOrientation.Right, native.Orientation);
		Assert.NotNull(native.Data);

		var portable = Assert.IsType<DataContent>(AppleIntelligenceChatClient.FromNative(native));
		Assert.Equal("image/png", portable.MediaType);
		var oriented = Assert.IsType<CGImage>(portable.RawRepresentation);
		Assert.Equal((nint)3, oriented.Width);
		Assert.Equal((nint)2, oriented.Height);
		Assert.Null(AppleIntelligenceChatClient.ToNative(portable).Orientation);
	}

	// Apple's CGImage RawRepresentation bypasses NSData allocation; zero-copy native handles are not portable.
	[Fact]
	public void ToNative_DataContentWithNativeHandle_UsesZeroCopyFastPath()
	{
		using var image = CreateTestImage();
		var content = new DataContent(new byte[] { 0 }, "image/png") { RawRepresentation = image };

		var native = AppleIntelligenceChatClient.ToNative(content);

		// Fast path: the native CGImage flows through and the byte payload is skipped.
		Assert.NotNull(native.CgImage);
		Assert.Null(native.Data);
		Assert.Null(native.Orientation);
	}

	// UIImage orientation must map to CGImagePropertyOrientation on Apple's native-handle fast path.
	[Fact]
	public void ToNative_DataContentWithUIImage_UsesNativeImage()
	{
		using var image = CreateTestImage();
		using var uiImage = new UIImage(image, 1, UIImageOrientation.Right);
		var content = new DataContent(new byte[] { 0 }, "image/png") { RawRepresentation = uiImage };

		var native = AppleIntelligenceChatClient.ToNative(content);

		Assert.NotNull(native.CgImage);
		Assert.Null(native.Data);
		Assert.Equal(CGImagePropertyOrientation.Right, native.Orientation);
	}

	// UIImage-backed UriContent uses Apple's handle/orientation path instead of URL loading.
	[Fact]
	public void ToNative_UriContentWithUIImage_PreservesOrientation()
	{
		using var image = CreateTestImage();
		using var uiImage = new UIImage(image, 1, UIImageOrientation.LeftMirrored);
		var content = new UriContent("file:///image.png", "image/png") { RawRepresentation = uiImage };

		var native = AppleIntelligenceChatClient.ToNative(content);

		Assert.NotNull(native.CgImage);
		Assert.Null(native.ImageUrl);
		Assert.Equal(CGImagePropertyOrientation.LeftMirrored, native.Orientation);
	}

	// Asserts NSUrl marshalling into the Apple bridge's ImageContentNative.ImageUrl.
	[Fact]
	public void ToNative_FileUri_ProducesImageUrl()
	{
		WithImageFile(CreatePngBytes(), path =>
		{
			var content = new UriContent(new Uri(path), "image/png");

			var native = AppleIntelligenceChatClient.ToNative(content);

			Assert.NotNull(native.ImageUrl);
			Assert.Equal(path, native.ImageUrl!.Path);
		});
	}

	// Apple's native image marshaller rejects remote URLs; providers that fetch image URLs have a different policy.
	[Fact]
	public void ToNative_HttpUri_ThrowsNotSupported()
	{
		var content = new UriContent("https://example.com/image.png", "image/png");

		Assert.Throws<NotSupportedException>(() => AppleIntelligenceChatClient.ToNative(content));
	}

	// Apple's ImageIO decoder validates supported image payloads before they reach native generation.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ToNative_EmptyOrMalformedImageBytes_ThrowsArgumentException(bool empty)
	{
		var content = new DataContent(empty ? [] : new byte[] { 1, 2, 3, 4 }, "image/png");

		var error = Assert.Throws<ArgumentException>(() => AppleIntelligenceChatClient.ToNative(content));

		Assert.Equal("data", error.ParamName);
		Assert.Contains("empty or malformed", error.Message, StringComparison.Ordinal);
	}

	// Undecodable image media is unsupported, distinct from malformed bytes for a supported format such as PNG.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ToNative_UnsupportedEncodedImageMedia_ThrowsNotSupported(bool useUri)
	{
		var error = Assert.Throws<NotSupportedException>(() => useUri
			? AppleIntelligenceChatClient.ToNative(new UriContent("file:///image.unknown", "image/x-essentials-unsupported"))
			: AppleIntelligenceChatClient.ToNative(new DataContent(CreatePngBytes(), "image/x-essentials-unsupported")));

		Assert.Contains("image/x-essentials-unsupported", error.Message, StringComparison.Ordinal);
	}

	// Apple rejects unsupported native representations rather than pretending a supplied image was mapped.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ToNative_UnsupportedImageRepresentation_ThrowsNotSupported(bool useUri)
	{
		AIContent content = useUri
			? new UriContent("file:///image.png", "image/png")
			: new DataContent(CreatePngBytes(), "image/png");
		content.RawRepresentation = new object();

		var error = Assert.Throws<NotSupportedException>(() => useUri
			? AppleIntelligenceChatClient.ToNative((UriContent)content)
			: AppleIntelligenceChatClient.ToNative((DataContent)content));

		Assert.Contains("RawRepresentation", error.Message, StringComparison.Ordinal);
	}

	// A recognized UIImage without a CGImage has a malformed supported payload, not an unknown content type.
	[Fact]
	public async Task ToNative_EmptyUIImage_ThrowsArgumentException()
	{
		await MainThread.InvokeOnMainThreadAsync(() =>
		{
			using var image = new UIImage();
			var content = new DataContent(CreatePngBytes(), "image/png") { RawRepresentation = image };

			var error = Assert.Throws<ArgumentException>(() => AppleIntelligenceChatClient.ToNative(content));

			Assert.Contains("no usable image payload", error.Message, StringComparison.Ordinal);
		});
	}

	// Apple's file:// input is supported only when ImageIO can decode its payload.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ToNative_EmptyOrMalformedImageFile_ThrowsArgumentException(bool empty)
	{
		WithImageFile(empty ? [] : new byte[] { 1, 2, 3, 4 }, path =>
		{
			var error = Assert.Throws<ArgumentException>(() =>
				AppleIntelligenceChatClient.ToNative(new UriContent(new Uri(path), "image/png")));
			Assert.Equal("uri", error.ParamName);
			Assert.Contains("malformed", error.Message, StringComparison.Ordinal);
		});
	}

	// Missing local images are invalid supported input, unlike unsupported remote URL representations.
	[Fact]
	public void ToNative_MissingImageFile_ThrowsArgumentException()
	{
		var path = Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, $"missing-{Guid.NewGuid():N}.png");
		var error = Assert.Throws<ArgumentException>(() =>
			AppleIntelligenceChatClient.ToNative(new UriContent(new Uri(path), "image/png")));

		Assert.Equal("uri", error.ParamName);
		Assert.Contains("missing", error.Message, StringComparison.Ordinal);
	}

	// Both public APIs reject malformed image payloads before Foundation Models generation; OS 26 first rejects image capability.
	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public async Task MalformedImage_RejectsBeforeInference(bool empty, bool streaming)
	{
		using var client = new AppleIntelligenceChatClient();
		ChatMessage[] messages = [new(ChatRole.User,
			[new TextContent("Describe this."), new DataContent(empty ? [] : new byte[] { 0 }, "image/png")])];
		var error = await Assert.ThrowsAnyAsync<Exception>(async () =>
		{
			if (streaming)
				await client.GetStreamingResponseAsync(messages).ToChatResponseAsync();
			else
				await client.GetResponseAsync(messages);
		});

		if (OperatingSystem.IsIOSVersionAtLeast(27) || OperatingSystem.IsMacCatalystVersionAtLeast(27))
			Assert.IsType<ArgumentException>(error);
		else
			Assert.IsType<NotSupportedException>(error);
	}

	// Converts Apple's ImageContentNative/CGImage to PNG while retaining a native handle; other providers cannot expose CGImage.
	[Fact]
	public void FromNative_CGImage_ProducesDecodablePng()
	{
		using var image = CreateTestImage();
		var native = new ImageContentNative(image, default, null);

		var content = AppleIntelligenceChatClient.FromNative(native);

		var data = Assert.IsType<DataContent>(content);
		Assert.Equal("image/png", data.MediaType);

		var bytes = data.Data.ToArray();
		Assert.True(bytes.Length > 8);
		Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]); // PNG signature
		Assert.NotNull(data.RawRepresentation);
	}

	// Invalid CGImagePropertyOrientation values are an Apple EXIF-marshalling error, not a common chat error shape.
	[Theory]
	[InlineData(0)]
	[InlineData(9)]
	public void FromNative_InvalidOrientation_Throws(int orientation)
	{
		using var image = CreateTestImage();
		var native = new ImageContentNative(image, (CGImagePropertyOrientation)orientation, null);

		var error = Assert.Throws<InvalidDataException>(() => AppleIntelligenceChatClient.FromNative(native));
		Assert.Contains("Unsupported EXIF orientation", error.Message);
	}

	// Verifies CGImage dimensions survive Apple's private native-image conversion round trip.
	[Fact]
	public void RoundTrip_CGImage_SurvivesConversion()
	{
		using var image = CreateTestImage(3, 3);

		var native = AppleIntelligenceChatClient.ToNative(
			new DataContent(new byte[] { 0 }, "image/png") { RawRepresentation = image });
		Assert.NotNull(native.CgImage);

		var back = AppleIntelligenceChatClient.FromNative(native);
		var data = Assert.IsType<DataContent>(back);

		var decoded = data.RawRepresentation as CGImage;
		Assert.NotNull(decoded);
		Assert.Equal((nint)3, decoded!.Width);
		Assert.Equal((nint)3, decoded.Height);
	}

	// Apple's ImageIO/CGImage conversion must normalize both PNG pixels and retained native pixels for EXIF orientation.
	[Theory]
	[InlineData(CGImagePropertyOrientation.UpMirrored, 2, 3)]
	[InlineData(CGImagePropertyOrientation.Right, 3, 2)]
	[InlineData(CGImagePropertyOrientation.Left, 3, 2)]
	public void FromNative_OrientedImage_NormalizesPortableAndNativePixels(CGImagePropertyOrientation orientation, int width, int height)
	{
		using var image = CreateTestImage(2, 3);
		var native = new ImageContentNative(image, orientation, null);

		var portable = Assert.IsType<DataContent>(AppleIntelligenceChatClient.FromNative(native));
		var oriented = Assert.IsType<CGImage>(portable.RawRepresentation);
		Assert.Equal((nint)width, oriented.Width);
		Assert.Equal((nint)height, oriented.Height);
		Assert.Null(AppleIntelligenceChatClient.ToNative(portable).Orientation);

		using var png = NSData.FromArray(portable.Data.ToArray());
		using var source = CGImageSource.FromData(png);
		using var decoded = source!.CreateImage(0, new CGImageOptions());
		Assert.Equal((nint)width, decoded!.Width);
		Assert.Equal((nint)height, decoded.Height);
	}

	// Tests NSUrl-backed ImageContentNative orientation normalization into CGImage pixels and PNG.
	[Fact]
	public void FromNative_OrientedFile_NormalizesPixels()
	{
		using var image = CreateTestImage(2, 3);
		using var png = new NSMutableData();
		using var destination = CGImageDestination.Create(png, "public.png", 1)!;
		destination.AddImage(image);
		Assert.True(destination.Close());

		WithImageFile(png.ToArray(), path =>
		{
			var native = new ImageContentNative(NSUrl.FromFilename(path), CGImagePropertyOrientation.Right, null);

			var portable = Assert.IsType<DataContent>(AppleIntelligenceChatClient.FromNative(native));
			var oriented = Assert.IsType<CGImage>(portable.RawRepresentation);
			Assert.Equal((nint)3, oriented.Width);
			Assert.Equal((nint)2, oriented.Height);
			Assert.Equal("image/png", portable.MediaType);
		});
	}

	// A native ImageContentNative with no NSData/CGImage/NSUrl must fail Apple's image marshalling validation.
	[Fact]
	public void FromNative_EmptyImage_Throws()
	{
		var native = new ImageContentNative(NSData.FromArray([]), "image/png", default, null)
		{
			Data = null,
		};

		Assert.Throws<InvalidDataException>(() => AppleIntelligenceChatClient.FromNative(native));
	}

	// Apple's inline transcript supports images in all non-Tool roles, but older OS versions must reject rather than omit them.
	[Theory]
	[InlineData("user", false)]
	[InlineData("assistant", false)]
	[InlineData("system", false)]
	[InlineData("user", true)]
	[InlineData("assistant", true)]
	[InlineData("system", true)]
	public void ToNative_WithImageContent_ConvertsOrRejectsUnsupportedRuntime(string role, bool useUri)
	{
		using var image = CreateTestImage();
		AIContent content = useUri
			? new UriContent("file:///image.png", "image/png") { RawRepresentation = image }
			: new DataContent(new byte[] { 0 }, "image/png") { RawRepresentation = image };
		var message = new ChatMessage(new ChatRole(role), [new TextContent("Hello"), content]);
		var supportsImages = OperatingSystem.IsIOSVersionAtLeast(27) ||
			OperatingSystem.IsMacCatalystVersionAtLeast(27) ||
			OperatingSystem.IsMacOSVersionAtLeast(27);

		if (!supportsImages)
		{
			Assert.Throws<NotSupportedException>(() => AppleIntelligenceChatClient.ToNative([message], null));
			return;
		}

		var native = Assert.Single(AppleIntelligenceChatClient.ToNative([message], null));
		Assert.Contains(native.Contents, item => item is TextContentNative text && text.Text == "Hello");
		Assert.Single(native.Contents.OfType<ImageContentNative>());
	}

	// Apple's unsupported URL representation must never disappear beside valid text, on either runtime branch.
	[Fact]
	public void ToNative_WithHttpImageUri_RejectsInsteadOfOmitting()
	{
		var message = new ChatMessage(ChatRole.User,
			[new TextContent("Hello"), new UriContent("https://example.com/x.png", "image/png")]);

		Assert.Throws<NotSupportedException>(() => AppleIntelligenceChatClient.ToNative([message], null));
	}

	// Foundation Models before OS 27 cannot represent image input, even alongside supported text.
	[Theory]
	[InlineData(false, false)]
	[InlineData(true, false)]
	[InlineData(false, true)]
	[InlineData(true, true)]
	public void ToNative_WithImageOnOlderOS_Throws(bool assistantHistory, bool useUri)
	{
		if (OperatingSystem.IsIOSVersionAtLeast(27) ||
			OperatingSystem.IsMacCatalystVersionAtLeast(27) ||
			OperatingSystem.IsMacOSVersionAtLeast(27))
			return;

		AIContent content = useUri
			? new UriContent("file:///image.png", "image/png")
			: new DataContent(new byte[] { 0 }, "image/png");
		ChatMessage[] messages = assistantHistory
			? [new(ChatRole.User, "Describe this image."),
				new(ChatRole.Assistant, [new TextContent("An image."), content]),
				new(ChatRole.User, "What color was it?")]
			: [new(ChatRole.User, [new TextContent("Describe this image."), content])];

		Assert.Throws<NotSupportedException>(() => AppleIntelligenceChatClient.ToNative(messages, null));
	}

	// Requires Apple's OS 27 vision model and CGImage RawRepresentation rather than cloud-encoded images.
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithImageAttachment_ReturnsDescription()
	{
		Assert.True(
			OperatingSystem.IsMacCatalystVersionAtLeast(27) ||
			OperatingSystem.IsIOSVersionAtLeast(27) ||
			OperatingSystem.IsMacOSVersionAtLeast(27),
			"Live image inference requires an OS 27 runtime and a ready vision-capable model, not just the 27 SDK.");

		using var image = CreateTestImage(64, 64);
		var client = new AppleIntelligenceChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User,
			[
				new TextContent("What is the dominant basic color of this image? Answer with one English color word."),
				new DataContent(new byte[] { 0 }, "image/png") { RawRepresentation = image },
			]),
		};

		var response = await client.GetResponseAsync(messages);

		Assert.Contains("red", response.Text, StringComparison.OrdinalIgnoreCase);
	}
}
#endif
