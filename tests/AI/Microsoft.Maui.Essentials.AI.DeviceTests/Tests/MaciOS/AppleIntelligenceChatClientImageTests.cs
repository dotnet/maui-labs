#if IOS || MACCATALYST
using CoreGraphics;
using CoreImage;
using CoreVideo;
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
public class AppleIntelligenceChatClientImageTests
{
	static CGImage CreateTestImage(int width = 2, int height = 2, bool green = false)
	{
		var bytesPerRow = width * 4;
		var buffer = new byte[bytesPerRow * height];
		using var colorSpace = CGColorSpace.CreateDeviceRGB();
		using var context = new CGBitmapContext(buffer, width, height, 8, bytesPerRow, colorSpace, CGImageAlphaInfo.PremultipliedLast);
		context.SetFillColor(new CGColor(green ? 0f : 1f, green ? 1f : 0f, 0f, 1f));
		context.FillRect(new CGRect(0, 0, width, height));
		return context.ToImage()!;
	}

	[Fact]
	public void IsImage_DetectsImageMediaTypes()
	{
		Assert.True(AppleIntelligenceChatClient.IsImage("image/png"));
		Assert.True(AppleIntelligenceChatClient.IsImage("IMAGE/JPEG"));
		Assert.False(AppleIntelligenceChatClient.IsImage("application/pdf"));
		Assert.False(AppleIntelligenceChatClient.IsImage(null));
	}

	[Fact]
	public void ToNative_DataContentImage_CarriesBytesAndMediaType()
	{
		var bytes = new byte[] { 1, 2, 3, 4 };
		var content = new DataContent(bytes, "image/png");

		var native = AppleIntelligenceChatClient.ToNative(content);

		Assert.NotNull(native.Data);
		Assert.Equal(bytes, native.Data!.ToArray());
		Assert.Equal("image/png", native.MimeType);
		Assert.Null(native.CgImage);
	}

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

	[Fact]
	public void ToNative_CoreImageRawRepresentation_RetainsNativeSource()
	{
		using var cg = CreateTestImage(3, 2);
		var ci = CIImage.FromCGImage(cg);
		var handle = ci.Handle;
		using var portableSource = new ImageContentNative(cg, default, null);
		var content = Assert.IsType<DataContent>(AppleIntelligenceChatClient.FromNative(portableSource));
		content.RawRepresentation = ci;
		using var native = AppleIntelligenceChatClient.ToNative(content);
		ci.Dispose();

		Assert.Equal(handle, native.CiImage!.Handle);
		Assert.Null(native.CgImage);
		Assert.Null(native.Data);
		Assert.Null(native.ImageUrl);
		AssertPortableImage(native, 3, 2);
	}

