using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Tests;

public sealed class EmbeddedFontResourceResolverTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FindResource_ExactPath_WinsBeforeBasenameFallback(bool reverse)
    {
        string[] names = ["App.Other.Font.ttf", "App.Fonts.Font.ttf"];
        if (reverse)
            Array.Reverse(names);

        Assert.Equal("App.Fonts.Font.ttf", EmbeddedFontResourceResolver.FindResource("Fonts/Font.ttf", names));
    }

    [Fact]
    public void FindResource_BackslashPath_NormalizesBeforeMatching()
    {
        Assert.Equal("App.Fonts.Font.ttf",
            EmbeddedFontResourceResolver.FindResource(@"Fonts\Font.ttf", ["App.Fonts.Font.ttf"]));
    }

    [Fact]
    public void FindResource_UniqueBasename_UsedWhenExactPathMissing()
    {
        Assert.Equal("App.Other.Font.ttf",
            EmbeddedFontResourceResolver.FindResource("Fonts/Font.ttf", ["App.Other.Font.ttf"]));
    }

    [Fact]
    public void FindResource_AmbiguousBasename_ThrowsExplicitly()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            EmbeddedFontResourceResolver.FindResource("Fonts/Font.ttf", ["App.One.Font.ttf", "App.Two.Font.ttf"]));
        Assert.Contains("App.One.Font.ttf", error.Message);
        Assert.Contains("App.Two.Font.ttf", error.Message);
    }

    [Fact]
    public void FindResource_AmbiguousExactPath_ThrowsExplicitly()
    {
        Assert.Throws<InvalidOperationException>(() =>
            EmbeddedFontResourceResolver.FindResource("Fonts/Font.ttf", ["One.Fonts.Font.ttf", "Two.Fonts.Font.ttf"]));
    }

    [Fact]
    public void FindResource_Missing_ReturnsNull()
    {
        Assert.Null(EmbeddedFontResourceResolver.FindResource("Missing.ttf", ["App.Fonts.Font.ttf"]));
    }
}
