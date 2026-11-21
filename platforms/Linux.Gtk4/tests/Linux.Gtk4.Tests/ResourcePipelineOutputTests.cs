#if NET11_0_OR_GREATER
using System.Buffers.Binary;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

public sealed class ResourcePipelineOutputTests
{
	[Fact]
	public void StandardMauiItems_ArePublishedWithoutAppTargets()
	{
		AssertPng("red.png", 16, 16);
		AssertPng("red@2x.png", 32, 32);
		Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "OpenSans-Regular.ttf")));
		Assert.Equal(
			"GTK resource pipeline acceptance",
			File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Acceptance", "nested.txt")).Trim());
		Assert.True(File.Exists(Path.Combine(
			AppContext.BaseDirectory, "hicolor", "scalable", "apps", "appicon.svg")));
		Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "splash", "splash.svg")));
	}

	static void AssertPng(string name, int width, int height)
	{
		var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, name));
		Assert.True(bytes.Length >= 24);
		Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
		Assert.Equal(width, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)));
		Assert.Equal(height, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
	}
}
#endif
