using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Controls.Platform;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

/// <summary>
/// Handles MAUI DisplayAlert, DisplayActionSheet, and DisplayPromptAsync
/// using custom GTK4 dialog windows. Registers an implementation of MAUI's
/// public <see cref="IAlertManagerSubscription"/> via DI so the default
/// AlertManager.Subscribe() discovers it automatically.
/// </summary>
public static class GtkAlertManager
{
/// <summary>
/// Registers the GTK <see cref="IAlertManagerSubscription"/> via DI.
/// MAUI's AlertManager.Subscribe() resolves this from DI before falling back
/// to its no-op default subscription.
/// </summary>
public static void Register(IServiceCollection services)
{
services.AddSingleton<IAlertManagerSubscription>(new AlertSubscription());
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

public sealed class AlertSubscription : IAlertManagerSubscription
{
public void OnAlertRequested(Page sender, AlertArguments arguments) => HandleAlert(sender, arguments);

public void OnActionSheetRequested(Page sender, ActionSheetArguments arguments) => HandleActionSheet(sender, arguments);

public void OnPromptRequested(Page sender, PromptArguments arguments) => HandlePrompt(sender, arguments);

static void HandleAlert(Page sender, AlertArguments? alertArgs)
{
if (alertArgs == null) return;

var title = alertArgs.Title ?? "Alert";
var message = alertArgs.Message ?? "";
var accept = alertArgs.Accept;
var cancel = alertArgs.Cancel;
var result = alertArgs.Result;

GLib.Functions.IdleAdd(0, () =>
{
var gtkWindow = GetGtkWindow(sender);
if (gtkWindow == null)
{
result.TrySetResult(false);
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
result.TrySetResult(true);
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
result.TrySetResult(false);
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
result.TrySetResult(false);
dialog.Close();
};
buttonBox.Append(okBtn);
}

dialog.OnCloseRequest += (_, _) =>
{
if (!responded)
{
responded = true;
result.TrySetResult(false);
}
return false;
};

box.Append(buttonBox);
dialog.Present();

return false;
});
}

static void HandleActionSheet(Page sender, ActionSheetArguments? sheetArgs)
{
if (sheetArgs == null) return;

var title = sheetArgs.Title ?? "";
var cancel = sheetArgs.Cancel ?? "Cancel";
var destruction = sheetArgs.Destruction;
var buttons = sheetArgs.Buttons?.ToArray() ?? Array.Empty<string>();
var result = sheetArgs.Result;

GLib.Functions.IdleAdd(0, () =>
{
var gtkWindow = GetGtkWindow(sender);
if (gtkWindow == null)
{
result.TrySetResult(cancel);
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
result.TrySetResult(btnText);
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
result.TrySetResult(destruction);
dialog.Close();
};
box.Append(destroyBtn);
}

var cancelBtn = Gtk.Button.NewWithLabel(cancel);
cancelBtn.OnClicked += (_, _) =>
{
if (responded) return;
responded = true;
result.TrySetResult(cancel);
dialog.Close();
};
box.Append(cancelBtn);

dialog.OnCloseRequest += (_, _) =>
{
if (!responded)
{
responded = true;
result.TrySetResult(cancel);
}
return false;
};

dialog.Present();

return false;
});
}

static void HandlePrompt(Page sender, PromptArguments? promptArgs)
{
if (promptArgs == null) return;

var title = promptArgs.Title ?? "Prompt";
var message = promptArgs.Message ?? "";
var accept = promptArgs.Accept ?? "OK";
var cancel = promptArgs.Cancel ?? "Cancel";
var placeholder = promptArgs.Placeholder ?? "";
var initialValue = promptArgs.InitialValue ?? "";
var result = promptArgs.Result;

GLib.Functions.IdleAdd(0, () =>
{
var gtkWindow = GetGtkWindow(sender);
if (gtkWindow == null)
{
result.TrySetResult(null);
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
result.TrySetResult(null);
dialog.Close();
};

acceptBtn.OnClicked += (_, _) =>
{
if (responded) return;
responded = true;
result.TrySetResult(entry.GetBuffer().GetText());
dialog.Close();
};

entry.OnActivate += (_, _) =>
{
if (responded) return;
responded = true;
result.TrySetResult(entry.GetBuffer().GetText());
dialog.Close();
};

dialog.OnCloseRequest += (_, _) =>
{
if (!responded)
{
responded = true;
result.TrySetResult(null);
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
}
}
