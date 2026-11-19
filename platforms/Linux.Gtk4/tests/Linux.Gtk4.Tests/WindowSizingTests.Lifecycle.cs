using System.Diagnostics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
using Xunit.Abstractions;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

public partial class WindowSizingTests
{
	sealed class ReentrantPage : ContentPage
	{
		public Action? OnNextMeasure { get; set; }

		protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
		{
			var result = base.MeasureOverride(widthConstraint, heightConstraint);
			var callback = OnNextMeasure;
			OnNextMeasure = null;
			callback?.Invoke();
			return result;
		}
	}

	sealed class ReentrantGrid : Grid
	{
		public Action? OnNextMeasure { get; set; }
		public int Measurements { get; private set; }
		public int Arrangements { get; private set; }
		public bool ArrangedBeforeMeasure { get; private set; }

		public ReentrantGrid() => Add(new Label { Text = "Reentrant allocation" });

		protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
		{
			Measurements++;
			var result = base.MeasureOverride(widthConstraint, heightConstraint);
			var callback = OnNextMeasure;
			OnNextMeasure = null;
			callback?.Invoke();
			return result;
		}

		protected override Size ArrangeOverride(Rect bounds)
		{
			Arrangements++;
			ArrangedBeforeMeasure |= Measurements == 0;
			return base.ArrangeOverride(bounds);
		}
	}

	sealed class AllocationLifecycleChecks(Application app, ITestOutputHelper output, Action<Exception?> finish)
	{
		Window _window = null!;
		Window _oldWindow = null!;
		Gtk.Window _native = null!;
		ReentrantPage _page = null!;
		ReentrantGrid _grid = null!;
		int _step;
		int _arrangementsAtDisconnect;
		Size _retiredWindowSize;
		readonly Stopwatch _clock = new();

		public void Start()
		{
			_grid = new();
			_page = new() { Content = _grid };
			_window = new(_page);
			app.OpenWindow(_window);
			_native = (Gtk.Window)_window.Handler!.PlatformView!;
			_clock.Restart();
			GLib.Functions.TimeoutAdd(0, 50, Tick);
		}

		void Apply()
		{
			switch (_step)
			{
				case 1:
					_oldWindow = _window;
					_retiredWindowSize = new(_window.Width, _window.Height);
					_window = new Window(_page);
					_oldWindow.Handler!.SetVirtualView(_window);
					break;
				case 2:
					_native.SetDefaultSize(640, 460);
					break;
				case 3:
					var oldPage = _page;
					_page = new() { Content = _grid = new() };
					oldPage.Handler!.SetVirtualView(_page);
					break;
				case 4:
					_page.OnNextMeasure = () =>
					{
						var handler = _page.Handler!;
						_page = new() { Content = _grid = new() };
						handler.SetVirtualView(_page);
					};
					_native.SetDefaultSize(600, 440);
					break;
				case 5:
					_grid.OnNextMeasure = () =>
					{
						var handler = _grid.Handler!;
						_grid = new();
						handler.SetVirtualView(_grid);
					};
					((GtkLayoutPanel)_grid.Handler!.PlatformView!).LayoutDirty = true;
					break;
				case 6:
					_grid.OnNextMeasure = () =>
					{
						_arrangementsAtDisconnect = _grid.Arrangements;
						_grid.Handler!.DisconnectHandler();
					};
					((GtkLayoutPanel)_grid.Handler!.PlatformView!).LayoutDirty = true;
					break;
				case 7:
					_native.SetDefaultSize(650, 470);
					break;
			}
			_clock.Restart();
		}

		bool Tick()
		{
			try
			{
				if (_clock.ElapsedMilliseconds < 500)
					return true;

				var nativePage = _native.GetChild()!.GetFirstChild()!;
				var ready = _window.Width == _native.GetChild()!.GetAllocatedWidth() &&
					_window.Height == _native.GetChild()!.GetAllocatedHeight() &&
					_page.Width == nativePage.GetAllocatedWidth() &&
					_page.Height == nativePage.GetAllocatedHeight() &&
					(_step < 6 ? _grid.Width == _page.Width && _grid.Height == _page.Height : _grid.Handler == null);
				if (!ready && _clock.Elapsed < TimeSpan.FromSeconds(5))
					return true;
				output.WriteLine($"allocation lifecycle {_step}: window {_window.Width}x{_window.Height}, page {_page.Frame}, grid {_grid.Frame}, measure {_grid.Measurements}, arrange {_grid.Arrangements}");
				Assert.Equal(_native.GetChild()!.GetAllocatedWidth(), _window.Width);
				Assert.Equal(_native.GetChild()!.GetAllocatedHeight(), _window.Height);
				Assert.True(_window.Width > 0 && _window.Height > 0);
				if (_step >= 1)
					Assert.Equal(_retiredWindowSize, new Size(_oldWindow.Width, _oldWindow.Height));
				Assert.Equal(nativePage.GetAllocatedWidth(), _page.Width);
				Assert.Equal(nativePage.GetAllocatedHeight(), _page.Height);
				if (_step < 6)
				{
					Assert.Equal(_page.Width, _grid.Width);
					Assert.Equal(_page.Height, _grid.Height);
				}
				if (_step == 5)
				{
					Assert.True(_grid.Measurements > 0);
					Assert.False(_grid.ArrangedBeforeMeasure, "A replacement must not be arranged with the previous view's measurement.");
				}
				if (_step >= 6)
				{
					Assert.Null(_grid.Handler);
					Assert.Equal(_arrangementsAtDisconnect, _grid.Arrangements);
				}
				if (++_step <= 7)
				{
					Apply();
					return true;
				}
			}
			catch (Exception ex)
			{
				_native.Close();
				finish(ex);
				return false;
			}
			_native.Close();
			finish(null);
			return false;
		}
	}
}
