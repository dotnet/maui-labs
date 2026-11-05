using BespokeCollections.Core;
using BespokeCollections.Fixture;
using Xunit;

namespace BespokeCollections.Tests;

public sealed class MasonryGeometryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(17)]
    public void Create_ValidCounts_HasFiniteNonoverlappingFrames(int count)
    {
        var items = Enumerable.Range(0, count).Select(index => FixtureCatalog.Item(index, "0")).ToArray();
        var geometry = MasonryGeometry.Create(items, 390, 2);
        Assert.Equal(count, geometry.Count);
        for (var i = 0; i < count; i++)
        {
            var frame = geometry[i];
            Assert.True(double.IsFinite(frame.Height) && frame.Height > 0);
            Assert.InRange(frame.Right, 0, 390);
            for (var j = i + 1; j < count; j++)
                Assert.False(frame.Intersects(geometry[j]));
        }
        Assert.Equal(count == 0 ? 0 : Enumerable.Range(0, count).Max(index => geometry[index].Bottom) + 8, geometry.Height);
    }

    [Fact]
    public void Intersecting_SeededViewports_MatchesSlowReference()
    {
        var items = Enumerable.Range(0, 10_000).Select(index => FixtureCatalog.Item(index, "0")).ToArray();
        var geometry = MasonryGeometry.Create(items, 800, 4);
        var random = new Random(47);
        for (var trial = 0; trial < 100; trial++)
        {
            var viewport = new MasonryRect(0, random.NextDouble() * geometry.Height, 800, 850);
            var expected = Enumerable.Range(0, items.Length).Where(index => geometry[index].Intersects(viewport)).ToArray();
            Assert.Equal(expected, geometry.Intersecting(viewport, out var visited));
            Assert.True(visited < 100, $"Viewport lookup visited {visited} candidates.");
        }
    }

    [Fact]
    public void Create_WidthChange_ReflowsFromMetadataWithoutViews()
    {
        var items = Enumerable.Range(0, 20).Select(index => FixtureCatalog.Item(index, "0")).ToArray();
        var narrow = MasonryGeometry.Create(items, 390, 2);
        var wide = MasonryGeometry.Create(items, 840, 4);
        Assert.NotEqual(narrow[0].Width, wide[0].Width);
        Assert.True(wide.Height < narrow.Height);
    }

    [Theory]
    [InlineData(double.NaN, 2)]
    [InlineData(double.PositiveInfinity, 2)]
    [InlineData(20, 4)]
    [InlineData(390, 0)]
    public void Create_InvalidConstraints_Throws(double width, int columns) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MasonryGeometry.Create([], width, columns));

    [Fact]
    public void Create_InvalidMetadata_Throws() =>
        Assert.Throws<ArgumentException>(() => MasonryGeometry.Create([new(1, "Invalid", 0, 100, 1, "/thumbnails/1")], 390, 2));
}
