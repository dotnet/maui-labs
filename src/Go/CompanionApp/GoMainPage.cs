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
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace Microsoft.Maui.Go.CompanionApp;

/// <summary>
/// State for the Comet Go companion app.
/// </summary>
public class GoAppState
{
	// On Android emulator, 10.0.2.2 reaches the host machine.
	// With adb reverse, localhost also works.
	public string ServerUrl { get; set; } = DeviceInfo.Platform == DevicePlatform.Android
		? $"ws://10.0.2.2:{GoProtocol.DefaultPort}{GoProtocol.DefaultPath}"
		: $"ws://localhost:{GoProtocol.DefaultPort}{GoProtocol.DefaultPath}";
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
		return new VStack(spacing: 20)
		{
			new Spacer(),

			// Logo / Title
			Text("Comet Go")
				.FontSize(36)
				.FontWeight(FontWeight.Bold)
				.Color(Colors.White)
				.HorizontalTextAlignment(TextAlignment.Center),

			Text("Connect to your dev server")
				.FontSize(16)
				.Color(new Color(255, 255, 255, 180))
				.HorizontalTextAlignment(TextAlignment.Center),

			new Spacer().Frame(height: 20),

			// Server URL input
			new TextField(new Signal<string>(State.ServerUrl), "ws://host:9000/maui-go")
				.OnTextChanged(url => SetState(s => s.ServerUrl = url))
				.Color(Colors.White)
				.FontSize(14)
				.AutomationId("ServerUrlField"),

			// Connect button
			Button("Connect", OnConnectTapped)
				.Color(Colors.White)
				.Background(new SolidPaint(new Color(139, 90, 43)))
				.CornerRadius(12)
				.Frame(height: 50)
				.AutomationId("ConnectButton"),

			// Status
			Text(State.Status)
				.FontSize(14)
				.Color(State.ErrorMessage is not null ? Colors.OrangeRed : new Color(200, 200, 200))
				.HorizontalTextAlignment(TextAlignment.Center)
				.AutomationId("StatusLabel"),

			// Error details
			State.ErrorMessage is not null
				? Text(State.ErrorMessage)
					.FontSize(12)
					.Color(Colors.OrangeRed)
					.FontFamily("Courier New")
				: (View)new Spacer().Frame(height: 1),

			new Spacer(),

			// Footer
			Text("Powered by .NET MAUI + Comet")
				.FontSize(11)
				.Color(new Color(180, 170, 155))
				.HorizontalTextAlignment(TextAlignment.Center),
		}
		.Padding(new Thickness(32))
		.Background(new SolidPaint(new Color(40, 26, 13)));
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
