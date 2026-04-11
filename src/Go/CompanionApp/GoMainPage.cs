// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Comet;
using Comet.Reactive;
using Comet.Styles;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace Microsoft.Maui.Go.CompanionApp;

/// <summary>
/// State for the Comet Go companion app.
/// </summary>
public class GoAppState
{
	// Use localhost as default -- works with adb reverse on physical devices
	// and directly on iOS/Mac simulators. For Android emulators without
	// adb reverse, change to 10.0.2.2 in the UI.
	public string ServerUrl { get; set; } = $"ws://localhost:{GoProtocol.DefaultPort}{GoProtocol.DefaultPath}";
	public string Status { get; set; } = "Enter server URL or scan QR code";
	public string? ErrorMessage { get; set; }
	public bool IsConnected { get; set; }
	public int DeltasApplied { get; set; }
	public View? UserView { get; set; }
}

/// <summary>
/// The main Comet Go companion app UI — connect screen + dynamic view host.
/// </summary>
public class GoMainPage : Component<GoAppState>
{
	GoClient? _client;
	bool _autoConnectAttempted;
	Type? _userViewType; // Track the user's view type for re-instantiation after deltas

	public GoMainPage()
	{
		// Auto-connect if MAUI_GO_SERVER env var is set
		var autoUrl = Environment.GetEnvironmentVariable("MAUI_GO_SERVER");
		if (!string.IsNullOrEmpty(autoUrl))
			State.ServerUrl = autoUrl;
	}

	public override View Render()
	{
		// Auto-connect on first render if env var was set
		if (!_autoConnectAttempted && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MAUI_GO_SERVER")))
		{
			_autoConnectAttempted = true;
			_ = Task.Run(async () =>
			{
				await Task.Delay(500);
				Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(OnConnectTapped);
			});
		}

		if (State.IsConnected && State.UserView is not null)
			return RenderUserView();

		return RenderConnectScreen();
	}

	View RenderConnectScreen()
	{
		return new VStack(spacing: 0)
		{
			new Spacer(),

			// Comet logo image
			new Image("comet_logo.png")
				.Frame(width: 200, height: 72)
				.HorizontalLayoutAlignment(Microsoft.Maui.Primitives.LayoutAlignment.Center),

			new Spacer().Frame(height: 12),

			// Welcome text
			Text("Welcome to Comet Go")
				.FontSize(22)
				.Color(new Color(160, 180, 204))
				.HorizontalTextAlignment(TextAlignment.Center),

			new Spacer().Frame(height: 40),

			// PRIMARY: Scan QR Code button (large, accent colored)
			Button("Scan QR Code", OnScanQrTapped)
				.Color(Colors.White)
				.FontWeight(FontWeight.Bold)
				.FontSize(18)
				.Background(new SolidPaint(new Color(212, 160, 74)))
				.CornerRadius(14)
				.Frame(height: 56)
				.AutomationId("ScanQrButton"),

			new Spacer().Frame(height: 24),

			// "or enter manually" divider
			new HStack(spacing: 12)
			{
				new Spacer().Frame(height: 1).Background(new SolidPaint(new Color(60, 50, 40))),
				Text("or enter manually")
					.FontSize(13)
					.Color(new Color(130, 120, 110)),
				new Spacer().Frame(height: 1).Background(new SolidPaint(new Color(60, 50, 40))),
			},

			new Spacer().Frame(height: 24),

			// Server URL input — outlined style (border, not filled)
			new TextField(new Signal<string>(State.ServerUrl), "ws://192.168.x.x:9000/maui-go")
				.OnTextChanged(url => SetState(s => s.ServerUrl = url))
				.Color(Colors.White)
				.FontSize(16)
				.Background(new SolidPaint(new Color(25, 20, 15)))
				.Padding(new Thickness(16, 14))
				.RoundedBorder(10, new Color(70, 60, 50))
				.AutomationId("ServerUrlField"),

			new Spacer().Frame(height: 16),

			// SECONDARY: Connect button (outlined/muted)
			Button("Connect", OnConnectTapped)
				.Color(new Color(212, 160, 74))
				.Background(new SolidPaint(new Color(35, 28, 18)))
				.CornerRadius(14)
				.Frame(height: 56)
				.RoundedBorder(14, new Color(70, 60, 50))
				.AutomationId("ConnectButton"),

			new Spacer().Frame(height: 20),

			// Status / error text
			State.ErrorMessage is not null
				? Text(State.ErrorMessage)
					.FontSize(13)
					.Color(Colors.OrangeRed)
					.HorizontalTextAlignment(TextAlignment.Center)
					.AutomationId("StatusLabel")
				: (State.Status != "Enter server URL or scan QR code"
					? Text(State.Status)
						.FontSize(13)
						.Color(new Color(160, 150, 140))
						.HorizontalTextAlignment(TextAlignment.Center)
						.AutomationId("StatusLabel")
					: (View)new Spacer().Frame(height: 1)),

			new Spacer(),
		}
		.Padding(new Thickness(28))
		.Background(new SolidPaint(new Color(25, 18, 10)));
	}

	View RenderUserView()
	{
		var children = new List<View>
		{
			// Status bar overlay
			new HStack(spacing: 8)
			{
				Text($"Comet Go — {State.DeltasApplied} updates")
					.FontSize(11)
					.Color(Colors.White),

				new Spacer(),

				Button("X", OnDisconnectTapped)
					.Color(Colors.White)
					.Background(Colors.Transparent)
					.FontSize(11)
					.AutomationId("DisconnectButton"),
			}
			.Padding(new Thickness(16, 4))
			.Background(new SolidPaint(new Color(139, 90, 43))),
		};

		// Show error/warning banner if present (simple text, no nested VStack)
		if (State.ErrorMessage is not null)
		{
			children.Add(
				Text(State.ErrorMessage)
					.FontSize(12)
					.Color(Colors.White)
					.FontFamily("Courier New")
					.Background(new SolidPaint(new Color(180, 40, 40)))
					.Padding(new Thickness(12, 8))
			);
		}

		// User's view fills the rest
		children.Add(State.UserView!);

		return new VStack(spacing: 0) { children.ToArray() };
	}

	async void OnScanQrTapped()
	{
		try
		{
			var tcs = new System.Threading.Tasks.TaskCompletionSource<string?>();
			var scannerPage = new QrScannerPage(tcs);

			// In CometApp, get the current page from the window
			var window = Microsoft.Maui.Controls.Application.Current?.Windows?.FirstOrDefault();
			var currentPage = window?.Page;

			Console.WriteLine($"[QR] Window: {window is not null}, Page: {currentPage?.GetType().Name ?? "null"}");

			if (currentPage is null)
			{
				SetState(s => s.ErrorMessage = "Cannot open scanner - no active page found");
				return;
			}

			await currentPage.Navigation.PushModalAsync(scannerPage);
			var result = await tcs.Task;

			if (!string.IsNullOrEmpty(result))
			{
				SetState(s =>
				{
					s.ServerUrl = result;
					s.Status = "QR code scanned - tap Connect";
					s.ErrorMessage = null;
				});
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[QR] Error: {ex}");
			SetState(s => s.ErrorMessage = $"Scanner error: {ex.Message}");
		}
	}

	async void OnConnectTapped()
	{
		SetState(s =>
		{
			s.Status = "Connecting...";
			s.ErrorMessage = null;
		});

		_client?.Dispose();
		_client = new GoClient();

		_client.StatusChanged += status =>
			Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
				SetState(s => s.Status = status));

		_client.ErrorReceived += error =>
			Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
				SetState(s => s.ErrorMessage = error));

		_client.AssemblyLoaded += assembly =>
			Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
				LoadUserView(assembly));

