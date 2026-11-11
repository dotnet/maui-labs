using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

public sealed class AccessibilityContractTests
{
	[Fact]
	public void GirCore_ProvidesTypedAccessiblePropertyBridge()
	{
		Assert.True(GtkAccessibilityExtensions.SupportsPropertyUpdates);
	}
}
