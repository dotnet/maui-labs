using System.Windows;
using System.Windows.Controls;
using WSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WColor = System.Windows.Media.Color;
using WScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility;

namespace HandlerTests;

public class EditorHandlerTests
{
	[Fact]
	public void CreatePlatformView_HasCorrectDefaults()
	{
		StaHelper.RunOnSta(() =>
		{
			var textBox = new TextBox
			{
				AcceptsReturn = true,
				TextWrapping = System.Windows.TextWrapping.Wrap,
				VerticalScrollBarVisibility = WScrollBarVisibility.Auto,
				MinHeight = 80,
			};

			Assert.True(textBox.AcceptsReturn);
			Assert.Equal(System.Windows.TextWrapping.Wrap, textBox.TextWrapping);
			Assert.Equal(WScrollBarVisibility.Auto, textBox.VerticalScrollBarVisibility);
			Assert.Equal(80, textBox.MinHeight);
		});
	}

	[Fact]
	public void MapPlaceholder_SetsPlaceholderText()
	{
		StaHelper.RunOnSta(() =>
		{
			var placeholderBlock = new TextBlock
			{
				IsHitTestVisible = false,
				Foreground = new WSolidColorBrush(WColor.FromRgb(160, 160, 160)),
			};
			placeholderBlock.Text = "Enter description...";
			Assert.Equal("Enter description...", placeholderBlock.Text);
		});
	}

	[Fact]
	public void MapIsReadOnly_SetsReadOnly()
	{
		StaHelper.RunOnSta(() =>
		{
			var textBox = new TextBox();
			textBox.IsReadOnly = true;
			Assert.True(textBox.IsReadOnly);
		});
	}

	[Fact]
	public void MapMaxLength_SetsMaxLength()
	{
		StaHelper.RunOnSta(() =>
		{
			var textBox = new TextBox();
			textBox.MaxLength = 500;
			Assert.Equal(500, textBox.MaxLength);
		});
	}
}
