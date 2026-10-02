using CometSamples;
using Xunit;

namespace Comet.Tests;

public class SampleScreenResolverTests
{
	[Theory]
	[InlineData("com.comet.sample.baristanotes", "baristanotes")]
	[InlineData("com.comet.sample.aot.baristanotes", "baristanotes")]
	[InlineData("com.comet.sample.perf.baristanotes", "baristanotes")]
	[InlineData("com.comet.sample.perf.diag.baristanotes", "baristanotes")]
	[InlineData("com.comet.composeprobe", "jetchat")]
	[InlineData(null, "jetchat")]
	public void Resolve_AppIdentity_SelectsExpectedScreen(string? appId, string expected)
	{
		Assert.Equal(expected, SampleScreenResolver.Resolve(null, appId));
	}

	[Fact]
	public void Resolve_ExplicitScreen_TakesPrecedence()
	{
		Assert.Equal(
			"reply",
			SampleScreenResolver.Resolve("reply", "com.comet.sample.aot.baristanotes"));
	}
}
