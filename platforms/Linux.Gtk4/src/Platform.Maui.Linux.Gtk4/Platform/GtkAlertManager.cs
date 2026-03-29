using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

namespace Platform.Maui.Linux.Gtk4.Platform;

/// <summary>
/// Handles MAUI DisplayAlert, DisplayActionSheet, and DisplayPromptAsync
/// using custom GTK4 dialog windows. Registers a DispatchProxy implementation
/// of MAUI's internal IAlertManagerSubscription via DI so AlertManager.Subscribe()
/// discovers it automatically.
/// See: https://gist.github.com/Redth/fc07a982bcff79cf925168f241a12c95
/// </summary>
public static class GtkAlertManager
{
/// <summary>
/// Creates and registers a DispatchProxy for IAlertManagerSubscription via DI.
/// MAUI's AlertManager.Subscribe() resolves this from DI before falling back
/// to the platform-specific AlertRequestHelper.
/// </summary>
public static void Register(IServiceCollection services)
{
try
{
var amType = typeof(Window).Assembly
.GetType("Microsoft.Maui.Controls.Platform.AlertManager");
if (amType == null) return;

var iamsType = amType.GetNestedType("IAlertManagerSubscription",
BindingFlags.Public | BindingFlags.NonPublic);
if (iamsType == null) return;

var proxyType = typeof(AlertSubscriptionProxy<>).MakeGenericType(iamsType);
var createMethod = typeof(DispatchProxy)
.GetMethods(BindingFlags.Public | BindingFlags.Static)
.First(m => m.Name == "Create" && m.GetGenericArguments().Length == 2)
.MakeGenericMethod(iamsType, proxyType);

var proxy = createMethod.Invoke(null, null);
if (proxy == null) return;

services.AddSingleton(iamsType, proxy);
}
catch { }
}

static Gtk.Window? GetGtkWindow(object? page)
{
if (page is Page mauiPage)
{
var window = mauiPage.GetParentWindow();
if (window?.Handler?.PlatformView is Gtk.Window gtkWin)
return gtkWin;
}

var app = Gtk.Application.GetDefault();
if (app is Gtk.Application gtkApp)
return gtkApp.GetActiveWindow();

return null;
}

static (Gtk.Window dialog, Gtk.Box content) CreateDialogWindow(
Gtk.Window parent, string title, string? message, int width = 400)
{
var dialog = new Gtk.Window();
dialog.SetTitle(title);
dialog.SetModal(true);
dialog.SetTransientFor(parent);
dialog.SetDefaultSize(width, -1);
dialog.SetResizable(false);

var app = parent.GetApplication();
if (app != null)
dialog.SetApplication(app);

var box = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
box.SetMarginTop(20);
box.SetMarginBottom(20);
box.SetMarginStart(20);
box.SetMarginEnd(20);

if (!string.IsNullOrEmpty(message))
{
var msgLabel = Gtk.Label.New(message);
msgLabel.SetWrap(true);
msgLabel.SetXalign(0);
box.Append(msgLabel);
}

dialog.SetChild(box);
return (dialog, box);
}

public class AlertSubscriptionProxy<T> : DispatchProxy
{
protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
{
if (targetMethod == null || args == null)
return null;

switch (targetMethod.Name)
{
case "OnAlertRequested":
HandleAlert(args);
break;
case "OnActionSheetRequested":
HandleActionSheet(args);
break;
case "OnPromptRequested":
HandlePrompt(args);
break;
}

return null;
}

static void HandleAlert(object?[] args)
{
if (args.Length < 2 || args[1] == null) return;

var alertArgs = args[1]!;
var title = GetProp<string>(alertArgs, "Title") ?? "Alert";
var message = GetProp<string>(alertArgs, "Message") ?? "";
var accept = GetProp<string>(alertArgs, "Accept");
var cancel = GetProp<string>(alertArgs, "Cancel");
var result = GetProp<object>(alertArgs, "Result");
var trySetResult = result?.GetType().GetMethod("TrySetResult");

GLib.Functions.IdleAdd(0, () =>
{
var gtkWindow = GetGtkWindow(args[0]);
if (gtkWindow == null)
{
trySetResult?.Invoke(result, [false]);
return false;
}

var (dialog, box) = CreateDialogWindow(gtkWindow, title, message);
bool responded = false;

var buttonBox = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
buttonBox.SetHalign(Gtk.Align.End);

if (!string.IsNullOrEmpty(accept))
{
var acceptBtn = Gtk.Button.NewWithLabel(accept);
acceptBtn.OnClicked += (_, _) =>
{
if (responded) return;
responded = true;
trySetResult?.Invoke(result, [true]);
dialog.Close();
};
buttonBox.Append(acceptBtn);
}

if (!string.IsNullOrEmpty(cancel))
{
var cancelBtn = Gtk.Button.NewWithLabel(cancel);
cancelBtn.OnClicked += (_, _) =>
{
if (responded) return;
responded = true;
trySetResult?.Invoke(result, [false]);
dialog.Close();
};
buttonBox.Append(cancelBtn);
}

if (string.IsNullOrEmpty(accept) && string.IsNullOrEmpty(cancel))
{
var okBtn = Gtk.Button.NewWithLabel("OK");
okBtn.OnClicked += (_, _) =>
{
if (responded) return;
responded = true;
trySetResult?.Invoke(result, [false]);
dialog.Close();
};
buttonBox.Append(okBtn);
}

dialog.OnCloseRequest += (_, _) =>
{
if (!responded)
{
responded = true;
trySetResult?.Invoke(result, [false]);
}
return false;
};

box.Append(buttonBox);
dialog.Present();

return false;
});
}

static void HandleActionSheet(object?[] args)
{
if (args.Length < 2 || args[1] == null) return;

var sheetArgs = args[1]!;
var title = GetProp<string>(sheetArgs, "Title") ?? "";
var cancel = GetProp<string>(sheetArgs, "Cancel") ?? "Cancel";
var destruction = GetProp<string>(sheetArgs, "Destruction");
var buttons = GetProp<string[]>(sheetArgs, "Buttons");
var result = GetProp<object>(sheetArgs, "Result");
var trySetResult = result?.GetType().GetMethod("TrySetResult");

GLib.Functions.IdleAdd(0, () =>
{
var gtkWindow = GetGtkWindow(args[0]);
if (gtkWindow == null)
{
trySetResult?.Invoke(result, [cancel]);
return false;
}

var (dialog, box) = CreateDialogWindow(gtkWindow, title, null);
bool responded = false;

if (buttons != null)
{
foreach (var btnText in buttons)
{
var btn = Gtk.Button.NewWithLabel(btnText);
btn.OnClicked += (_, _) =>
{
if (responded) return;
responded = true;
trySetResult?.Invoke(result, [btnText]);
dialog.Close();
};
box.Append(btn);
}
}

if (!string.IsNullOrEmpty(destruction))
{
var destroyBtn = Gtk.Button.NewWithLabel(destruction);
destroyBtn.AddCssClass("destructive-action");
destroyBtn.OnClicked += (_, _) =>
{
if (responded) return;
responded = true;
trySetResult?.Invoke(result, [destruction]);
dialog.Close();
};
box.Append(destroyBtn);
}

var cancelBtn = Gtk.Button.NewWithLabel(cancel);
cancelBtn.OnClicked += (_, _) =>
{
if (responded) return;
responded = true;
trySetResult?.Invoke(result, [cancel]);
dialog.Close();
};
box.Append(cancelBtn);

dialog.OnCloseRequest += (_, _) =>
{
if (!responded)
{
responded = true;
trySetResult?.Invoke(result, [cancel]);
}
return false;
};

dialog.Present();

return false;
});
}

static void HandlePrompt(object?[] args)
{
if (args.Length < 2 || args[1] == null) return;

var promptArgs = args[1]!;
var title = GetProp<string>(promptArgs, "Title") ?? "Prompt";
var message = GetProp<string>(promptArgs, "Message") ?? "";
var accept = GetProp<string>(promptArgs, "Accept") ?? "OK";
var cancel = GetProp<string>(promptArgs, "Cancel") ?? "Cancel";
var placeholder = GetProp<string>(promptArgs, "Placeholder") ?? "";
var initialValue = GetProp<string>(promptArgs, "InitialValue") ?? "";
var result = GetProp<object>(promptArgs, "Result");
var trySetResult = result?.GetType().GetMethod("TrySetResult");

GLib.Functions.IdleAdd(0, () =>
{
var gtkWindow = GetGtkWindow(args[0]);
if (gtkWindow == null)
{
trySetResult?.Invoke(result, [null]);
return false;
}

var (dialog, box) = CreateDialogWindow(gtkWindow, title, message);
bool responded = false;

var entry = Gtk.Entry.New();
if (!string.IsNullOrEmpty(placeholder))
entry.SetPlaceholderText(placeholder);
if (!string.IsNullOrEmpty(initialValue))
entry.GetBuffer().SetText(initialValue, initialValue.Length);
box.Append(entry);

var buttonBox = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
buttonBox.SetHalign(Gtk.Align.End);

var cancelBtn = Gtk.Button.NewWithLabel(cancel);
var acceptBtn = Gtk.Button.NewWithLabel(accept);

cancelBtn.OnClicked += (_, _) =>
{
if (responded) return;
responded = true;
trySetResult?.Invoke(result, [null]);
dialog.Close();
};

acceptBtn.OnClicked += (_, _) =>
{
if (responded) return;
responded = true;
trySetResult?.Invoke(result, [entry.GetBuffer().GetText()]);
dialog.Close();
};

entry.OnActivate += (_, _) =>
{
if (responded) return;
responded = true;
trySetResult?.Invoke(result, [entry.GetBuffer().GetText()]);
dialog.Close();
};

dialog.OnCloseRequest += (_, _) =>
{
if (!responded)
{
responded = true;
trySetResult?.Invoke(result, [null]);
}
return false;
};

buttonBox.Append(cancelBtn);
buttonBox.Append(acceptBtn);
box.Append(buttonBox);
dialog.Present();
entry.GrabFocus();

return false;
});
}

static TResult? GetProp<TResult>(object obj, string name)
{
var prop = obj.GetType().GetProperty(name);
return prop != null ? (TResult?)prop.GetValue(obj) : default;
}
}
}
