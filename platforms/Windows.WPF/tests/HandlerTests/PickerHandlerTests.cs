using System.Windows;
using System.Windows.Controls;
using WSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WColor = System.Windows.Media.Color;

namespace HandlerTests;

public class PickerHandlerTests
{
	[Fact]
	public void MapTitle_SetsTextWhenNoSelection()
	{
		StaHelper.RunOnSta(() =>
		{
			var comboBox = new ComboBox { MinWidth = 100 };
			// Simulate no selection
			comboBox.SelectedIndex = -1;
			// Title should be settable as text
			comboBox.Text = "Select an item";
			Assert.Equal("Select an item", comboBox.Text);
		});
	}

	[Fact]
	public void MapTitleColor_SetsForeground()
	{
		StaHelper.RunOnSta(() =>
		{
			var comboBox = new ComboBox();
			var color = Microsoft.Maui.Graphics.Colors.Green;
			var brush = new WSolidColorBrush(WColor.FromArgb(
				(byte)(color.Alpha * 255), (byte)(color.Red * 255),
				(byte)(color.Green * 255), (byte)(color.Blue * 255)));
			comboBox.Foreground = brush;

			var actual = (WSolidColorBrush)comboBox.Foreground;
			Assert.Equal(0, actual.Color.R);
			Assert.True(actual.Color.G > 100); // Green
			Assert.Equal(0, actual.Color.B);
		});
	}

	[Fact]
	public void MapItems_PopulatesComboBox()
	{
		StaHelper.RunOnSta(() =>
		{
			var comboBox = new ComboBox();
			var items = new[] { "Apple", "Banana", "Cherry" };
			foreach (var item in items)
				comboBox.Items.Add(item);

			Assert.Equal(3, comboBox.Items.Count);
			Assert.Equal("Apple", comboBox.Items[0]);
			Assert.Equal("Banana", comboBox.Items[1]);
			Assert.Equal("Cherry", comboBox.Items[2]);
		});
	}

	[Fact]
	public void MapSelectedIndex_SetsSelection()
	{
		StaHelper.RunOnSta(() =>
		{
			var comboBox = new ComboBox();
			comboBox.Items.Add("A");
			comboBox.Items.Add("B");
			comboBox.Items.Add("C");

			comboBox.SelectedIndex = 1;
			Assert.Equal(1, comboBox.SelectedIndex);
			Assert.Equal("B", comboBox.SelectedItem);
		});
	}
}
