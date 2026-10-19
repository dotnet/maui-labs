#if IOS || MACCATALYST
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class AppleIntelligenceChatClientSamplingTests
{
	[Fact]
	public void ToNative_SamplingHints_PreservesStandardOptions()
	{
		using var client = new AppleIntelligenceChatClient();
		using var native = client.ToNative(new ChatOptions
		{
			TopP = 0.75f, Temperature = 0.5f, Seed = -42, MaxOutputTokens = 100
		}, CancellationToken.None)!;
		Assert.Equal(0.75, native.TopP!.DoubleValue);
		Assert.Equal(0.5, native.Temperature!.DoubleValue);
		Assert.Equal(-42, native.Seed!.Int64Value);
		Assert.Equal(100, native.MaxOutputTokens!.Int32Value);
		Assert.Null(native.TopK);
	}

	[Fact]
	public void ToNative_DefaultOptions_DoesNotIntroduceSamplingHints()
	{
		using var client = new AppleIntelligenceChatClient();
		Assert.Null(client.ToNative(null, CancellationToken.None));
		using var native = client.ToNative(new ChatOptions(), CancellationToken.None)!;
		Assert.Null(native.TopP);
		Assert.Null(native.TopK);
		Assert.Null(native.Temperature);
		Assert.Null(native.Seed);
		Assert.Null(native.MaxOutputTokens);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task TopKAndTopP_Together_ThrowsArgumentExceptionBeforeNativeInvocation(bool streaming)
	{
		using var client = new AppleIntelligenceChatClient();
		var options = new ChatOptions { TopK = 20, TopP = 0.9f };
		ChatMessage[] messages = [new(ChatRole.User, "Hello")];
		var error = streaming
			? await Assert.ThrowsAsync<ArgumentException>(async () =>
			{
				await foreach (var _ in client.GetStreamingResponseAsync(messages, options)) { }
			})
			: await Assert.ThrowsAsync<ArgumentException>(() => client.GetResponseAsync(messages, options));
		Assert.Equal("options", error.ParamName);
		Assert.Contains("Apple", error.Message);
		Assert.Contains("either TopK or TopP", error.Message);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void ToNative_IndividualTopKOrTopP_AcceptsEither(bool topK)
	{
		using var client = new AppleIntelligenceChatClient();
		var options = topK ? new ChatOptions { TopK = 20 } : new ChatOptions { TopP = 0.9f };
		using var native = client.ToNative(options, CancellationToken.None)!;
		if (topK)
		{
			Assert.Equal(20, native.TopK!.Int32Value);
			Assert.Null(native.TopP);
		}
		else
		{
			Assert.Equal((double)0.9f, native.TopP!.DoubleValue);
			Assert.Null(native.TopK);
		}
	}

	[Theory]
	[InlineData(0f)]
	[InlineData(1f)]
	public void ToNative_TopPBoundary_PreservesValue(float topP)
	{
		using var client = new AppleIntelligenceChatClient();
		using var native = client.ToNative(new ChatOptions { TopP = topP }, CancellationToken.None)!;
		Assert.Equal((double)topP, native.TopP!.DoubleValue);
	}

	[Theory]
	[InlineData(-1f)]
	[InlineData(1.01f)]
	[InlineData(float.NaN)]
	[InlineData(float.PositiveInfinity)]
	[InlineData(float.NegativeInfinity)]
	public void ToNative_InvalidTopP_ThrowsBeforeModel(float topP)
	{
		using var client = new AppleIntelligenceChatClient();
		var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
			client.ToNative(new ChatOptions { TopP = topP }, CancellationToken.None));
		Assert.Equal("options", error.ParamName);
	}

	[Theory]
	[InlineData(-1f)]
	[InlineData(float.NaN)]
	[InlineData(float.PositiveInfinity)]
	[InlineData(float.NegativeInfinity)]
	public void ToNative_InvalidTemperature_ThrowsBeforeModel(float temperature)
	{
		using var client = new AppleIntelligenceChatClient();
		var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
			client.ToNative(new ChatOptions { Temperature = temperature }, CancellationToken.None));
		Assert.Equal("options", error.ParamName);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void ToNative_InvalidTopK_ThrowsBeforeModel(int topK)
	{
		using var client = new AppleIntelligenceChatClient();
		var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
			client.ToNative(new ChatOptions { TopK = topK }, CancellationToken.None));
		Assert.Equal("options", error.ParamName);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void ToNative_InvalidMaxOutputTokens_ThrowsBeforeModel(int maxOutputTokens)
	{
		using var client = new AppleIntelligenceChatClient();
		var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
			client.ToNative(new ChatOptions { MaxOutputTokens = maxOutputTokens }, CancellationToken.None));
		Assert.Equal("options", error.ParamName);
	}
}
#endif
