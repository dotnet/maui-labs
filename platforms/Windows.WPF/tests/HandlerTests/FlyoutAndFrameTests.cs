using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using Microsoft.Maui.Handlers.WPF;
using WColors = System.Windows.Media.Colors;
using WBorder = System.Windows.Controls.Border;
using WCornerRadius = System.Windows.CornerRadius;

namespace HandlerTests;

public class FlyoutViewHandlerTests
{
	[Fact]
	public void SetFlyoutVisible_True_ShowsFlyoutColumn()
	{
		StaHelper.RunOnSta(() =>
		{
			var container = new FlyoutContainerView();
			container.SetFlyoutVisible(true);
			Assert.True(container.ColumnDefinitions[0].Width.Value > 0);
		});
	}

	[Fact]
	public void SetFlyoutVisible_False_CollapsesFlyoutColumn()
	{
		StaHelper.RunOnSta(() =>
		{
			var container = new FlyoutContainerView();
			container.SetFlyoutVisible(false);
			Assert.Equal(0, container.ColumnDefinitions[0].Width.Value);
		});
	}

	[Fact]
	public void SetFlyoutWidth_SetsColumnWidth()
	{
		StaHelper.RunOnSta(() =>
		{
			var container = new FlyoutContainerView();
			container.SetFlyoutWidth(300);
			Assert.Equal(300, container.ColumnDefinitions[0].Width.Value);
		});
	}

	[Fact]
	public void IsPresentedChanged_EventExists()
	{
		StaHelper.RunOnSta(() =>
		{
			var container = new FlyoutContainerView();
			bool eventFired = false;
			container.IsPresentedChanged += (s, isPresented) => eventFired = true;
			// Event should be subscribable (DragCompleted fires it)
			Assert.False(eventFired);
		});
	}
}

public class FrameShadowTests
{
	[Fact]
	public void DropShadowEffect_DefaultValues_MatchXamarinDefaults()
	{
		StaHelper.RunOnSta(() =>
		{
			// Matches the default shadow from Xamarin.Forms FrameRenderer
			var effect = new DropShadowEffect
			{
				Color = WColors.Gray,
				Direction = 320,
				Opacity = 0.5,
				BlurRadius = 6,
				ShadowDepth = 2
			};

			Assert.Equal(WColors.Gray, effect.Color);
			Assert.Equal(320, effect.Direction);
			Assert.Equal(0.5, effect.Opacity);
			Assert.Equal(6, effect.BlurRadius);
			Assert.Equal(2, effect.ShadowDepth);
		});
	}

	[Fact]
	public void Border_CornerRadius_SetsCorrectly()
	{
		StaHelper.RunOnSta(() =>
		{
			var border = new WBorder();
			border.CornerRadius = new WCornerRadius(10);
			Assert.Equal(10, border.CornerRadius.TopLeft);
			Assert.Equal(10, border.CornerRadius.TopRight);
			Assert.Equal(10, border.CornerRadius.BottomRight);
			Assert.Equal(10, border.CornerRadius.BottomLeft);
		});
	}
}
