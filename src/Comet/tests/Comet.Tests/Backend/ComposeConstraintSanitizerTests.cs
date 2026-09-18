#nullable enable
using Comet.Backend;
using Xunit;

namespace Comet.Tests.Backend
{
	public class ComposeConstraintSanitizerTests
	{
		[Fact]
		public void ValidMaximumPackedPair_IsPreserved()
		{
			var size = ComposeConstraintSanitizer.Sanitize(262_142, 8_190, 1);

			Assert.Equal(262_142, size.Width);
			Assert.Equal(8_190, size.Height);
			Assert.False(size.WasSanitized);
		}

		[Fact]
		public void InvalidLargeWidth_IsClampedToComposeEighteenBitLimit()
		{
			var size = ComposeConstraintSanitizer.Sanitize(262_144_128, 34, 1);

			Assert.Equal(262_142, size.Width);
			Assert.Equal(34, size.Height);
			Assert.True(size.WasSanitized);
		}

		[Fact]
		public void PairThatExceedsPackedBits_ClampsOnlyLowerPriorityAxis()
		{
			var size = ComposeConstraintSanitizer.Sanitize(70_000, 40_000, 1);

			Assert.Equal(70_000, size.Width);
			Assert.Equal(8_190, size.Height);
			Assert.True(size.WasSanitized);
		}

		[Fact]
		public void ReversedLargePair_PrioritizesHeight()
		{
			var size = ComposeConstraintSanitizer.Sanitize(40_000, 70_000, 1);

			Assert.Equal(8_190, size.Width);
			Assert.Equal(70_000, size.Height);
			Assert.True(size.WasSanitized);
		}

		[Fact]
		public void DensityIsAppliedBeforeComposeLimit()
		{
			var size = ComposeConstraintSanitizer.Sanitize(90_000, 10, 3);

			Assert.Equal(262_142f / 3f, size.Width);
			Assert.Equal(10, size.Height);
			Assert.True(size.WasSanitized);
		}

		[Theory]
		[InlineData(float.NaN)]
		[InlineData(float.PositiveInfinity)]
		[InlineData(float.NegativeInfinity)]
		[InlineData(-1f)]
		public void InvalidDimension_IsOmitted(float invalid)
		{
			var size = ComposeConstraintSanitizer.Sanitize(invalid, 34, 1);

			Assert.Null(size.Width);
			Assert.Equal(34, size.Height);
			Assert.True(size.WasSanitized);
		}
	}
}
