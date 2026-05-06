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
Type? _userViewType;
View? _lastGoodView;

public GoMainPage()
{
var autoUrl = Environment.GetEnvironmentVariable("MAUI_GO_SERVER");
if (!string.IsNullOrEmpty(autoUrl))
State.ServerUrl = autoUrl;
}

/// <summary>
/// Unwrap TargetInvocationException / TypeInitializationException to get the real error.
/// </summary>
static Exception UnwrapException(Exception ex) => ex.GetBaseException() ?? ex;

/// <summary>
/// Try to instantiate and preflight-build a user view.
/// Returns the view on success, null on failure (error message in out param).
/// This prevents crashes by catching render errors BEFORE committing to state.
/// </summary>
View? TryCreateUserView(Type viewType, out string? error)
{
error = null;
View candidate;

// Step 1: Instantiate
try
{
candidate = (Comet.View)Activator.CreateInstance(viewType)!;
}
catch (Exception ex)
{
var real = UnwrapException(ex);
error = $"Constructor failed: {real.GetType().Name}: {real.Message}";
Console.WriteLine($"[GoMainPage] {error}");
return null;
}

// Step 2: Preflight — force the [Body] method to execute so we catch
// render errors BEFORE committing to state.
try
{
candidate.GetView();
}
catch (Exception ex)
{
var real = UnwrapException(ex);
error = $"Render failed: {real.GetType().Name}: {real.Message}";
Console.WriteLine($"[GoMainPage] Preflight render error: {error}");
return null;
}

return candidate;
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
{
if (State.ErrorMessage is not null)
{
// Show error overlay on top of user view — doesn't affect user layout
return new ZStack
{
new VStack
{
State.UserView
.FillHorizontal()
.FillVertical()
}
.IgnoreSafeArea()
.Padding(0)
.Margin(0),

new VStack(spacing: 0f)
{
new Spacer(),
Text(State.ErrorMessage)
.FontSize(12)
.Color(Colors.White)
.HorizontalTextAlignment(TextAlignment.Center)
.Padding(new Thickness(16, 8))
.Background(new SolidPaint(Color.FromArgb("#CC3333")))
.Margin(new Thickness(20, 0, 20, 50))
}
.IgnoreSafeArea()
};
}

return new VStack
{
State.UserView
.FillHorizontal()
.FillVertical()
}
.IgnoreSafeArea()
.Padding(0)
.Margin(0);
}

return RenderConnectScreen();
}

View RenderConnectScreen()
{
var bgColor = new Color(210, 188, 165);
var accentColor = new Color(139, 90, 43);
var textColor = new Color(60, 40, 20);
var borderColor = new Color(120, 80, 40);

return new VStack(spacing: 0)
{
new Spacer(),

new Image("comet_logo")
.Frame(width: 260, height: 87)
.HorizontalLayoutAlignment(Microsoft.Maui.Primitives.LayoutAlignment.Center),

new Spacer().Frame(height: 16),

Text("Welcome to Comet Go")
.FontSize(20)
.Color(new Color(80, 55, 30))
.HorizontalTextAlignment(TextAlignment.Center),

new Spacer().Frame(height: 48),

Button("Scan QR Code", OnScanQrTapped)
.Color(Colors.White)
.FontWeight(FontWeight.Bold)
.FontSize(18)
.Background(new SolidPaint(accentColor))
.CornerRadius(14)
.Frame(height: 56)
.RoundedBorder(14, accentColor)
.AutomationId("ScanQrButton"),

new Spacer().Frame(height: 28),

new HStack(spacing: 0)
{
new BoxView().Frame(height: 1).Background(new SolidPaint(borderColor)).VerticalLayoutAlignment(Primitives.LayoutAlignment.Center),
Text("  or enter manually  ")
.FontSize(13)
.Color(new Color(100, 75, 50)),
new BoxView().Frame(height: 1).Background(new SolidPaint(borderColor)).VerticalLayoutAlignment(Primitives.LayoutAlignment.Center),
},

new Spacer().Frame(height: 28),

new TextField(new Signal<string>(State.ServerUrl), "ws://192.168.x.x:9000/maui-go")
.OnTextChanged(url => SetState(s => s.ServerUrl = url))
.Color(textColor)
.FontSize(16)
.Background(new SolidPaint(new Color(235, 220, 200)))
.Padding(new Thickness(16, 14))
.Frame(height: 56)
.RoundedBorder(14, borderColor, 2)
.AutomationId("ServerUrlField"),

new Spacer().Frame(height: 16),

Button("Connect", OnConnectTapped)
.Color(accentColor)
.FontSize(16)
.FontWeight(FontWeight.Semibold)
.Background(new SolidPaint(new Color(235, 220, 200)))
.CornerRadius(14)
.Frame(height: 56)
.RoundedBorder(14, accentColor, 2)
.AutomationId("ConnectButton"),

new Spacer().Frame(height: 20),

State.ErrorMessage is not null
? Text(State.ErrorMessage)
.FontSize(13)
.Color(Colors.OrangeRed)
.HorizontalTextAlignment(TextAlignment.Center)
.AutomationId("StatusLabel")
: (State.Status != "Enter server URL or scan QR code"
? Text(State.Status)
.FontSize(13)
.Color(new Color(100, 80, 60))
.HorizontalTextAlignment(TextAlignment.Center)
.AutomationId("StatusLabel")
: (View)new Spacer().Frame(height: 1)),

new Spacer(),
}
.Padding(new Thickness(28))
.Background(new SolidPaint(bgColor));
}