	[Fact]
	public void ToNative_CoreImageUriRawRepresentation_RetainsNativeSource()
	{
		using var cg = CreateTestImage(3, 2);
		var ci = CIImage.FromCGImage(cg);
		var handle = ci.Handle;
		using var native = AppleIntelligenceChatClient.ToNative(
			new UriContent("file:///image.png", "image/png") { RawRepresentation = ci });
		ci.Dispose();

		Assert.Equal(handle, native.CiImage!.Handle);
		Assert.Null(native.ImageUrl);
		AssertPortableImage(native, 3, 2);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ToNative_PixelBufferRawRepresentation_RetainsNativeStorage(bool uri)
	{
		using var cg = CreateTestImage(3, 2, green: true);
		using var ci = CIImage.FromCGImage(cg);
		var buffer = new CVPixelBuffer(3, 2, CVPixelFormatType.CV32BGRA);
		using var context = new CIContext();
		context.Render(ci, buffer);
		var handle = buffer.Handle;
		using var portableSource = new ImageContentNative(cg, default, null);
		var content = Assert.IsType<DataContent>(AppleIntelligenceChatClient.FromNative(portableSource));
		content.RawRepresentation = buffer;
		using var native = uri
			? AppleIntelligenceChatClient.ToNative(new UriContent("file:///image.png", "image/png") { RawRepresentation = buffer })
			: AppleIntelligenceChatClient.ToNative(content);
		buffer.Dispose();

		Assert.Equal(handle, native.PixelBuffer!.Handle);
		Assert.Null(native.CgImage);
		Assert.Null(native.Data);
		Assert.Null(native.ImageUrl);
		AssertPortableImage(native, 3, 2);
	}

	[Theory]
	[InlineData(UIImageOrientation.Up, CGImagePropertyOrientation.Up, 2, 3)]
	[InlineData(UIImageOrientation.UpMirrored, CGImagePropertyOrientation.UpMirrored, 2, 3)]
	[InlineData(UIImageOrientation.Down, CGImagePropertyOrientation.Down, 2, 3)]
	[InlineData(UIImageOrientation.DownMirrored, CGImagePropertyOrientation.DownMirrored, 2, 3)]
	[InlineData(UIImageOrientation.LeftMirrored, CGImagePropertyOrientation.LeftMirrored, 3, 2)]
	[InlineData(UIImageOrientation.Right, CGImagePropertyOrientation.Right, 3, 2)]
	[InlineData(UIImageOrientation.RightMirrored, CGImagePropertyOrientation.RightMirrored, 3, 2)]
	[InlineData(UIImageOrientation.Left, CGImagePropertyOrientation.Left, 3, 2)]
	public void ToNative_UIImageWithOnlyCIImage_PreservesOrientation(
		UIImageOrientation orientation, CGImagePropertyOrientation expectedOrientation, int width, int height)
	{
		using var cg = CreateTestImage(2, 3);
		using var ci = CIImage.FromCGImage(cg);
		using var ui = new UIImage(ci, 1, orientation);
		using var portableSource = new ImageContentNative(cg, default, null);
		var content = Assert.IsType<DataContent>(AppleIntelligenceChatClient.FromNative(portableSource));
		content.RawRepresentation = ui;
		using var native = AppleIntelligenceChatClient.ToNative(content);
		Assert.NotNull(native.CiImage);
		Assert.Null(native.CgImage);
		Assert.Equal(expectedOrientation, native.Orientation);
		AssertPortableImage(native, width, height);
	}

	static void AssertPortableImage(ImageContentNative native, int width, int height)
	{
		var portable = Assert.IsType<DataContent>(AppleIntelligenceChatClient.FromNative(native));
		using var rendered = Assert.IsType<CGImage>(portable.RawRepresentation);
		Assert.Equal((nint)width, rendered.Width);
		Assert.Equal((nint)height, rendered.Height);
		Assert.Equal("image/png", portable.MediaType);
		using var png = NSData.FromArray(portable.Data.ToArray());
		using var source = CGImageSource.FromData(png);
		using var decoded = source!.CreateImage(0, new CGImageOptions());
		Assert.Equal((nint)width, decoded!.Width);
		Assert.Equal((nint)height, decoded.Height);
		Assert.Null(AppleIntelligenceChatClient.ToNative(portable).Orientation);
	}

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

	[Fact]
	public void ToNative_UriContentWithUIImage_PreservesOrientation()
	{
		using var image = CreateTestImage();
		using var uiImage = new UIImage(image, 1, UIImageOrientation.LeftMirrored);
		var content = new UriContent("file:///tmp/image.png", "image/png") { RawRepresentation = uiImage };

		var native = AppleIntelligenceChatClient.ToNative(content);

		Assert.NotNull(native.CgImage);
		Assert.Null(native.ImageUrl);
		Assert.Equal(CGImagePropertyOrientation.LeftMirrored, native.Orientation);
	}

	[Fact]
	public void ToNative_FileUri_ProducesImageUrl()
	{
		var content = new UriContent("file:///tmp/example.png", "image/png");

		var native = AppleIntelligenceChatClient.ToNative(content);

		Assert.NotNull(native.ImageUrl);
		Assert.EndsWith("example.png", native.ImageUrl!.Path!);
	}

	[Fact]
	public void ToNative_HttpUri_ThrowsNotSupported()
	{
		var content = new UriContent("https://example.com/image.png", "image/png");

		Assert.Throws<NotSupportedException>(() => AppleIntelligenceChatClient.ToNative(content));
	}

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

	[Fact]
	public void FromNative_OrientedFile_NormalizesPixels()
	{
		using var image = CreateTestImage(2, 3);
		using var png = new NSMutableData();
		using var destination = CGImageDestination.Create(png, "public.png", 1)!;
		destination.AddImage(image);
		Assert.True(destination.Close());

		var path = Path.GetTempFileName();
		try
		{
			File.WriteAllBytes(path, png.ToArray());
			var native = new ImageContentNative(NSUrl.FromFilename(path), CGImagePropertyOrientation.Right, null);

			var portable = Assert.IsType<DataContent>(AppleIntelligenceChatClient.FromNative(native));
			var oriented = Assert.IsType<CGImage>(portable.RawRepresentation);
			Assert.Equal((nint)3, oriented.Width);
			Assert.Equal((nint)2, oriented.Height);
			Assert.Equal("image/png", portable.MediaType);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public void FromNative_EmptyImage_Throws()
	{
		var native = new ImageContentNative(NSData.FromArray([]), "image/png", default, null)
		{
			Data = null,
		};

		Assert.Throws<InvalidDataException>(() => AppleIntelligenceChatClient.FromNative(native));
	}

	[Fact]
	public async Task GetResponseAsync_WithImageOnOlderOS_ReportsUnsupported()
	{
		if (OperatingSystem.IsIOSVersionAtLeast(27) || OperatingSystem.IsMacCatalystVersionAtLeast(27))
			return;

		using var image = CreateTestImage();
		var client = new AppleIntelligenceChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User,
			[
				new TextContent("Describe this image."),
				new DataContent(new byte[] { 0 }, "image/png") { RawRepresentation = image },
			]),
		};

		var error = await Assert.ThrowsAsync<NSErrorException>(() => client.GetResponseAsync(messages));
		Assert.Contains("Image input requires iOS, Mac Catalyst, or macOS 27.0 or later.", error.Message);
	}

	[Fact]
	public async Task GetStreamingResponseAsync_WithImageOnOlderOS_ReportsUnsupported()
	{
		if (OperatingSystem.IsIOSVersionAtLeast(27) || OperatingSystem.IsMacCatalystVersionAtLeast(27))
			return;

		using var image = CreateTestImage();
		var client = new AppleIntelligenceChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User,
			[
				new TextContent("Describe this image."),
				new DataContent(new byte[] { 0 }, "image/png") { RawRepresentation = image },
			]),
		};

