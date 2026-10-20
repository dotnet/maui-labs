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
		var bytes = new byte[] { 1, 2, 3, 4 };
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
		var content = new UriContent("file:///tmp/image.png", "image/png") { RawRepresentation = uiImage };

		var native = AppleIntelligenceChatClient.ToNative(content);

		Assert.NotNull(native.CgImage);
		Assert.Null(native.ImageUrl);
		Assert.Equal(CGImagePropertyOrientation.LeftMirrored, native.Orientation);
	}

	// Asserts NSUrl marshalling into the Apple bridge's ImageContentNative.ImageUrl.
	[Fact]
	public void ToNative_FileUri_ProducesImageUrl()
	{
		var content = new UriContent("file:///tmp/example.png", "image/png");

		var native = AppleIntelligenceChatClient.ToNative(content);

		Assert.NotNull(native.ImageUrl);
		Assert.EndsWith("example.png", native.ImageUrl!.Path!);
	}

	// Apple's native image marshaller rejects remote URLs; providers that fetch image URLs have a different policy.
	[Fact]
	public void ToNative_HttpUri_ThrowsNotSupported()
	{
		var content = new UriContent("https://example.com/image.png", "image/png");

		Assert.Throws<NotSupportedException>(() => AppleIntelligenceChatClient.ToNative(content));
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

	// Apple maps images only where native image APIs exist, preserving text when images cannot be mapped.
	[Theory]
	[InlineData("user", false)]
	[InlineData("assistant", false)]
	[InlineData("user", true)]
	public void ToNative_WithImageContent_ConvertsOrIgnoresUnsupportedContent(string role, bool useUri)
	{
		using var image = CreateTestImage();
		AIContent content = useUri
			? new UriContent("file:///tmp/image.png", "image/png") { RawRepresentation = image }
			: new DataContent(new byte[] { 0 }, "image/png") { RawRepresentation = image };
		var message = new ChatMessage(new ChatRole(role), [new TextContent("Hello"), content]);
		var supportsImages = OperatingSystem.IsIOSVersionAtLeast(27) ||
			OperatingSystem.IsMacCatalystVersionAtLeast(27) ||
			OperatingSystem.IsMacOSVersionAtLeast(27);

		var native = Assert.Single(AppleIntelligenceChatClient.ToNative([message], null));
		Assert.Contains(native.Contents, item => item is TextContentNative text && text.Text == "Hello");
		Assert.Equal(supportsImages ? 1 : 0, native.Contents.OfType<ImageContentNative>().Count());
	}

	// Once image input is supported, invalid URL payloads remain errors rather than unsupported-content no-ops.
	[Fact]
	public void ToNative_WithHttpImageUri_IgnoresUnavailableImagesOrRejectsUnsupportedUrl()
	{
		var message = new ChatMessage(ChatRole.User,
			[new TextContent("Hello"), new UriContent("https://example.com/x.png", "image/png")]);

		if (OperatingSystem.IsIOSVersionAtLeast(27) ||
			OperatingSystem.IsMacCatalystVersionAtLeast(27) ||
			OperatingSystem.IsMacOSVersionAtLeast(27))
			Assert.Throws<NotSupportedException>(() => AppleIntelligenceChatClient.ToNative([message], null));
		else
			Assert.IsType<TextContentNative>(Assert.Single(
				Assert.Single(AppleIntelligenceChatClient.ToNative([message], null)).Contents));
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
