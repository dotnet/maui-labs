using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

public sealed class AccessibilityContractTests
{
	[Fact]
	public void GirCore_ProvidesTypedAccessiblePropertyBridge()
	{
		Assert.True(GtkAccessibilityExtensions.SupportsPropertyUpdates);
	}

	[Theory]
	[InlineData(4, 14, false)]
	[InlineData(4, 16, true)]
	[InlineData(5, 0, true)]
	public void HelpTextVersionGate_MatchesGtkIntroductionVersion(
		uint major,
		uint minor,
		bool expected)
	{
		Assert.Equal(expected, GtkRuntime.IsVersionAtLeast(major, minor, 4, 16));
	}
}
