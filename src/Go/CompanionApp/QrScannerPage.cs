// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace Microsoft.Maui.Go.CompanionApp;

/// <summary>
/// Full-screen camera QR code scanner page.
/// Presented modally via <see cref="ModalPresenter"/>, returns the scanned URL via TaskCompletionSource.
/// </summary>
/// <remarks>
/// IMPORTANT: <c>ModalPresenter</c> presents this page by calling <c>ToHandler()</c> and pushing the
/// raw <c>UIViewController</c>, which bypasses MAUI's Page lifecycle — <c>OnAppearing</c> never fires.
/// Initialization runs from <c>HandlerChanged</c> instead.
/// </remarks>
public class QrScannerPage : ContentPage
{
	readonly TaskCompletionSource<string?> _tcs;
	readonly CameraBarcodeReaderView _barcodeReader;
	readonly Border _viewfinder;
	readonly Border _statusBorder;
	readonly Label _statusLabel;
	bool _initStarted;
	bool _scanned;

	static readonly Color AccentColor = Color.FromArgb("#D4A04A");
	static readonly Color OverlayColor = Color.FromArgb("#AA281A0D");

	public QrScannerPage(TaskCompletionSource<string?> tcs)
	{
		_tcs = tcs;
		Shell.SetNavBarIsVisible(this, false);
		NavigationPage.SetHasNavigationBar(this, false);
		BackgroundColor = Colors.Black;

		// ModalPresenter bypasses MAUI's Page lifecycle (presents raw UIViewController),
		// so OnAppearing never fires. HandlerChanged DOES fire when the platform handler attaches.
		HandlerChanged += (_, _) =>
		{
			if (Handler is not null && !_initStarted)
			{
				_initStarted = true;
				MainThread.BeginInvokeOnMainThread(() => _ = StartScannerAsync());
			}
		};

		_barcodeReader = new CameraBarcodeReaderView
		{
			IsDetecting = false,
			CameraLocation = CameraLocation.Rear,
			Options = new BarcodeReaderOptions
			{
				Formats = BarcodeFormat.QrCode,
				AutoRotate = true,
				Multiple = false,
				TryHarder = true,
			}
		};
		_barcodeReader.BarcodesDetected += OnBarcodesDetected;

		_viewfinder = new Border
		{
			Stroke = Color.FromArgb("#44D4A04A"),
			StrokeThickness = 1.5,
			Background = Brush.Transparent,
			HorizontalOptions = LayoutOptions.Center,
			VerticalOptions = LayoutOptions.Center,
			WidthRequest = 240,
			HeightRequest = 240,
			StrokeShape = new RoundRectangle { CornerRadius = 20 },
			Content = new Grid()
		};

		_statusLabel = new Label
		{
			TextColor = Color.FromArgb("#48bb78"),
			FontSize = 14,
			FontAttributes = FontAttributes.Bold,
		};

		_statusBorder = new Border
		{
			Background = new SolidColorBrush(Color.FromArgb("#CC281A0D")),
			Stroke = Color.FromArgb("#3348bb78"),
			StrokeThickness = 1,
			Padding = new Thickness(20, 10),
			HorizontalOptions = LayoutOptions.Center,
			VerticalOptions = LayoutOptions.End,
			Margin = new Thickness(0, 0, 0, 90),
			IsVisible = false,
			StrokeShape = new RoundRectangle { CornerRadius = 24 },
			Content = _statusLabel,
		};

		var cornersGrid = new Grid
		{
			HorizontalOptions = LayoutOptions.Center,
			VerticalOptions = LayoutOptions.Center,
			WidthRequest = 240,
			HeightRequest = 240,
		};
		AddCornerAccents(cornersGrid);

		var closeButton = CreateCloseButton();
		var titleLabel = new Label
		{
			Text = "Scan QR Code",
			TextColor = Color.FromArgb("#D2BCA5"),
			FontSize = 16,
			FontAttributes = FontAttributes.Bold,
			HorizontalTextAlignment = TextAlignment.Center,
			VerticalTextAlignment = TextAlignment.Center,
		};

		var topBar = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition(48),
				new ColumnDefinition(GridLength.Star),
				new ColumnDefinition(48),
			},
			Padding = new Thickness(16, 60, 16, 0),
			VerticalOptions = LayoutOptions.Start,
		};
		topBar.Add(closeButton, 0);
		topBar.Add(titleLabel, 1);

		var instructionLabel = new Label
		{
			Text = "Point your camera at the QR code\nshown by the dev server",
			TextColor = Color.FromArgb("#D2BCA5"),
			FontSize = 14,
			HorizontalTextAlignment = TextAlignment.Center,
			VerticalOptions = LayoutOptions.Center,
			Margin = new Thickness(32, 180, 32, 0),
		};

		var layout = new Grid();
		layout.Add(_barcodeReader);
		layout.Add(_viewfinder);
		layout.Add(cornersGrid);
		layout.Add(topBar);
		layout.Add(instructionLabel);
		layout.Add(_statusBorder);

		Content = layout;
	}

	/// <summary>
	/// Dismiss this page. Uses native iOS dismiss since CometApp presents modally
	/// via UIViewController, not MAUI Navigation.
	/// </summary>
	void DismissScanner()
	{
#if IOS || MACCATALYST
		var vc = this.Handler as Microsoft.Maui.Handlers.PageHandler;
		vc?.ViewController?.DismissViewController(true, null);
#endif
	}

	Border CreateCloseButton()
	{
		var border = new Border
		{
			Background = new SolidColorBrush(Color.FromArgb("#66281A0D")),
			StrokeThickness = 0,
			WidthRequest = 44,
			HeightRequest = 44,
			HorizontalOptions = LayoutOptions.Start,
			StrokeShape = new RoundRectangle { CornerRadius = 22 },
			Content = new Label
			{
				Text = "X",
				TextColor = Color.FromArgb("#D2BCA5"),
				FontSize = 20,
				FontAttributes = FontAttributes.Bold,
				HorizontalTextAlignment = TextAlignment.Center,
				VerticalTextAlignment = TextAlignment.Center,
			}
		};

		var tap = new TapGestureRecognizer();
		tap.Tapped += (_, _) =>
		{
			_tcs.TrySetResult(null);
			DismissScanner();
		};
		border.GestureRecognizers.Add(tap);
		return border;
	}

	void AddCornerAccents(Grid grid)
	{
		// Top-left
		grid.Add(new BoxView { Color = AccentColor, WidthRequest = 36, HeightRequest = 3, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start });
		grid.Add(new BoxView { Color = AccentColor, WidthRequest = 3, HeightRequest = 36, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start });
		// Top-right
		grid.Add(new BoxView { Color = AccentColor, WidthRequest = 36, HeightRequest = 3, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start });
		grid.Add(new BoxView { Color = AccentColor, WidthRequest = 3, HeightRequest = 36, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start });
		// Bottom-left
		grid.Add(new BoxView { Color = AccentColor, WidthRequest = 36, HeightRequest = 3, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.End });
		grid.Add(new BoxView { Color = AccentColor, WidthRequest = 3, HeightRequest = 36, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.End });
		// Bottom-right
		grid.Add(new BoxView { Color = AccentColor, WidthRequest = 36, HeightRequest = 3, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End });
		grid.Add(new BoxView { Color = AccentColor, WidthRequest = 3, HeightRequest = 36, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End });
	}

	async Task StartScannerAsync()
	{
		try
		{
			var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
			if (status != PermissionStatus.Granted)
			{
				status = await Permissions.RequestAsync<Permissions.Camera>();
				if (status != PermissionStatus.Granted)
				{
					_tcs.TrySetResult(null);
					DismissScanner();
					return;
				}
			}

			await Task.Delay(500);
			_barcodeReader.IsDetecting = true;
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[QrScanner] StartScannerAsync error: {ex}");
		}
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		_barcodeReader.IsDetecting = false;
	}

	void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
	{
		if (_scanned) return;

		var result = e.Results?.FirstOrDefault();
		if (result is null) return;

		_scanned = true;

		MainThread.BeginInvokeOnMainThread(async () =>
		{
			try
			{
				_barcodeReader.IsDetecting = false;

				_viewfinder.Stroke = Color.FromArgb("#48bb78");
				_statusLabel.Text = "QR Code detected";
				_statusBorder.IsVisible = true;
				await Task.Delay(500);

				_tcs.TrySetResult(result.Value);
				DismissScanner();
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[QrScanner] Dismiss error: {ex}");
				_tcs.TrySetResult(result.Value);
				try { DismissScanner(); } catch { }
			}
		});
	}
}
