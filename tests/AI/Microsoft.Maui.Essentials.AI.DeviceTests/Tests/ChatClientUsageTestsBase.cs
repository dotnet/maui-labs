using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

/// <summary>
/// Base class for chat client usage tests.
/// Provides common consistency checks for any <see cref="IChatClient"/> implementation.
/// </summary>
/// <typeparam name="T">The concrete chat client type to test.</typeparam>
public abstract class ChatClientUsageTestsBase<T>
	where T : class, IChatClient, new()
{
	/// <summary>
	/// Gets whether the provider is expected to report usage in the current environment.
	/// </summary>
	protected virtual bool IsUsageAvailable => true;

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_ReturnsExpectedUsage()
	{
		var client = new T();

		var response = await client.GetResponseAsync("Reply with exactly one word: hello.");

		AssertExpectedUsage(response.Usage);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetStreamingResponseAsync_ReturnsExpectedUsage()
	{
		var client = new T();

		var response = await client
			.GetStreamingResponseAsync("Reply with exactly one word: hello.")
			.ToChatResponseAsync();

		AssertExpectedUsage(response.Usage);
	}

	/// <summary>
	/// Validates usage according to the provider's support in the current environment.
	/// </summary>
	protected void AssertExpectedUsage(UsageDetails? usage)
	{
		if (!IsUsageAvailable)
		{
			Assert.Null(usage);
			return;
		}

		Assert.NotNull(usage);
		Assert.NotNull(usage.InputTokenCount);
		Assert.NotNull(usage.OutputTokenCount);
		Assert.NotNull(usage.TotalTokenCount);
		Assert.True(usage.InputTokenCount.Value >= 1);
		Assert.True(usage.OutputTokenCount.Value >= 1);
		Assert.True(usage.TotalTokenCount.Value >= usage.InputTokenCount.Value);
		Assert.True(usage.TotalTokenCount.Value >= usage.OutputTokenCount.Value);

		if (usage.CachedInputTokenCount is { } cachedInputTokenCount)
			Assert.True(cachedInputTokenCount >= 0);

		if (usage.ReasoningTokenCount is { } reasoningTokenCount)
			Assert.True(reasoningTokenCount >= 0);

		AssertProviderUsage(usage);
	}

	/// <summary>
	/// Validates provider-specific usage details when usage is available.
	/// </summary>
	protected virtual void AssertProviderUsage(UsageDetails usage)
	{
	}
}
