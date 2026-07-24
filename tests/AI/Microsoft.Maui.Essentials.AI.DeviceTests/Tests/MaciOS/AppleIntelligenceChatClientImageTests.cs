#if IOS || MACCATALYST
using CoreGraphics;
using Foundation;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

/// <summary>
/// Tests for multimodal image input on the Apple Intelligence chat client.
/// The conversion tests are model-free (they exercise only the managed ↔ native marshalling)
/// and run in CI; the end-to-end description test requires an on-device model.
/// </summary>
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
	public void ToNative_DataContentWithNativeHandle_UsesZeroCopyFastPath()
	{
		using var image = CreateTestImage();
		var content = new DataContent(new byte[] { 0 }, "image/png") { RawRepresentation = image };

		var native = AppleIntelligenceChatClient.ToNative(content);

		// Fast path: the native CGImage flows through and the byte payload is skipped.
		Assert.NotNull(native.CgImage);
		Assert.Null(native.Data);
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
		var native = new ImageContentNative(image, 0, null);

		var content = AppleIntelligenceChatClient.FromNative(native);

		var data = Assert.IsType<DataContent>(content);
		Assert.Equal("image/png", data.MediaType);

		var bytes = data.Data.ToArray();
		Assert.True(bytes.Length > 8);
		Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]); // PNG signature
		Assert.NotNull(data.RawRepresentation);
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
		// Multimodal image input needs the runtime OS to be 27.0+ (the 27.0 SDK only enables
		// compilation). Skip on older runtimes so this doesn't false-fail off-device.
		if (!OperatingSystem.IsMacCatalystVersionAtLeast(27) &&
			!OperatingSystem.IsIOSVersionAtLeast(27) &&
			!OperatingSystem.IsMacOSVersionAtLeast(27))
		{
			return;
		}

		using var image = CreateTestImage(64, 64);
		var client = new AppleIntelligenceChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User,
			[
				new TextContent("What is the dominant color of this image? Answer in one word."),
				new DataContent(new byte[] { 0 }, "image/png") { RawRepresentation = image },
			]),
		};

		var response = await client.GetResponseAsync(messages);

		Assert.False(string.IsNullOrWhiteSpace(response.Text));
	}
}
#endif
