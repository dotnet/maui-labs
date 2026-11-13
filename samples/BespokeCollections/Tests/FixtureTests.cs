using System.Buffers.Binary;
using BespokeCollections.Fixture;
using Xunit;

namespace BespokeCollections.Tests;

public sealed class FixtureTests
{
    [Fact]
    public void Page_CursorProgressAndTerminal_AreExplicit()
    {
        var first = FixtureCatalog.Page("0", null, 50);
        var next = FixtureCatalog.Page("0", first.NextCursor, 50);
        Assert.Equal("0:50", first.NextCursor);
        Assert.Equal(51, next.Items[0].Id);
        Assert.Null(FixtureCatalog.Page("0", "0:99950", 50).NextCursor);
        Assert.Throws<ArgumentException>(() => FixtureCatalog.Page("1", first.NextCursor, 50));
    }

    [Fact]
    public void Png_OriginalFixture_HasRequestedDimensionsAndDeterministicBytes()
    {
        var png = FixturePng.Create(7, 160, 240);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        Assert.Equal(160, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(240, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
        Assert.Equal(png, FixturePng.Create(7, 160, 240));
        Assert.NotEqual(png, FixturePng.Create(8, 160, 240));
    }
}
