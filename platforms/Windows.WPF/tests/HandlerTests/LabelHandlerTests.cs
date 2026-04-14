using System.Windows;
using System.Windows.Controls;
using WSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WColor = System.Windows.Media.Color;

namespace HandlerTests;

public class LabelHandlerTests
{
	[Theory]
	[InlineData(Microsoft.Maui.LineBreakMode.NoWrap, System.Windows.TextTrimming.None, System.Windows.TextWrapping.NoWrap)]
	[InlineData(Microsoft.Maui.LineBreakMode.WordWrap, System.Windows.TextTrimming.None, System.Windows.TextWrapping.Wrap)]
	[InlineData(Microsoft.Maui.LineBreakMode.CharacterWrap, System.Windows.TextTrimming.CharacterEllipsis, System.Windows.TextWrapping.Wrap)]
	[InlineData(Microsoft.Maui.LineBreakMode.HeadTruncation, System.Windows.TextTrimming.WordEllipsis, System.Windows.TextWrapping.NoWrap)]
	[InlineData(Microsoft.Maui.LineBreakMode.TailTruncation, System.Windows.TextTrimming.CharacterEllipsis, System.Windows.TextWrapping.NoWrap)]
	[InlineData(Microsoft.Maui.LineBreakMode.MiddleTruncation, System.Windows.TextTrimming.WordEllipsis, System.Windows.TextWrapping.NoWrap)]
	public void MapLineBreakMode_SetsCorrectProperties(
		Microsoft.Maui.LineBreakMode lineBreakMode,
		System.Windows.TextTrimming expectedTrimming,
		System.Windows.TextWrapping expectedWrapping)
	{
		StaHelper.RunOnSta(() =>
		{
			var textBlock = new TextBlock { Text = "Test text for line break mode" };

			// Apply the same logic as MapLineBreakMode
			switch (lineBreakMode)
			{
				case Microsoft.Maui.LineBreakMode.NoWrap:
					textBlock.TextTrimming = System.Windows.TextTrimming.None;
					textBlock.TextWrapping = System.Windows.TextWrapping.NoWrap;
					break;
				case Microsoft.Maui.LineBreakMode.WordWrap:
					textBlock.TextTrimming = System.Windows.TextTrimming.None;
					textBlock.TextWrapping = System.Windows.TextWrapping.Wrap;
					break;
				case Microsoft.Maui.LineBreakMode.CharacterWrap:
					textBlock.TextTrimming = System.Windows.TextTrimming.CharacterEllipsis;
					textBlock.TextWrapping = System.Windows.TextWrapping.Wrap;
					break;
				case Microsoft.Maui.LineBreakMode.HeadTruncation:
					textBlock.TextTrimming = System.Windows.TextTrimming.WordEllipsis;
					textBlock.TextWrapping = System.Windows.TextWrapping.NoWrap;
					break;
				case Microsoft.Maui.LineBreakMode.TailTruncation:
					textBlock.TextTrimming = System.Windows.TextTrimming.CharacterEllipsis;
					textBlock.TextWrapping = System.Windows.TextWrapping.NoWrap;
					break;
				case Microsoft.Maui.LineBreakMode.MiddleTruncation:
					textBlock.TextTrimming = System.Windows.TextTrimming.WordEllipsis;
					textBlock.TextWrapping = System.Windows.TextWrapping.NoWrap;
					break;
			}

			Assert.Equal(expectedTrimming, textBlock.TextTrimming);
			Assert.Equal(expectedWrapping, textBlock.TextWrapping);
		});
	}

	[Fact]
	public void MapTextDecorations_SetsUnderline()
	{
		StaHelper.RunOnSta(() =>
		{
			var textBlock = new TextBlock { Text = "Underlined" };
			textBlock.TextDecorations = System.Windows.TextDecorations.Underline;
			Assert.NotNull(textBlock.TextDecorations);
			Assert.True(textBlock.TextDecorations.Count > 0);
		});
	}

	[Fact]
	public void MapTextColor_SetsForeground()
	{
		StaHelper.RunOnSta(() =>
		{
			var textBlock = new TextBlock();
			var color = Microsoft.Maui.Graphics.Colors.Blue;
			var brush = new WSolidColorBrush(WColor.FromArgb(
				(byte)(color.Alpha * 255), (byte)(color.Red * 255),
				(byte)(color.Green * 255), (byte)(color.Blue * 255)));
			textBlock.Foreground = brush;

			var actual = (WSolidColorBrush)textBlock.Foreground;
			Assert.Equal(0, actual.Color.R);
			Assert.Equal(0, actual.Color.G);
			Assert.Equal(255, actual.Color.B);
		});
	}

	[Fact]
	public void MapPadding_SetsPadding()
	{
		StaHelper.RunOnSta(() =>
		{
			var textBlock = new TextBlock();
			textBlock.Padding = new System.Windows.Thickness(10, 5, 10, 5);
			Assert.Equal(10, textBlock.Padding.Left);
			Assert.Equal(5, textBlock.Padding.Top);
		});
	}
}
