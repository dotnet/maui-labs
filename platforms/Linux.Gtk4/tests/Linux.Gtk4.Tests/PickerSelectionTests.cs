using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Linux.Gtk4.Handlers;
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
		CheckRebuildSequence(context);
		CheckInitialSelection(context);
		CheckItemsMutations(context);
		CheckItemsSourceMutations(context);
		CheckSelectionMappingAndNativeChanges(context);
		CheckDisconnectAndReconnect(context);
	}

	void CheckRebuildSequence(GtkMauiContext context)
	{
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
			Assert.Equal("September", picker.SelectedItem);
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

	void CheckInitialSelection(GtkMauiContext context)
	{
		foreach (var index in new[] { -1, 0, 8 })
		{
			var picker = new Picker();
			foreach (var month in Months())
				picker.Items.Add(month);
			picker.SelectedIndex = index;
			var seen = new List<int>();
			picker.SelectedIndexChanged += (_, _) => seen.Add(picker.SelectedIndex);
			using var native = (Gtk.DropDown)picker.ToPlatform(context);
			try
			{
				Assert.Equal(index, picker.SelectedIndex);
				Assert.Equal(index < 0 ? uint.MaxValue : (uint)index, native.GetSelected());
				Assert.Empty(seen);
			}
			finally
			{
				picker.Handler?.DisconnectHandler();
			}
		}
		output.WriteLine("Initial selection: -1, 0, and 8 preserved without events.");
	}

	void CheckItemsMutations(GtkMauiContext context)
	{
		CheckAgainstMaui(context, picker =>
		{
			foreach (var month in Months())
				picker.Items.Add(month);
		},
		[
			picker => picker.Items.Add("Extra"),
			picker => picker.SelectedIndex = 8,
			picker => picker.Items.Insert(0, "Before"),
			picker => picker.Items.RemoveAt(0),
			picker => picker.Items[picker.SelectedIndex] = "Renamed",
			picker => picker.Items.RemoveAt(picker.SelectedIndex),
			picker => picker.SelectedIndex = picker.Items.Count - 1,
			picker => picker.Items.RemoveAt(picker.SelectedIndex),
			picker => picker.Items.Clear(),
			picker => picker.Items.Add("Refilled")
		]);
		output.WriteLine("Items add/insert/replace/removal/clear/refill match handlerless MAUI state and events.");
	}

	void CheckItemsSourceMutations(GtkMauiContext context)
	{
		CheckAgainstMaui(context,
			picker => picker.ItemsSource = new ObservableCollection<string>(Months()),
		[
			picker => picker.SelectedIndex = 8,
			picker => ((ObservableCollection<string>)picker.ItemsSource).Insert(0, "Before"),
			picker => ((ObservableCollection<string>)picker.ItemsSource).RemoveAt(0),
			picker => ((ObservableCollection<string>)picker.ItemsSource).Move(8, 3),
			picker => ((ObservableCollection<string>)picker.ItemsSource)[3] = "Renamed",
			picker => picker.ItemsSource = new ObservableCollection<string>(["Short", "List"]),
			picker => picker.ItemsSource = new ObservableCollection<string>(),
			picker => picker.ItemsSource = new ObservableCollection<string>(Months()),
			picker => picker.ItemsSource = null
		]);
		output.WriteLine("ItemsSource mutations and replacement match handlerless MAUI state and events.");
	}

	static void CheckAgainstMaui(GtkMauiContext context, Action<Picker> initialize, Action<Picker>[] changes)
	{
		var expected = new Picker();
		var actual = new Picker();
		initialize(expected);
		initialize(actual);
		using var native = (Gtk.DropDown)actual.ToPlatform(context);
		var expectedEvents = new List<int>();
		var actualEvents = new List<int>();
		expected.SelectedIndexChanged += (_, _) => expectedEvents.Add(expected.SelectedIndex);
		actual.SelectedIndexChanged += (_, _) => actualEvents.Add(actual.SelectedIndex);
		try
		{
			AssertSelection();
			foreach (var change in changes)
			{
				change(expected);
				change(actual);
				AssertSelection();
			}
		}
		finally
		{
			actual.Handler?.DisconnectHandler();
		}

		void AssertSelection()
		{
			Assert.Equal(expected.SelectedIndex, actual.SelectedIndex);
			Assert.Equal(expected.SelectedItem, actual.SelectedItem);
			Assert.Equal(expectedEvents, actualEvents);
			Assert.Equal(expected.SelectedIndex < 0 ? uint.MaxValue : (uint)expected.SelectedIndex,
				native.GetSelected());
		}
	}

	void CheckSelectionMappingAndNativeChanges(GtkMauiContext context)
	{
		var picker = new SelectionTrackingPicker();
		foreach (var month in Months())
			picker.Items.Add(month);
		using var native = (Gtk.DropDown)picker.ToPlatform(context);
		var seen = new List<int>();
		picker.SelectedIndexChanged += (_, _) => seen.Add(picker.SelectedIndex);
		try
		{
			foreach (var index in new[] { 8, 0, -1, 3 })
			{
				picker.SelectedIndex = index;
				Assert.Equal(index < 0 ? uint.MaxValue : (uint)index, native.GetSelected());
			}
			Assert.Equal(new[] { 8, 0, -1, 3 }, seen);
			Assert.Equal(0, picker.NativeSelectionWrites);

			seen.Clear();
			native.SetSelected(0);
			native.SetSelected(0);
			native.SetSelected(8);
			native.SetSelected(uint.MaxValue);
			Assert.Equal(new[] { 0, 8, -1 }, seen);
			Assert.Equal(3, picker.NativeSelectionWrites);
			Assert.Equal(-1, picker.SelectedIndex);
			Assert.Null(picker.SelectedItem);
		}
		finally
		{
			picker.Handler?.DisconnectHandler();
		}
		output.WriteLine("Managed selection maps without writeback; native changes report 0, 8, -1 exactly once.");
	}

	void CheckDisconnectAndReconnect(GtkMauiContext context)
	{
		var picker = new Picker();
		foreach (var month in Months())
			picker.Items.Add(month);
		picker.SelectedIndex = 8;
		using var original = (Gtk.DropDown)picker.ToPlatform(context);
		var handler = (PickerHandler)picker.Handler!;
		var seen = new List<int>();
		picker.SelectedIndexChanged += (_, _) => seen.Add(picker.SelectedIndex);
		handler.DisconnectHandler();
		original.SetSelected(0);
		Assert.Equal(8, picker.SelectedIndex);
		Assert.Empty(seen);

		handler.SetVirtualView(picker);
		using var reconnected = handler.PlatformView;
		try
		{
			Assert.Equal(8u, reconnected.GetSelected());
			Assert.Empty(seen);
			reconnected.SetSelected(3);
			Assert.Equal(new[] { 3 }, seen);
			Assert.Equal(3, picker.SelectedIndex);
		}
		finally
		{
			handler.DisconnectHandler();
		}
		output.WriteLine("Disconnect ignores native changes; reconnect restores selection and one subscription.");
	}

	static string[] Months() => CultureInfo.InvariantCulture.DateTimeFormat.MonthNames[..12];

	sealed class SelectionTrackingPicker : Picker, IPicker
	{
		public int NativeSelectionWrites { get; private set; }

		int IPicker.SelectedIndex
		{
			get => SelectedIndex;
			set
			{
				NativeSelectionWrites++;
				SelectedIndex = value;
			}
		}
	}
}
