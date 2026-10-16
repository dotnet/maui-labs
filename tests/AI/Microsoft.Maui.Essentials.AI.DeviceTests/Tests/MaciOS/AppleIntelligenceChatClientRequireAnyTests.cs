#if IOS || MACCATALYST
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class AppleIntelligenceChatClientRequireAnyTests
{
	static bool Supports27 => OperatingSystem.IsIOSVersionAtLeast(27) ||
		OperatingSystem.IsMacCatalystVersionAtLeast(27) || OperatingSystem.IsMacOSVersionAtLeast(27);

	[Fact]
	public void ToNative_RequireAny_UsesNativePolicyOrExplicitOlderOSError()
	{
		using var client = new AppleIntelligenceChatClient();
		var options = new ChatOptions
		{
			ToolMode = ChatToolMode.RequireAny,
			Tools = [AIFunctionFactory.Create(() => "result", "test_tool")]
		};
		if (!Supports27)
		{
			Assert.Throws<NotSupportedException>(() => client.ToNative(options, CancellationToken.None));
			return;
		}
		using var native = client.ToNative(options, CancellationToken.None)!;
		Assert.True(native.RequireAnyTool);
		Assert.Single(native.Tools!);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ToNative_RequireAnyWithoutTools_ThrowsBeforeModel(bool emptyTools)
	{
		using var client = new AppleIntelligenceChatClient();
		var options = new ChatOptions
		{
			ToolMode = ChatToolMode.RequireAny,
			Tools = emptyTools ? [] : null
		};
		if (!Supports27)
			Assert.Throws<NotSupportedException>(() => client.ToNative(options, CancellationToken.None));
		else
			Assert.Throws<ArgumentException>(() => client.ToNative(options, CancellationToken.None));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task RequireAnyOnOlderOS_ThrowsNotSupportedBeforeGeneration(bool streaming)
	{
		if (Supports27)
			return;
		using var client = new AppleIntelligenceChatClient();
		await Assert.ThrowsAsync<NotSupportedException>(async () =>
		{
			ChatMessage[] messages = [new(ChatRole.User, "Hello")];
			var options = new ChatOptions { ToolMode = ChatToolMode.RequireAny };
			if (!streaming)
				await client.GetResponseAsync(messages, options);
			else
				await foreach (var _ in client.GetStreamingResponseAsync(messages, options)) { }
		});
	}
}
#endif
