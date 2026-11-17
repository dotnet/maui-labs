using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

[Collection("GTK runtime")]
public class EntryTextTests
{
	[GtkRuntimeFact]
	public void TextReplacement_AndFiltering_KeepNativeAndManagedTextInSync()
	{
		Gtk.Module.Initialize();
		Gtk.Functions.Init();
		using var app = MauiApp.CreateBuilder().UseMauiAppLinuxGtk4<Application>().Build();
		var entry = new Entry { Text = "original" };
		using var native = (Gtk.Entry)entry.ToPlatform(new GtkMauiContext(app.Services));
		var changes = new List<string?>();
		entry.TextChanged += (_, args) => changes.Add(args.NewTextValue);
		try
		{
			entry.Text = "replacement";
			Assert.Equal("replacement", native.GetText());
			Assert.Equal(new[] { "replacement" }, changes);

			entry.TextChanged += (_, args) =>
			{
				var filtered = new string((args.NewTextValue ?? "").Where(char.IsDigit).ToArray());
				if (filtered != args.NewTextValue)
					entry.Text = filtered;
			};
			entry.Text = "a12b34";
			Assert.Equal("1234", entry.Text);
			Assert.Equal("1234", native.GetText());

			native.SetText("56");
			Assert.Equal("56", entry.Text);
			Assert.Equal("56", native.GetText());
		}
		finally
		{
			entry.Handler?.DisconnectHandler();
		}
	}
}