		var error = await Assert.ThrowsAsync<NSErrorException>(async () =>
		{
			await foreach (var _ in client.GetStreamingResponseAsync(messages))
			{
			}
		});
		Assert.Contains("Image input requires iOS, Mac Catalyst, or macOS 27.0 or later.", error.Message);
	}

	[Fact]
	public async Task GetResponseAsync_WithImageInAssistantHistoryOnOlderOS_ReportsUnsupported()
	{
		if (OperatingSystem.IsIOSVersionAtLeast(27) || OperatingSystem.IsMacCatalystVersionAtLeast(27))
			return;

		using var image = CreateTestImage();
		var client = new AppleIntelligenceChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "What is in this image?"),
			new(ChatRole.Assistant,
			[
				new TextContent("Here is the image."),
				new DataContent(new byte[] { 0 }, "image/png") { RawRepresentation = image },
			]),
			new(ChatRole.User, "What color was it?"),
		};

		var error = await Assert.ThrowsAsync<NSErrorException>(() => client.GetResponseAsync(messages));
		Assert.Contains("Image input requires iOS, Mac Catalyst, or macOS 27.0 or later.", error.Message);
	}

	[Fact]
	public async Task GetResponseAsync_WithImageFileOnOlderOS_ReportsUnsupported()
	{
		if (OperatingSystem.IsIOSVersionAtLeast(27) || OperatingSystem.IsMacCatalystVersionAtLeast(27))
			return;

		var client = new AppleIntelligenceChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User,
			[
				new TextContent("Describe this image."),
				new UriContent("file:///tmp/image.png", "image/png"),
			]),
		};

		var error = await Assert.ThrowsAsync<NSErrorException>(() => client.GetResponseAsync(messages));
		Assert.Contains("Image input requires iOS, Mac Catalyst, or macOS 27.0 or later.", error.Message);
	}

	[Fact]
	public async Task GetResponseAsync_WithHttpImageUri_ThrowsBeforeModel()
	{
		var client = new AppleIntelligenceChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User,
			[
				new TextContent("Describe this image."),
				new UriContent("https://example.com/x.png", "image/png"),
			]),
		};

		// Conversion runs before any native model call, so this fails fast without a model.
		await Assert.ThrowsAsync<NotSupportedException>(() => client.GetResponseAsync(messages));
	}

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

	[Theory]
	[InlineData(false, false, false)]
	[InlineData(false, false, true)]
	[InlineData(false, true, false)]
	[InlineData(false, true, true)]
	[InlineData(true, false, false)]
	[InlineData(true, false, true)]
	[InlineData(true, true, false)]
	[InlineData(true, true, true)]
	public async Task NativeImageRawRepresentation_OnOlderOS_ReportsUnsupported(bool pixelBuffer, bool streaming, bool history)
	{
		if (OperatingSystem.IsMacCatalystVersionAtLeast(27) || OperatingSystem.IsIOSVersionAtLeast(27))
			return;

		using var image = CreateTestImage();
		using var ci = CIImage.FromCGImage(image);
		using var buffer = new CVPixelBuffer(2, 2, CVPixelFormatType.CV32BGRA);
		using var native = new ImageContentNative(image, default, null);
		var content = Assert.IsType<DataContent>(AppleIntelligenceChatClient.FromNative(native));
		content.RawRepresentation = pixelBuffer ? buffer : ci;
		var messages = new List<ChatMessage> { new(ChatRole.User, [new TextContent("Describe this image."), content]) };
		if (history)
			messages.Add(new(ChatRole.User, "What color was that image?"));
		using var client = new AppleIntelligenceChatClient();
		var error = streaming
			? await Assert.ThrowsAsync<NSErrorException>(async () =>
			{
				await foreach (var _ in client.GetStreamingResponseAsync(messages)) { }
			})
			: await Assert.ThrowsAsync<NSErrorException>(() => client.GetResponseAsync(messages));
		Assert.Contains("Image input requires iOS, Mac Catalyst, or macOS 27.0 or later.", error.Message);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task NativeImageRawRepresentation_PromptAndStream_RecognizesImage(bool pixelBuffer)
	{
		Assert.True(OperatingSystem.IsMacCatalystVersionAtLeast(27) || OperatingSystem.IsIOSVersionAtLeast(27),
			"Native image inference requires OS 27 and a ready vision model.");
		using var cg = CreateTestImage(64, 64, green: pixelBuffer);
		using var ci = CIImage.FromCGImage(cg);
		using var buffer = new CVPixelBuffer(64, 64, CVPixelFormatType.CV32BGRA);
		using var context = new CIContext();
		context.Render(ci, buffer);
		using var client = new AppleIntelligenceChatClient();
		using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
		using var nativeImage = new ImageContentNative(cg, default, null);
		var content = Assert.IsType<DataContent>(AppleIntelligenceChatClient.FromNative(nativeImage));
		content.RawRepresentation = pixelBuffer ? buffer : ci;
		ChatMessage[] messages = [new(ChatRole.User,
		[
			new TextContent("What is the dominant basic color? Answer with one English color word."),
			content
		])];
		var expectedColor = pixelBuffer ? "green" : "red";
		var response = await client.GetResponseAsync(messages, cancellationToken: timeout.Token);
		Assert.Contains(expectedColor, response.Text, StringComparison.OrdinalIgnoreCase);

		var text = new System.Text.StringBuilder();
		await foreach (var update in client.GetStreamingResponseAsync(messages, cancellationToken: timeout.Token))
			text.Append(update.Text);
		Assert.Contains(expectedColor, text.ToString(), StringComparison.OrdinalIgnoreCase);
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task NativeImageRawRepresentation_DisposedAfterSubmission_RemainsAliveDuringGeneration(bool pixelBuffer, bool streaming)
	{
		Assert.True(OperatingSystem.IsMacCatalystVersionAtLeast(27) || OperatingSystem.IsIOSVersionAtLeast(27),
			"Native image inference requires OS 27 and a ready vision model.");
		using var cg = CreateTestImage(64, 64, green: pixelBuffer);
		using var ci = CIImage.FromCGImage(cg);
		using var buffer = new CVPixelBuffer(64, 64, CVPixelFormatType.CV32BGRA);
		using var context = new CIContext();
		context.Render(ci, buffer);
		using var native = new ImageContentNative(cg, default, null);
		var content = Assert.IsType<DataContent>(AppleIntelligenceChatClient.FromNative(native));
		IDisposable source = pixelBuffer ? buffer : ci;
		content.RawRepresentation = source;
		ChatMessage[] messages = [new(ChatRole.User,
		[
			new TextContent("What is the dominant basic color? Answer with one English color word."),
			content
		])];
		using var client = new AppleIntelligenceChatClient();
		using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
		var text = new System.Text.StringBuilder();
		if (streaming)
		{
			await using var enumerator = client.GetStreamingResponseAsync(messages, cancellationToken: timeout.Token)
				.GetAsyncEnumerator(timeout.Token);
			var pending = enumerator.MoveNextAsync();
			source.Dispose();
			GC.Collect();
			GC.WaitForPendingFinalizers();
			if (await pending)
				text.Append(enumerator.Current.Text);
			while (await enumerator.MoveNextAsync())
				text.Append(enumerator.Current.Text);
		}
		else
		{
			var pending = client.GetResponseAsync(messages, cancellationToken: timeout.Token);
			source.Dispose();
			GC.Collect();
			GC.WaitForPendingFinalizers();
			text.Append((await pending).Text);
		}
		Assert.Contains(pixelBuffer ? "green" : "red", text.ToString(), StringComparison.OrdinalIgnoreCase);
	}
}
#endif
