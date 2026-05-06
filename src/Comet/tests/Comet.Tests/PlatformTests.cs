using System;
using Microsoft.Maui;
using Microsoft.Maui.Devices;
using Xunit;

namespace Comet.Tests
{
	public class PlatformTests : TestBase
	{
		// ---- OnPlatform Tests ----

		[Fact]
		public void OnPlatformCreation()
		{
			var value = new OnPlatform<double>
			{
				Default = 14.0,
				iOS = 16.0,
				Android = 15.0,
				MacCatalyst = 14.0
			};
			Assert.Equal(14.0, value.Default);
		}

		[Fact]
		public void OnPlatformImplicitConversion()
		{
			var value = new OnPlatform<string>
			{
				Default = "default",
				iOS = "ios"
			};
			string result = value;
			Assert.NotNull(result);
		}

		// ---- OnIdiom Tests ----

		[Fact]
		public void OnIdiomCreation()
		{
			var value = new OnIdiom<double>
			{
				Default = 14.0,
				Phone = 12.0,
				Tablet = 16.0,
				Desktop = 14.0
			};
			Assert.Equal(14.0, value.Default);
		}

		[Fact]
		public void OnIdiomImplicitConversion()
		{
			var value = new OnIdiom<int>
			{
				Default = 10,
				Phone = 8,
				Desktop = 12
			};
			int result = value;
			Assert.True(result > 0);
		}

		// ---- PlatformBehavior Tests ----

		[Fact]
		public void PlatformBehaviorCreation()
		{
			var behavior = new TestPlatformBehavior();
			Assert.NotNull(behavior);
		}

		class TestPlatformBehavior : PlatformBehavior<Text>
		{
			protected override void OnAttachedTo(Text view) { }
			protected override void OnDetachingFrom(Text view) { }
		}
	}
}