		_client.DeltaApplied += seq =>
			Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
			{
				// Re-instantiate the user's view to pick up the updated method body.
				if (_userViewType is not null)
				{
					try
					{
						var newView = (Comet.View)Activator.CreateInstance(_userViewType)!;
						SetState(s =>
						{
							s.UserView = newView;
							s.DeltasApplied = seq;
							s.ErrorMessage = null; // Clear any previous error
						});
					}
					catch (Exception ex)
					{
						SetState(s =>
						{
							s.DeltasApplied = seq;
							s.ErrorMessage = $"View update failed: {ex.Message}";
						});
					}
				}
				else
				{
					SetState(s => s.DeltasApplied = seq);
				}
			});

		_client.Disconnected += () =>
			Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
				SetState(s =>
				{
					s.IsConnected = false;
					s.UserView = null;
					s.Status = "Disconnected — tap Connect to retry";
				}));

		_client.RestartRequired += reason =>
			Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
				SetState(s => s.ErrorMessage = $"Restart required: {reason}"));

		await _client.ConnectAsync(State.ServerUrl);
	}

	void OnDisconnectTapped()
	{
		_client?.Dispose();
		_client = null;
		SetState(s =>
		{
			s.IsConnected = false;
			s.UserView = null;
			s.DeltasApplied = 0;
			s.Status = "Disconnected";
		});
	}

	/// <summary>
	/// Finds the user's main Comet View in the loaded assembly and instantiates it.
	/// Convention: looks for a class named "MainPage" inheriting from Comet.View.
	/// </summary>
	void LoadUserView(Assembly assembly)
	{
		var cometViewType = typeof(Comet.View);

		// Find MainPage or first public View subclass
		var viewType = assembly.GetExportedTypes()
			.FirstOrDefault(t => t.Name == "MainPage" && cometViewType.IsAssignableFrom(t))
			?? assembly.GetExportedTypes()
				.FirstOrDefault(t => cometViewType.IsAssignableFrom(t) && !t.IsAbstract);

		if (viewType is null)
		{
			SetState(s => s.ErrorMessage = "No Comet View found in assembly. Create a class named 'MainPage' inheriting from Comet.View.");
			return;
		}

		_userViewType = viewType;

		try
		{
			var view = (Comet.View)Activator.CreateInstance(viewType)!;
			SetState(s =>
			{
				s.UserView = view;
				s.IsConnected = true;
				s.ErrorMessage = null;
			});
		}
		catch (Exception ex)
		{
			SetState(s => s.ErrorMessage = $"Failed to instantiate {viewType.Name}: {ex.Message}");
		}
	}
}