async void OnScanQrTapped()
{
try
{
var tcs = new TaskCompletionSource<string?>();
var scannerPage = new QrScannerPage(tcs);

if (!ModalPresenter.Present(scannerPage))
{
SetState(s => s.ErrorMessage = "Cannot open scanner on this platform");
return;
}

var result = await tcs.Task;

if (!string.IsNullOrEmpty(result))
{
SetState(s =>
{
s.ServerUrl = result;
s.Status = "QR code scanned - connecting...";
s.ErrorMessage = null;
});
OnConnectTapped();
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
	try
	{
		// Validate the URL synchronously — any throw here would crash the app
		// because async void doesn't surface exceptions to the caller.
		var rawUrl = State.ServerUrl?.Trim() ?? "";
		if (string.IsNullOrEmpty(rawUrl))
		{
			SetState(s =>
			{
				s.Status = "Enter a server URL or scan the QR code";
				s.ErrorMessage = "Server URL is empty.";
			});
			return;
		}

		// Auto-prepend ws:// if user typed just an IP/host
		if (!rawUrl.Contains("://"))
			rawUrl = "ws://" + rawUrl;

		if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) ||
			(uri.Scheme != "ws" && uri.Scheme != "wss"))
		{
			SetState(s =>
			{
				s.Status = "Invalid server URL";
				s.ErrorMessage = $"\"{rawUrl}\" isn't a valid ws:// or wss:// URL. Example: ws://192.168.0.10:9000/maui-go";
			});
			return;
		}

		SetState(s =>
		{
			s.ServerUrl = rawUrl;
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
				if (_userViewType is not null)
				{
					var candidate = TryCreateUserView(_userViewType, out var error);
					if (candidate is not null)
					{
						_lastGoodView = candidate;
						SetState(s =>
						{
							s.UserView = candidate;
							s.DeltasApplied = seq;
							s.ErrorMessage = null;
						});
					}
					else
					{
						// Keep showing last good view, show error as overlay
						SetState(s =>
						{
							s.DeltasApplied = seq;
							s.ErrorMessage = error;
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

		await _client.ConnectAsync(rawUrl);
	}
	catch (Exception ex)
	{
		// async void: if anything escapes here it crashes the app. Log + show inline.
		Console.WriteLine($"[GoMainPage] Connect failed: {ex}");
		SetState(s =>
		{
			s.Status = "Connection failed";
			s.ErrorMessage = ex.Message;
		});
	}
}

void OnDisconnectTapped()
{
_client?.Dispose();
_client = null;
_lastGoodView = null;
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
/// Uses preflight to catch constructor/render errors before committing to state.
/// </summary>
void LoadUserView(Assembly assembly)
{
static bool IsCometView(Type t)
{
var current = t.BaseType;
while (current is not null)
{
if (current.FullName == "Comet.View")
return true;
current = current.BaseType;
}
return false;
}

var viewType = assembly.GetExportedTypes()
.FirstOrDefault(t => t.Name == "MainPage" && IsCometView(t))
?? assembly.GetExportedTypes()
.FirstOrDefault(t => IsCometView(t) && !t.IsAbstract);

if (viewType is null)
{
SetState(s =>
{
s.ErrorMessage = "No Comet View found in assembly. Create a class named 'MainPage' inheriting from Comet.View.";
s.IsConnected = true;
});
return;
}

_userViewType = viewType;
Console.WriteLine($"[GoMainPage] Found view type: {viewType.FullName}");

var candidate = TryCreateUserView(viewType, out var error);
if (candidate is not null)
{
_lastGoodView = candidate;
SetState(s =>
{
s.UserView = candidate;
s.IsConnected = true;
s.ErrorMessage = null;
});
}
else
{
SetState(s =>
{
s.IsConnected = true;
s.ErrorMessage = error;
});
}
}
}
