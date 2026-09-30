using System.Globalization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
using Xunit.Abstractions;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

[Collection("GTK runtime")]
public class PickerSelectionTests(ITestOutputHelper output)
{
	[GtkRuntimeFact]
	public void Items_Replacement_PreservesMauiSelectionAndEvents()
	{
		Gtk.Module.Initialize();
		Gtk.Functions.Init();
		using var app = MauiApp.CreateBuilder().UseMauiAppLinuxGtk4<Application>().Build();
		var context = new GtkMauiContext(app.Services);
		var picker = new Picker();
		var native = (Gtk.DropDown)picker.ToPlatform(context);
		var seen = new List<int>();
		picker.SelectedIndexChanged += (_, _) => seen.Add(picker.SelectedIndex);
		try
		{
			Fill();
			picker.SelectedIndex = 8;
			picker.Items.Clear();
			Fill();
			picker.SelectedIndex = 8;

			output.WriteLine($"Fill/clear/refill sequence: {string.Join(", ", seen)}");
			Assert.Equal(new[] { 8, -1, 8 }, seen);
			Assert.Equal(8, picker.SelectedIndex);
			Assert.Equal(8u, native.GetSelected());
		}
		finally
		{
			picker.Handler?.DisconnectHandler();
			native.Dispose();
		}

		void Fill()
		{
			foreach (var month in CultureInfo.InvariantCulture.DateTimeFormat.MonthNames[..12])
				picker.Items.Add(month);
		}
	}
}
