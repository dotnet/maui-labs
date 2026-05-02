using System;
using System.Reflection;
using Comet.Styles;
using Xunit;

namespace Comet.Tests
{
	/// <summary>
	/// Tests for ControlState [Flags] enum (§9).
	/// Written TDD-style against the Style/Theme Spec.
	/// </summary>
	public class ControlStateTests : TestBase
	{
		// ================================================================
		// [Flags] attribute presence (§9.1)
		// ================================================================

		[Fact]
		public void ControlState_HasFlagsAttribute()
		{
			var attr = typeof(ControlState).GetCustomAttribute<FlagsAttribute>();
			Assert.NotNull(attr);
		}

		// ================================================================
		// Default value (§9.1)
		// ================================================================

		[Fact]
		public void ControlState_Default_IsZero()
		{
			Assert.Equal(0, (int)ControlState.Default);
		}

		// ================================================================
		// Power-of-two values (§9.1)
		// ================================================================

		[Fact]
		public void ControlState_Pressed_Is1()
		{
			Assert.Equal(1, (int)ControlState.Pressed);
		}

		[Fact]
		public void ControlState_Hovered_Is2()
		{
			Assert.Equal(2, (int)ControlState.Hovered);
		}

		[Fact]
		public void ControlState_Focused_Is4()
		{
			Assert.Equal(4, (int)ControlState.Focused);
		}

		[Fact]
		public void ControlState_Disabled_Is8()
		{
			Assert.Equal(8, (int)ControlState.Disabled);
		}

		// ================================================================
		// Bitwise operations work (§9.1)
		// ================================================================

		[Fact]
		public void ControlState_BitwiseOr_CombinesFlags()
		{
			var combined = ControlState.Pressed | ControlState.Hovered;

			Assert.True(combined.HasFlag(ControlState.Pressed));
			Assert.True(combined.HasFlag(ControlState.Hovered));
			Assert.False(combined.HasFlag(ControlState.Disabled));
			Assert.False(combined.HasFlag(ControlState.Focused));
		}

		[Fact]
		public void ControlState_BitwiseAnd_ChecksFlag()
		{
			var state = ControlState.Pressed | ControlState.Focused;

			Assert.True((state & ControlState.Pressed) != 0);
			Assert.True((state & ControlState.Focused) != 0);
			Assert.False((state & ControlState.Hovered) != 0);
		}

		[Fact]
		public void ControlState_AllFlagsCombine()
		{
			var all = ControlState.Disabled
				| ControlState.Pressed
				| ControlState.Hovered
				| ControlState.Focused;

			Assert.Equal(15, (int)all);
			Assert.True(all.HasFlag(ControlState.Disabled));
			Assert.True(all.HasFlag(ControlState.Pressed));
			Assert.True(all.HasFlag(ControlState.Hovered));
			Assert.True(all.HasFlag(ControlState.Focused));
		}

		[Fact]
		public void ControlState_RemoveFlag_Works()
		{
			var state = ControlState.Pressed | ControlState.Hovered;

			// Remove Pressed
			state &= ~ControlState.Pressed;

			Assert.False(state.HasFlag(ControlState.Pressed));
			Assert.True(state.HasFlag(ControlState.Hovered));
		}

		[Theory]
		[InlineData(ControlState.Default, 0)]
		[InlineData(ControlState.Pressed, 1)]
		[InlineData(ControlState.Hovered, 2)]
		[InlineData(ControlState.Focused, 4)]
		[InlineData(ControlState.Disabled, 8)]
		public void ControlState_AllValuesArePowersOfTwo(ControlState state, int expected)
		{
			Assert.Equal(expected, (int)state);
		}
	}
}
