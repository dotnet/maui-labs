using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
#if IOS || MACCATALYST
using UIKit;
#endif
#if MACOS
using AppKit;
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if WINDOWS
using Microsoft.Maui.DevFlow.Agent.Windows;
#endif

namespace Microsoft.Maui.DevFlow.Agent;

/// <summary>
/// Platform-specific visual tree walker that provides native view info
/// for Android, iOS, Mac Catalyst, Windows, and macOS AppKit.
/// </summary>
public class PlatformVisualTreeWalker : VisualTreeWalker
{
    public PlatformVisualTreeWalker()
    {
    }

    internal PlatformVisualTreeWalker(NativeElementRegistrationRegistry nativeElementRegistry)
        : base(nativeElementRegistry)
    {
    }

    internal override ElementInfo CreateRegisteredNativeElementInfo(
        NativeElementRegistrationSnapshot registration,
        string? ownerId)
    {
        var info = base.CreateRegisteredNativeElementInfo(registration, ownerId);
        try
        {
#if ANDROID
            info.Framework = "android-native";
            info.IsVisible = false;
            if (registration.NativeElement is global::Android.Views.View androidView)
            {
                var density = androidView.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
                info.NativeProperties ??= new Dictionary<string, string?>();
                info.NativeProperties["displayDensity"] = density.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                var hasExactBounds = androidView.IsAttachedToWindow
                    && androidView.Width > 0
                    && androidView.Height > 0;
                if (hasExactBounds)
                {
                    var location = new int[2];
                    GetAndroidLocationInAppWindow(androidView, location);
                    info.WindowBounds = new BoundsInfo
                    {
                        X = location[0] / density,
                        Y = location[1] / density,
                        Width = androidView.Width / density,
                        Height = androidView.Height / density
                    };
                    info.BoundsQuality = "exact";
                }
                info.IsVisible = hasExactBounds
                    && androidView.Visibility == global::Android.Views.ViewStates.Visible
                    && androidView.IsShown;
                info.IsEnabled = androidView.Enabled;
                info.IsFocused = androidView.HasFocus;
                if (androidView.Id != global::Android.Views.View.NoId)
                {
                    try
                    {
                        info.AutomationId = androidView.Resources?.GetResourceEntryName(androidView.Id);
                    }
                    catch
                    {
                    }
                }
                if (androidView is global::Android.Widget.TextView textView)
                {
                    var isPassword = textView is global::Android.Widget.EditText editText
                        && editText.TransformationMethod
                            is global::Android.Text.Method.PasswordTransformationMethod;
                    info.Text = SensitiveValueRedactor.Redact(textView.Text, isPassword);
                    info.NativeProperties["isPassword"] = isPassword.ToString();
                }
                else
                {
                    info.Text = androidView.ContentDescription;
                }
            }
            else if (registration.NativeElement is global::Android.Views.IMenuItem menuItem)
            {
                info.Type = "MenuItem";
                info.IsEnabled = menuItem.IsEnabled;
                info.IsFocused = menuItem.IsChecked;
                info.Text = menuItem.TitleFormatted?.ToString();
                info.NativeProperties ??= new Dictionary<string, string?>();
                info.NativeProperties["itemId"] = menuItem.ItemId.ToString();
                info.NativeProperties["groupId"] = menuItem.GroupId.ToString();
                info.NativeProperties["isCheckable"] = menuItem.IsCheckable.ToString();
                info.NativeProperties["isChecked"] = menuItem.IsChecked.ToString();
                info.NativeProperties["logicalVisibility"] = menuItem.IsVisible.ToString();
                if (menuItem.ActionView is global::Android.Views.View actionView
                    && actionView.IsAttachedToWindow
                    && actionView.Width > 0
                    && actionView.Height > 0)
                {
                    var location = new int[2];
                    GetAndroidLocationInAppWindow(actionView, location);
                    var density = actionView.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
                    info.NativeProperties["displayDensity"] = density.ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
                    info.WindowBounds = new BoundsInfo
                    {
                        X = location[0] / density,
                        Y = location[1] / density,
                        Width = actionView.Width / density,
                        Height = actionView.Height / density
                    };
                    info.BoundsQuality = "exact";
                    info.IsVisible = menuItem.IsVisible
                        && actionView.Visibility == global::Android.Views.ViewStates.Visible
                        && actionView.IsShown;
                }
            }
#elif IOS || MACCATALYST
            if (registration.NativeElement is MenuItem)
            {
                info.Framework = "maui-logical";
                return info;
            }

            info.Framework = "apple-native";
            info.IsVisible = false;
            if (registration.NativeElement is UIKit.UIView uiView)
            {
                var window = uiView.Window;
                if (window is not null && uiView.Bounds.Width > 0 && uiView.Bounds.Height > 0)
                {
                    var bounds = uiView.ConvertRectToView(uiView.Bounds, window);
                    info.WindowBounds = new BoundsInfo
                    {
                        X = bounds.X,
                        Y = bounds.Y,
                        Width = bounds.Width,
                        Height = bounds.Height
                    };
                    info.BoundsQuality = "exact";
                    info.NativeProperties ??= new Dictionary<string, string?>();
                    info.NativeProperties["displayDensity"] = window.Screen.Scale.ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
                }
                info.IsVisible = IsAppleViewVisible(uiView)
                    || IsAttachedAppleDialogSurfaceVisible(registration, uiView);
                info.IsEnabled = uiView is not UIKit.UIControl control || control.Enabled;
                info.IsFocused = uiView.IsFirstResponder;
                info.AutomationId = uiView.AccessibilityIdentifier;
                if (uiView is UIKit.UITextField textField)
                {
                    info.Text = SensitiveValueRedactor.Redact(
                        textField.Text,
                        textField.SecureTextEntry);
                    info.NativeProperties ??= new Dictionary<string, string?>();
                    info.NativeProperties["isPassword"] = textField.SecureTextEntry.ToString();
                }
                else if (uiView is UIKit.UISearchBar searchBar)
                {
                    info.Text = searchBar.Text;
                }
                else if (uiView is UIKit.UIButton button)
                {
                    info.Text = button.Title(UIKit.UIControlState.Normal)
                        ?? uiView.AccessibilityLabel;
                }
                else if (uiView is UIKit.UILabel label)
                {
                    info.Text = label.Text;
                }
                else
                {
                    info.Text = uiView.AccessibilityLabel;
                }
            }
            else if (registration.NativeElement is UIKit.UIBarItem barItem)
            {
                info.Type = barItem.GetType().Name;
                info.IsEnabled = barItem.Enabled;
                info.AutomationId = barItem.AccessibilityIdentifier;
                info.Text = barItem.AccessibilityLabel ?? barItem.Title;
                var frame = barItem.AccessibilityFrame;
                var ownerWindow = FindAppleWindow(registration.Owner)
                    ?? FindAppleWindow(frame);
                if (!frame.IsEmpty && ownerWindow is not null)
                {
                    var windowFrame = ownerWindow.ConvertRectFromCoordinateSpace(
                        frame,
                        ownerWindow.Screen.CoordinateSpace);
                    info.WindowBounds = new BoundsInfo
                    {
                        X = windowFrame.X,
                        Y = windowFrame.Y,
                        Width = windowFrame.Width,
                        Height = windowFrame.Height
                    };
                    info.BoundsQuality = "accessibility";
                    info.IsVisible = ownerWindow.Bounds.IntersectsWith(windowFrame);
                    info.NativeProperties ??= new Dictionary<string, string?>();
                    info.NativeProperties["displayDensity"] = ownerWindow.Screen.Scale.ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            else if (registration.NativeElement is UIKit.UIMenuElement menuElement)
            {
                info.Type = menuElement.GetType().Name;
                info.IsEnabled = menuElement switch
                {
                    UIKit.UIAction action => !action.Attributes.HasFlag(UIKit.UIMenuElementAttributes.Disabled),
                    UIKit.UICommand command => !command.Attributes.HasFlag(UIKit.UIMenuElementAttributes.Disabled),
                    _ => true
                };
                info.Text = menuElement.Title;
                info.NativeProperties ??= new Dictionary<string, string?>();
                info.NativeProperties["logicalVisibility"] = bool.TrueString;
            }
            else if (registration.NativeElement is UIKit.UIAlertAction alertAction)
            {
                info.Type = nameof(UIKit.UIAlertAction);
                info.IsEnabled = alertAction.Enabled;
                info.Text = alertAction.Title;
                info.NativeProperties ??= new Dictionary<string, string?>();
                info.NativeProperties["style"] = alertAction.Style.ToString();
                info.NativeProperties["logicalVisibility"] = bool.TrueString;
            }
#elif WINDOWS
            info.Framework = "windows-native";
            if (registration.NativeElement is Microsoft.UI.Xaml.FrameworkElement frameworkElement)
            {
                var hasExactBounds = frameworkElement.IsLoaded
                    && frameworkElement.XamlRoot is not null
                    && frameworkElement.ActualWidth > 0
                    && frameworkElement.ActualHeight > 0;
                if (hasExactBounds)
                {
                    var point = frameworkElement.TransformToVisual(null)
                        .TransformPoint(new global::Windows.Foundation.Point(0, 0));
                    info.WindowBounds = new BoundsInfo
                    {
                        X = point.X,
                        Y = point.Y,
                        Width = frameworkElement.ActualWidth,
                        Height = frameworkElement.ActualHeight
                    };
                    info.BoundsQuality = "exact";
                }
                info.IsVisible = hasExactBounds && IsWinUiElementVisible(frameworkElement);
                info.IsEnabled = frameworkElement is not Microsoft.UI.Xaml.Controls.Control control || control.IsEnabled;
                info.IsFocused = frameworkElement.FocusState != Microsoft.UI.Xaml.FocusState.Unfocused;
                info.AutomationId = Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(frameworkElement);
                info.Text = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(frameworkElement);
                if (string.IsNullOrEmpty(info.Text)
                    && frameworkElement is Microsoft.UI.Xaml.Controls.AppBarButton appBarButton)
                {
                    info.Text = appBarButton.Label;
                }
            }
#elif MACOS
            info.Framework = "macos-native";
            if (registration.NativeElement is AppKit.NSView nsView)
            {
                var contentView = nsView.Window?.ContentView;
                if (contentView is not null)
                {
                    var bounds = nsView.ConvertRectToView(nsView.Bounds, contentView);
                    var windowX = bounds.X;
                    var windowY = contentView.Bounds.Height - bounds.Y - bounds.Height;
                    if (nsView.Window is { } nativeWindow
                        && FindAppKitOwnerWindow(registration.Owner, nativeWindow)
                            is { } ownerWindow)
                    {
                        windowX += nativeWindow.Frame.X - ownerWindow.Frame.X;
                        windowY += ownerWindow.Frame.Y + ownerWindow.Frame.Height
                            - nativeWindow.Frame.Y - nativeWindow.Frame.Height;
                    }

                    info.WindowBounds = new BoundsInfo
                    {
                        X = windowX,
                        Y = windowY,
                        Width = bounds.Width,
                        Height = bounds.Height
                    };
                    info.BoundsQuality = "exact";
                }
                info.IsVisible = IsAppKitViewVisible(nsView);
                info.IsEnabled = nsView is not AppKit.NSControl control || control.Enabled;
                info.IsFocused = nsView is AppKit.NSTextField textField
                    ? textField.CurrentEditor is not null
                    : nsView.Window?.FirstResponder == nsView;
                info.AutomationId = nsView.AccessibilityIdentifier
                    ?? (registration.Owner as Element)?.AutomationId;
                if (nsView is AppKit.NSButton nsButton)
                {
                    info.Text = string.IsNullOrEmpty(nsButton.Title)
                        ? nsButton.AccessibilityLabel
                        : nsButton.Title;
                }
                else if (nsView is AppKit.NSTextField nsTextField)
                {
                    var isPassword = nsTextField is AppKit.NSSecureTextField;
                    info.Text = SensitiveValueRedactor.Redact(nsTextField.StringValue, isPassword);
                    info.NativeProperties ??= new Dictionary<string, string?>();
                    info.NativeProperties["isPassword"] = isPassword.ToString();
                    if (!string.IsNullOrEmpty(nsTextField.PlaceholderString))
                        info.NativeProperties["placeholder"] = nsTextField.PlaceholderString;
                }
                else
                {
                    info.Text = nsView.AccessibilityLabel;
                }
            }
            else if (registration.NativeElement is AppKit.NSToolbarItem toolbarItem)
            {
                info.Type = toolbarItem.GetType().Name;
                info.IsEnabled = toolbarItem.Enabled;
                info.Text = toolbarItem.Label ?? toolbarItem.ToolTip;
                var itemView = toolbarItem is AppKit.NSSearchToolbarItem searchToolbarItem
                    ? searchToolbarItem.SearchField
                    : toolbarItem.View;
                info.IsVisible = !toolbarItem.Hidden;
                if (itemView?.Window?.ContentView is { } contentView)
                {
                    var bounds = itemView.ConvertRectToView(itemView.Bounds, contentView);
                    info.WindowBounds = new BoundsInfo
                    {
                        X = bounds.X,
                        Y = contentView.Bounds.Height - bounds.Y - bounds.Height,
                        Width = bounds.Width,
                        Height = bounds.Height
                    };
                    info.BoundsQuality = "exact";
                    info.IsVisible = info.IsVisible && IsAppKitViewVisible(itemView);
                    info.IsFocused = itemView.Window?.FirstResponder == itemView;
                    info.AutomationId = itemView.AccessibilityIdentifier;
                    info.Text ??= itemView.AccessibilityLabel;
                }

                info.Capabilities ??= ["select"];
                if (toolbarItem is AppKit.NSSearchToolbarItem)
                {
                    AddCapability(info.Capabilities, "invoke");
                    AddCapability(info.Capabilities, "focus");
                    AddCapability(info.Capabilities, "set-value");
                }
                else if (toolbarItem.Action is not null)
                {
                    AddCapability(info.Capabilities, "invoke");
                }
            }
#endif
        }
        catch
        {
            info.BoundsQuality = "unknown";
        }

        return info;
    }

#if ANDROID
    private static void GetAndroidLocationInAppWindow(
        global::Android.Views.View view,
        int[] location)
    {
        view.GetLocationOnScreen(location);
        var rootView = FindAndroidActivity(view.Context)?
            .Window?
            .DecorView?
            .RootView
            ?? view.RootView;
        if (rootView is null)
            return;

        var rootLocation = new int[2];
        rootView.GetLocationOnScreen(rootLocation);
        location[0] -= rootLocation[0];
        location[1] -= rootLocation[1];
    }

    private static global::Android.App.Activity? FindAndroidActivity(
        global::Android.Content.Context? context)
    {
        while (context is global::Android.Content.ContextWrapper wrapper)
        {
            if (context is global::Android.App.Activity activity)
                return activity;

            var baseContext = wrapper.BaseContext;
            if (ReferenceEquals(baseContext, context))
                break;
            context = baseContext;
        }

        return context as global::Android.App.Activity;
    }
#endif

#if WINDOWS
    private static bool IsWinUiElementVisible(Microsoft.UI.Xaml.FrameworkElement element)
    {
        Microsoft.UI.Xaml.DependencyObject? current = element;
        while (current is Microsoft.UI.Xaml.FrameworkElement frameworkElement)
        {
            if (frameworkElement.Visibility != Microsoft.UI.Xaml.Visibility.Visible
                || frameworkElement.Opacity <= 0)
            {
                return false;
            }

            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(frameworkElement);
        }

        return element.IsLoaded && element.XamlRoot is not null;
    }
#endif

#if MACOS
    private static bool IsAppKitViewVisible(AppKit.NSView view)
    {
        if (view.Window is null)
            return false;

        AppKit.NSView? current = view;
        while (current is not null)
        {
            if (current.Hidden || current.AlphaValue <= 0)
                return false;
            current = current.Superview;
        }

        return true;
    }

    private static AppKit.NSWindow? FindAppKitOwnerWindow(
        object owner,
        AppKit.NSWindow nativeWindow)
    {
        static AppKit.NSWindow? FromPlatformView(object? platformView) => platformView switch
        {
            AppKit.NSWindow window => window,
            AppKit.NSView view => view.Window,
            AppKit.NSWindowController controller => controller.Window,
            AppKit.NSViewController viewController => viewController.View?.Window,
            _ => null
        };

        static bool IsDifferentWindow(
            AppKit.NSWindow? candidate,
            AppKit.NSWindow nativeWindow)
            => candidate != null && candidate.Handle != nativeWindow.Handle;

        static Window? FindMauiWindow(object owner)
        {
            for (var element = owner as Element; element is not null; element = element.Parent)
            {
                if (element is Page page && page.Window is not null)
                    return page.Window;
            }

            return null;
        }

        if (nativeWindow.SheetParent is { } sheetParent)
            return sheetParent;

        var ownerWindow = FromPlatformView(
            FindMauiWindow(owner)?.Handler?.PlatformView);
        if (IsDifferentWindow(ownerWindow, nativeWindow))
            return ownerWindow;

        return null;
    }
#endif

    private static void AddCapability(List<string> capabilities, string capability)
    {
        if (!capabilities.Contains(capability, StringComparer.Ordinal))
            capabilities.Add(capability);
    }

#if IOS || MACCATALYST
    private static bool IsAppleViewVisible(UIKit.UIView view)
    {
        if (view.Window is null)
            return false;

        UIKit.UIView? current = view;
        while (current is not null)
        {
            if (current.Hidden || current.Alpha <= 0)
                return false;
            current = current.Superview;
        }

        return true;
    }

    private static bool IsAttachedAppleDialogSurfaceVisible(
        NativeElementRegistrationSnapshot registration,
        UIKit.UIView view)
    {
        if (registration.Discriminator?.Equals("RealizedView", StringComparison.Ordinal) != true
            || !(registration.Role.Equals("Dialog", StringComparison.Ordinal)
                || registration.Role.Equals("DialogAction", StringComparison.Ordinal))
            || view.Window is not { } window
            || view.Hidden
            || view.Alpha <= 0
            || view.Bounds.Width <= 0
            || view.Bounds.Height <= 0)
        {
            return false;
        }

        var bounds = view.ConvertRectToView(view.Bounds, window);
        var visibleMinX = Math.Max(window.Bounds.X, bounds.X);
        var visibleMinY = Math.Max(window.Bounds.Y, bounds.Y);
        var visibleMaxX = Math.Min(
            window.Bounds.X + window.Bounds.Width,
            bounds.X + bounds.Width);
        var visibleMaxY = Math.Min(
            window.Bounds.Y + window.Bounds.Height,
            bounds.Y + bounds.Height);

        for (var ancestor = view.Superview; ancestor is not null; ancestor = ancestor.Superview)
        {
            if (!ancestor.ClipsToBounds)
                continue;

            var clipBounds = ancestor.ConvertRectToView(ancestor.Bounds, window);
            visibleMinX = Math.Max(visibleMinX, clipBounds.X);
            visibleMinY = Math.Max(visibleMinY, clipBounds.Y);
            visibleMaxX = Math.Min(visibleMaxX, clipBounds.X + clipBounds.Width);
            visibleMaxY = Math.Min(visibleMaxY, clipBounds.Y + clipBounds.Height);
        }

        return visibleMaxX > visibleMinX && visibleMaxY > visibleMinY;
    }

    private static UIKit.UITableView? FindAppleTableView(UIKit.UIView view)
    {
        UIKit.UIView? current = view.Superview;
        while (current is not null)
        {
            if (current is UIKit.UITableView tableView)
                return tableView;
            current = current.Superview;
        }

        return null;
    }

    private static UIKit.UIWindow? FindAppleWindow(object owner)
    {
        var current = owner as Element;
        while (current is not null)
        {
            if (current is Microsoft.Maui.Controls.Window window
                && window.Handler?.PlatformView is UIKit.UIWindow uiWindow)
                return uiWindow;
            current = current.Parent;
        }

        return null;
    }

    private static UIKit.UIWindow? FindAppleWindow(CoreGraphics.CGRect screenFrame)
    {
        UIKit.UIWindow? fallback = null;
        foreach (var scene in UIKit.UIApplication.SharedApplication.ConnectedScenes)
        {
            if (scene is not UIKit.UIWindowScene windowScene)
                continue;

            foreach (var window in windowScene.Windows)
            {
                if (window.Hidden || window.Alpha <= 0)
                    continue;
                fallback ??= window;
                if (window.Frame.IntersectsWith(screenFrame))
                    return window;
            }
        }

        return fallback;
    }
#endif

#if ANDROID || IOS || MACCATALYST || MACOS
    protected override bool CanInvokeRegisteredNativeElement(object nativeElement)
    {
#if ANDROID
        return nativeElement is global::Android.Views.View
        {
            IsAttachedToWindow: true,
            IsShown: true,
            Enabled: true,
            Clickable: true
        };
#elif IOS || MACCATALYST
        if (nativeElement is UIKit.UITableViewCell cell)
        {
            var tableView = FindAppleTableView(cell);
            return IsAppleViewVisible(cell)
                && cell.UserInteractionEnabled
                && tableView?.IndexPathForCell(cell) is not null
                && tableView.Delegate is not null;
        }

        if (nativeElement is UIKit.UIControl control)
            return IsAppleViewVisible(control)
                && control.Enabled
                && (control.AllTargets.Count > 0
                    || control is UIKit.UIButton
                    {
                        Menu: not null,
                        ShowsMenuAsPrimaryAction: true
                    } && (OperatingSystem.IsIOSVersionAtLeast(17, 4)
                        || OperatingSystem.IsMacCatalystVersionAtLeast(17, 4)));

        return nativeElement is UIKit.UIBarButtonItem
        {
            Enabled: true,
            Action: not null
        };
#elif MACOS
        return nativeElement is AppKit.NSButton { Enabled: true } button
            && IsAppKitViewVisible(button);
#endif
    }

    protected override bool CanFocusRegisteredNativeElement(object nativeElement)
    {
#if ANDROID
        return nativeElement is global::Android.Views.View
        {
            IsAttachedToWindow: true,
            IsShown: true,
            Enabled: true,
            Focusable: true
        };
#elif IOS || MACCATALYST
        return nativeElement is UIKit.UIView view
            && IsAppleViewVisible(view)
            && view.CanBecomeFirstResponder;
#elif MACOS
        return nativeElement is AppKit.NSView view
            && (view is not AppKit.NSControl control || control.Enabled)
            && IsAppKitViewVisible(view)
            && view.AcceptsFirstResponder();
#endif
    }

    protected override bool CanSetValueRegisteredNativeElement(object nativeElement)
    {
#if ANDROID
        return nativeElement is global::Android.Widget.EditText
        {
            IsAttachedToWindow: true,
            Enabled: true
        };
#elif IOS || MACCATALYST
        return nativeElement is UIKit.UITextField { Enabled: true }
            or UIKit.UISearchBar { UserInteractionEnabled: true };
#elif MACOS
        return nativeElement is AppKit.NSTextField { Enabled: true, Editable: true } textField
            && IsAppKitViewVisible(textField);
#endif
    }

    protected override string? TrySetValueRegisteredNativeElement(
        string elementId,
        object nativeElement,
        string value)
    {
#if ANDROID
        if (nativeElement is global::Android.Widget.EditText editText)
        {
            if (!CanSetValueRegisteredNativeElement(editText))
                return $"Native element '{elementId}' is not attached and enabled";

            editText.Text = value;
            editText.SetSelection(editText.Text?.Length ?? 0);
            return "ok";
        }
#elif IOS || MACCATALYST
        if (nativeElement is UIKit.UITextField textField)
        {
            if (!CanSetValueRegisteredNativeElement(textField))
                return $"Native element '{elementId}' is not enabled";

            textField.Text = value;
            textField.SendActionForControlEvents(UIKit.UIControlEvent.EditingChanged);
            return "ok";
        }

        if (nativeElement is UIKit.UISearchBar searchBar)
        {
            if (!CanSetValueRegisteredNativeElement(searchBar))
                return $"Native element '{elementId}' is not enabled";

            searchBar.Text = value;
            return "ok";
        }
#elif MACOS
        if (nativeElement is AppKit.NSTextField textField)
        {
            if (!CanSetValueRegisteredNativeElement(textField))
                return $"Native element '{elementId}' is not editable and enabled";

            textField.StringValue = value;
            return "ok";
        }
#endif

        return null;
    }

    protected override string? TryInvokeRegisteredNativeElement(
        string elementId,
        object nativeElement)
    {
#if ANDROID
        if (nativeElement is global::Android.Views.View androidView)
        {
            if (!CanInvokeRegisteredNativeElement(androidView))
                return $"Native element '{elementId}' is not attached, visible, enabled, and clickable";

            return androidView.PerformClick()
                ? "ok"
                : $"Native element '{elementId}' did not handle the click";
        }
#elif IOS || MACCATALYST
        if (nativeElement is UIKit.UITableViewCell cell)
        {
            var tableView = FindAppleTableView(cell);
            var indexPath = tableView?.IndexPathForCell(cell);
            if (!CanInvokeRegisteredNativeElement(cell)
                || tableView is null
                || indexPath is null)
            {
                return $"Native element '{elementId}' is not a visible, actionable table row";
            }

            tableView.SelectRow(indexPath, animated: true, UIKit.UITableViewScrollPosition.None);
            tableView.Delegate?.RowSelected(tableView, indexPath);
            return "ok";
        }

        if (nativeElement is UIKit.UIControl control)
        {
            if (!CanInvokeRegisteredNativeElement(control))
                return $"Native element '{elementId}' is not visible, enabled, and actionable";

            if (control is UIKit.UIButton
                {
                    Menu: not null,
                    ShowsMenuAsPrimaryAction: true
                } menuButton)
            {
                if (OperatingSystem.IsIOSVersionAtLeast(17, 4)
                    || OperatingSystem.IsMacCatalystVersionAtLeast(17, 4))
                {
                    menuButton.PerformPrimaryAction();
                    return "ok";
                }

                return $"Native menu launcher '{elementId}' requires iOS or Mac Catalyst 17.4 or later for programmatic invocation";
            }

            control.SendActionForControlEvents(UIKit.UIControlEvent.TouchUpInside);
            return "ok";
        }

        if (nativeElement is UIKit.UIBarButtonItem barButtonItem)
        {
            if (!CanInvokeRegisteredNativeElement(barButtonItem))
                return $"Native element '{elementId}' is not enabled and actionable";

            return UIKit.UIApplication.SharedApplication.SendAction(
                barButtonItem.Action!,
                barButtonItem.Target,
                barButtonItem,
                null)
                    ? "ok"
                    : $"Native element '{elementId}' action was not handled";
        }

        if (nativeElement is UIKit.UITabBarItem tabBarItem
            && GetNativeElementInfoById(elementId) is
            {
                Role: "shell-tab-overflow",
                Discriminator: "TabBarItem"
            })
        {
            var tabBarController = FindAppleTabBarController(tabBarItem);
            if (tabBarController is null)
                return $"Native More tab '{elementId}' is not attached to a tab bar controller";

            tabBarController.SelectedViewController = tabBarController.MoreNavigationController;
            return "ok";
        }
#elif MACOS
        if (nativeElement is AppKit.NSButton button)
        {
            if (!CanInvokeRegisteredNativeElement(button))
                return $"Native element '{elementId}' is not visible and enabled";

            // PerformClick drives the same target/action machinery AppKit wires up for a
            // physical click, including closing a modal NSAlert with the button's response.
            button.PerformClick(button);
            return "ok";
        }
#endif

        return null;
    }

#if IOS || MACCATALYST
    private static UIKit.UITabBarController? FindAppleTabBarController(
        UIKit.UITabBarItem tabBarItem)
    {
        foreach (var scene in UIKit.UIApplication.SharedApplication.ConnectedScenes)
        {
            if (scene is not UIKit.UIWindowScene windowScene)
                continue;

            foreach (var window in windowScene.Windows)
            {
                if (FindAppleTabBarController(window.RootViewController, tabBarItem)
                    is { } controller)
                {
                    return controller;
                }
            }
        }

        return null;
    }

    private static UIKit.UITabBarController? FindAppleTabBarController(
        UIKit.UIViewController? controller,
        UIKit.UITabBarItem tabBarItem)
    {
        if (controller is null)
            return null;

        if (controller is UIKit.UITabBarController tabBarController
            && tabBarController.TabBar.Items?.Any(item => ReferenceEquals(item, tabBarItem)) == true)
        {
            return tabBarController;
        }

        if (FindAppleTabBarController(controller.PresentedViewController, tabBarItem)
            is { } presentedController)
        {
            return presentedController;
        }

        foreach (var child in controller.ChildViewControllers)
        {
            if (FindAppleTabBarController(child, tabBarItem) is { } childController)
                return childController;
        }

        return null;
    }
#endif
#endif

#if ANDROID || IOS || MACCATALYST || MACOS
    public override string TryNativeElementFocus(string elementId)
    {
        if (!elementId.StartsWith("native:registered:", StringComparison.Ordinal))
            return base.TryNativeElementFocus(elementId);

        var nativeElement = GetNativeElementById(elementId);
#if ANDROID
        if (nativeElement is global::Android.Views.View androidView)
            return androidView.RequestFocus()
                ? "ok"
                : $"Native element '{elementId}' could not be focused";
#elif IOS || MACCATALYST
        if (nativeElement is UIKit.UIView uiView)
            return uiView.BecomeFirstResponder()
                ? "ok"
                : $"Native element '{elementId}' could not be focused";
#elif MACOS
        if (nativeElement is AppKit.NSSearchToolbarItem searchToolbarItem)
            return searchToolbarItem.SearchField.Window?.MakeFirstResponder(searchToolbarItem.SearchField) == true
                ? "ok"
                : $"Native element '{elementId}' could not be focused";
        if (nativeElement is AppKit.NSView nsView)
            return nsView.Window?.MakeFirstResponder(nsView) == true
                ? "ok"
                : $"Native element '{elementId}' could not be focused";
#endif

        return $"Native element '{elementId}' was not found";
    }
#endif

#if MACOS
        public override string TryNativeElementTap(string elementId)
        {
            if (!elementId.StartsWith("native:registered:", StringComparison.Ordinal))
                return base.TryNativeElementTap(elementId);

            var baseResult = base.TryNativeElementTap(elementId);
            if (baseResult == "ok")
                return baseResult;

            var nativeElement = GetNativeElementById(elementId);
            if (nativeElement is AppKit.NSSearchToolbarItem searchToolbarItem)
                return TryNativeElementFocus(elementId);

            if (nativeElement is AppKit.NSToolbarItem toolbarItem
                && toolbarItem.Action is not null)
            {
                var dispatched = AppKit.NSApplication.SharedApplication.SendAction(
                    toolbarItem.Action,
                    toolbarItem.Target,
                    toolbarItem);

                return ToolbarItemInvocationOutcome.IsSuccessful(
                    toolbarItem.Enabled,
                    toolbarItem.Target is not null,
                    dispatched)
                        ? "ok"
                        : $"Native element '{elementId}' action was not handled";
            }

            return baseResult;
        }

        public override string TryNativeElementSetValue(string elementId, string value)
        {
            if (elementId.StartsWith("native:registered:", StringComparison.Ordinal)
                && GetNativeElementById(elementId) is AppKit.NSSearchToolbarItem searchToolbarItem)
            {
                searchToolbarItem.SearchField.StringValue = value;
                if (GetRegisteredNativeOwner(elementId) is MacOSSearchToolbarItem searchOwner)
                    searchOwner.Text = value;
                else if (GetRegisteredNativeOwner(elementId) is Page ownerPage
                    && MacOSToolbar.GetSearchItem(ownerPage) is { } pageSearchOwner)
                {
                    pageSearchOwner.Text = value;
                }
                return "ok";
            }

            return base.TryNativeElementSetValue(elementId, value);
        }
#endif

#if WINDOWS
    private readonly NativeWindowProbe _nativeProbe = new();
    private readonly object _nativeObjectsLock = new();
    private Dictionary<string, object> _nativeObjects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object> _nativeHitObjects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ElementInfo> _nativeHitInfos = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxNativeHitCacheSize = 256;

    internal override bool AreElementIdentitiesEqual(object first, object second)
        => NativeWindowProbe.SameIdentity(first, second);

    internal override object GetElementIdentity(object element)
        => element is System.Windows.Automation.AutomationElement automationElement
            ? NativeWindowProbe.GetStableIdentity(automationElement)
            : base.GetElementIdentity(element);

    internal override bool ShouldRetainElementIdentityStrongly(object identity)
        => NativeWindowProbe.IsDurableIdentity(identity)
            || base.ShouldRetainElementIdentityStrongly(identity);
#endif

    protected override void PopulateNativeInfo(ElementInfo info, VisualElement ve)
    {
        try
        {
            var platformView = ve.Handler?.PlatformView;
            if (platformView == null) return;

            info.NativeType = platformView.GetType().FullName;

#if IOS || MACCATALYST
            if (platformView is UIKit.UIView uiView)
            {
                var props = new Dictionary<string, string?>();
                if (!string.IsNullOrEmpty(uiView.AccessibilityIdentifier))
                    props["accessibilityIdentifier"] = uiView.AccessibilityIdentifier;
                if (!string.IsNullOrEmpty(uiView.AccessibilityLabel))
                    props["accessibilityLabel"] = uiView.AccessibilityLabel;
                if (uiView is UIKit.UIControl uiControl)
                    props["isUIControl"] = "true";
                if (uiView is UIKit.UITextField textField)
                    props["isSecureTextEntry"] = textField.SecureTextEntry.ToString();
                if (props.Count > 0)
                    info.NativeProperties = props;
            }
#elif ANDROID
            if (platformView is global::Android.Views.View androidView)
            {
                var props = new Dictionary<string, string?>();
                if (!string.IsNullOrEmpty(androidView.ContentDescription))
                    props["contentDescription"] = androidView.ContentDescription;
                if (androidView is global::Android.Widget.EditText editText)
                    props["inputType"] = editText.InputType.ToString();
                if (androidView.Clickable)
                    props["clickable"] = "true";
                if (props.Count > 0)
                    info.NativeProperties = props;
            }
#elif MACOS
            if (platformView is NSView nsView)
            {
                var props = new Dictionary<string, string?>();
                if (!string.IsNullOrEmpty(nsView.AccessibilityIdentifier))
                    props["accessibilityIdentifier"] = nsView.AccessibilityIdentifier;
                if (!string.IsNullOrEmpty(nsView.AccessibilityLabel))
                    props["accessibilityLabel"] = nsView.AccessibilityLabel;
                if (nsView is NSControl nsControl)
                {
                    props["isNSControl"] = "true";
                    props["isEnabled"] = nsControl.Enabled.ToString();
                }
                if (nsView is NSButton nsButton)
                    props["buttonTitle"] = nsButton.Title;
                if (nsView is NSTextField nsTextField)
                {
                    var isPassword = ve is Entry { IsPassword: true }
                        || nsTextField is NSSecureTextField;
                    props["stringValue"] = SensitiveValueRedactor.Redact(
                        nsTextField.StringValue,
                        isPassword);
                    props["isEditable"] = nsTextField.Editable.ToString();
                    props["isPassword"] = isPassword.ToString();
                }
                props["isHidden"] = nsView.Hidden.ToString();
                props["alphaValue"] = nsView.AlphaValue.ToString("F2");
                if (props.Count > 0)
                    info.NativeProperties = props;
            }
#elif WINDOWS
            if (platformView is Microsoft.UI.Xaml.FrameworkElement frameworkElement)
            {
                var props = new Dictionary<string, string?>();
                var automationId = Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(frameworkElement);
                if (!string.IsNullOrEmpty(automationId))
                    props["automationId"] = automationId;
                var automationName = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(frameworkElement);
                if (!string.IsNullOrEmpty(automationName))
                    props["automationName"] = automationName;
                var helpText = Microsoft.UI.Xaml.Automation.AutomationProperties.GetHelpText(frameworkElement);
                if (!string.IsNullOrEmpty(helpText))
                    props["helpText"] = helpText;
                if (!string.IsNullOrEmpty(frameworkElement.Name))
                    props["name"] = frameworkElement.Name;
                if (frameworkElement.Visibility != Microsoft.UI.Xaml.Visibility.Visible)
                    props["visibility"] = "collapsed";
                if (!frameworkElement.IsHitTestVisible)
                    props["isHitTestVisible"] = "false";
                if (frameworkElement is Microsoft.UI.Xaml.Controls.Control control)
                {
                    if (!control.IsEnabled)
                        props["isEnabled"] = "false";
                    if (!control.IsTabStop)
                        props["isTabStop"] = "false";
                }
                if (frameworkElement is Microsoft.UI.Xaml.Controls.TextBox textBox)
                {
                    if (textBox.IsReadOnly)
                        props["isReadOnly"] = "true";
                }
                if (frameworkElement is Microsoft.UI.Xaml.Controls.PasswordBox)
                    props["isPassword"] = "true";
                if (props.Count > 0)
                    info.NativeProperties = props;
            }
#endif
        }
        catch
        {
            // Native info is best-effort; don't fail the tree walk
        }
    }

    protected override BoundsInfo? ResolveSyntheticBounds(object marker)
    {
        try
        {
#if IOS || MACCATALYST
            return ResolveBoundsApple(marker);
#elif ANDROID
            return ResolveBoundsAndroid(marker);
#elif WINDOWS
            return ResolveBoundsWindows(marker);
#else
            return null;
#endif
        }
        catch { return null; }
    }

    protected override void PopulateSyntheticNativeInfo(ElementInfo info, object marker)
    {
        try
        {
#if IOS || MACCATALYST
            PopulateNativeInfoApple(info, marker);
#elif ANDROID
            PopulateNativeInfoAndroid(info, marker);
#elif WINDOWS
            PopulateNativeInfoWindows(info, marker);
#endif
        }
        catch { }
    }

    protected override BoundsInfo? ResolveWindowBounds(VisualElement ve)
    {
        try
        {
            var platformView = ve.Handler?.PlatformView;
            if (platformView == null) return null;

#if IOS || MACCATALYST
            if (platformView is UIKit.UIView uiView && uiView.Window != null)
            {
                var windowRect = uiView.ConvertRectToView(uiView.Bounds, uiView.Window.RootViewController?.View ?? uiView.Window);
                return new BoundsInfo
                {
                    X = windowRect.X,
                    Y = windowRect.Y,
                    Width = windowRect.Width,
                    Height = windowRect.Height
                };
            }
#elif ANDROID
            if (platformView is global::Android.Views.View androidView)
            {
                var location = new int[2];
                androidView.GetLocationInWindow(location);
                var density = androidView.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
                return new BoundsInfo
                {
                    X = location[0] / density,
                    Y = location[1] / density,
                    Width = androidView.Width / density,
                    Height = androidView.Height / density
                };
            }
#elif WINDOWS
            if (platformView is Microsoft.UI.Xaml.UIElement uiElement)
            {
                var transform = uiElement.TransformToVisual(null);
                var point = transform.TransformPoint(new global::Windows.Foundation.Point(0, 0));
                if (uiElement is Microsoft.UI.Xaml.FrameworkElement fe)
                {
                    return new BoundsInfo
                    {
                        X = point.X,
                        Y = point.Y,
                        Width = fe.ActualWidth,
                        Height = fe.ActualHeight
                    };
                }
            }
#elif MACOS
            if (platformView is AppKit.NSView nsView && nsView.Window?.ContentView != null)
            {
                var windowRect = nsView.ConvertRectToView(nsView.Bounds, nsView.Window.ContentView);
                // NSView uses bottom-left origin; convert to top-left
                var contentHeight = nsView.Window.ContentView.Bounds.Height;
                return new BoundsInfo
                {
                    X = windowRect.X,
                    Y = contentHeight - windowRect.Y - windowRect.Height,
                    Width = windowRect.Width,
                    Height = windowRect.Height
                };
            }
#endif
            return null;
        }
        catch { return null; }
    }

#if IOS || MACCATALYST
    private BoundsInfo? ResolveBoundsApple(object marker)
    {
        Shell? shell = marker switch
        {
            FlyoutButtonMarker m => m.Shell,
            ShellFlyoutItemMarker m => m.Shell,
            ShellTabMarker m => m.Shell,
            NavBarTitleMarker => Shell.Current,
            SearchHandlerMarker => Shell.Current,
            ToolbarItem => Shell.Current,
            _ => null
        };

        if (shell?.Handler?.PlatformView is not UIView shellView)
            return null;

        // Find UINavigationBar for nav bar elements
        if (marker is NavBarTitleMarker or FlyoutButtonMarker or SearchHandlerMarker or ToolbarItem)
        {
            var navBar = FindSubview<UINavigationBar>(shellView);
            if (navBar != null)
            {
                if (marker is ToolbarItem ti)
                {
                    // Find the button matching this toolbar item in the nav bar
                    var button = FindToolbarButton(navBar, ti, shellView);
                    if (button != null) return button;
                }

                var frame = navBar.ConvertRectToView(navBar.Bounds, shellView);
                if (marker is FlyoutButtonMarker)
                {
                    // Flyout button is in the left area of the nav bar
                    return new BoundsInfo
                    {
                        X = frame.X,
                        Y = frame.Y,
                        Width = 44,
                        Height = frame.Height
                    };
                }
                return new BoundsInfo
                {
                    X = frame.X,
                    Y = frame.Y,
                    Width = frame.Width,
                    Height = frame.Height
                };
            }
        }

        // Find UITabBar for tab elements
        if (marker is ShellTabMarker)
        {
            var tabBar = FindSubview<UITabBar>(shellView);
            if (tabBar != null)
            {
                var frame = tabBar.ConvertRectToView(tabBar.Bounds, shellView);
                return new BoundsInfo
                {
                    X = frame.X,
                    Y = frame.Y,
                    Width = frame.Width,
                    Height = frame.Height
                };
            }
        }

        return null;
    }

    private static BoundsInfo? FindToolbarButton(UINavigationBar navBar, ToolbarItem ti, UIView rootView)
    {
        // Search for any interactive view in the nav bar matching the toolbar item
        var match = FindMatchingView(navBar, ti);
        if (match != null)
        {
            var frame = match.ConvertRectToView(match.Bounds, rootView);
            return new BoundsInfo
            {
                X = frame.X,
                Y = frame.Y,
                Width = frame.Width,
                Height = frame.Height
            };
        }
        return null;
    }

    private static UIView? FindMatchingView(UIView root, ToolbarItem ti)
    {
        // Check this view's accessibility label/identifier against the toolbar item
        var accessLabel = root.AccessibilityLabel;
        var accessId = root.AccessibilityIdentifier;
        var title = (root as UIButton)?.CurrentTitle;

        if ((!string.IsNullOrEmpty(ti.Text) && (title == ti.Text || accessLabel == ti.Text))
            || (!string.IsNullOrEmpty(ti.AutomationId) && accessId == ti.AutomationId))
        {
            // Prefer interactive leaf views — only match if clickable or if no subviews
            if (root.UserInteractionEnabled && root.Bounds.Width > 0 && root.Bounds.Height > 0)
                return root;
        }

        // Recurse into subviews, preferring deeper (more specific) matches
        foreach (var sub in root.Subviews)
        {
            var found = FindMatchingView(sub, ti);
            if (found != null) return found;
        }

        return null;
    }

    private static T? FindSubview<T>(UIView root) where T : UIView
    {
        if (root is T match) return match;
        foreach (var sub in root.Subviews)
        {
            var found = FindSubview<T>(sub);
            if (found != null) return found;
        }
        return null;
    }

    private void PopulateNativeInfoApple(ElementInfo info, object marker)
    {
        Shell? shell = marker switch
        {
            FlyoutButtonMarker m => m.Shell,
            ShellFlyoutItemMarker m => m.Shell,
            ShellTabMarker m => m.Shell,
            NavBarTitleMarker => Shell.Current,
            _ => null
        };

        if (shell?.Handler?.PlatformView is UIView shellView)
        {
            if (marker is NavBarTitleMarker or FlyoutButtonMarker)
            {
                var navBar = FindSubview<UINavigationBar>(shellView);
                if (navBar != null) info.NativeType = navBar.GetType().FullName;
            }
            else if (marker is ShellTabMarker)
            {
                var tabBar = FindSubview<UITabBar>(shellView);
                if (tabBar != null) info.NativeType = tabBar.GetType().FullName;
            }
        }
    }
#endif

#if ANDROID
    private BoundsInfo? ResolveBoundsAndroid(object marker)
    {
        Shell? shell = marker switch
        {
            FlyoutButtonMarker m => m.Shell,
            ShellFlyoutItemMarker m => m.Shell,
            ShellTabMarker m => m.Shell,
            NavBarTitleMarker => Shell.Current,
            ToolbarItem => Shell.Current,
            _ => null
        };

        if (shell?.Handler?.PlatformView is not global::Android.Views.View shellView)
            return null;

        var density = shellView.Context?.Resources?.DisplayMetrics?.Density ?? 1f;

        if (marker is NavBarTitleMarker or FlyoutButtonMarker or ToolbarItem)
        {
            var toolbar = FindAndroidView<global::AndroidX.AppCompat.Widget.Toolbar>(shellView);
            if (toolbar != null)
            {
                // For ToolbarItem, try to find the specific action view
                if (marker is ToolbarItem ti)
                {
                    var actionView = FindAndroidToolbarButton(toolbar, ti);
                    if (actionView != null)
                    {
                        var loc = new int[2];
                        actionView.GetLocationInWindow(loc);
                        return new BoundsInfo
                        {
                            X = loc[0] / density,
                            Y = loc[1] / density,
                            Width = actionView.Width / density,
                            Height = actionView.Height / density
                        };
                    }
                }

                // For FlyoutButton, find the navigation ImageButton
                if (marker is FlyoutButtonMarker)
                {
                    var navButton = FindAndroidNavigationButton(toolbar);
                    if (navButton != null)
                    {
                        var loc = new int[2];
                        navButton.GetLocationInWindow(loc);
                        return new BoundsInfo
                        {
                            X = loc[0] / density,
                            Y = loc[1] / density,
                            Width = navButton.Width / density,
                            Height = navButton.Height / density
                        };
                    }
                }

                var location = new int[2];
                toolbar.GetLocationOnScreen(location);
                var shellLocation = new int[2];
                shellView.GetLocationOnScreen(shellLocation);

                return new BoundsInfo
                {
                    X = (location[0] - shellLocation[0]) / density,
                    Y = (location[1] - shellLocation[1]) / density,
                    Width = toolbar.Width / density,
                    Height = toolbar.Height / density
                };
            }
        }

        if (marker is ShellTabMarker)
        {
            var bottomNav = FindAndroidView<Google.Android.Material.BottomNavigation.BottomNavigationView>(shellView);
            if (bottomNav != null)
            {
                var location = new int[2];
                bottomNav.GetLocationOnScreen(location);
                var shellLocation = new int[2];
                shellView.GetLocationOnScreen(shellLocation);

                return new BoundsInfo
                {
                    X = (location[0] - shellLocation[0]) / density,
                    Y = (location[1] - shellLocation[1]) / density,
                    Width = bottomNav.Width / density,
                    Height = bottomNav.Height / density
                };
            }
        }

        return null;
    }

    private static T? FindAndroidView<T>(global::Android.Views.View root) where T : global::Android.Views.View
    {
        if (root is T match) return match;
        if (root is global::Android.Views.ViewGroup vg)
        {
            for (int i = 0; i < vg.ChildCount; i++)
            {
                var child = vg.GetChildAt(i);
                if (child != null)
                {
                    var found = FindAndroidView<T>(child);
                    if (found != null) return found;
                }
            }
        }
        return null;
    }

    private static global::Android.Views.View? FindAndroidToolbarButton(global::AndroidX.AppCompat.Widget.Toolbar toolbar, ToolbarItem ti)
    {
        // Search toolbar's descendants recursively — action buttons are nested
        // inside ActionMenuView/LinearLayoutCompat, not direct children.
        // ContentDescription may be set to AutomationId or Text, so check both.
        return FindToolbarButtonRecursive(toolbar, ti);

        static global::Android.Views.View? FindToolbarButtonRecursive(global::Android.Views.ViewGroup parent, ToolbarItem ti)
        {
            for (int i = 0; i < parent.ChildCount; i++)
            {
                var child = parent.GetChildAt(i);
                if (child == null) continue;

                var desc = child.ContentDescription;
                if (!string.IsNullOrEmpty(desc))
                {
                    if (desc == ti.Text || desc == ti.AutomationId)
                        return child;
                }
                if (child is global::Android.Widget.TextView tv && tv.Text == ti.Text)
                    return child;

                if (child is global::Android.Views.ViewGroup vg)
                {
                    var found = FindToolbarButtonRecursive(vg, ti);
                    if (found != null) return found;
                }
            }
            return null;
        }
    }

    private static global::Android.Views.View? FindAndroidNavigationButton(global::AndroidX.AppCompat.Widget.Toolbar toolbar)
    {
        // The navigation/hamburger button is an ImageButton direct child of the toolbar
        for (int i = 0; i < toolbar.ChildCount; i++)
        {
            var child = toolbar.GetChildAt(i);
            if (child is global::Android.Widget.ImageButton)
                return child;
        }
        return null;
    }

    private void PopulateNativeInfoAndroid(ElementInfo info, object marker)
    {
        Shell? shell = marker switch
        {
            FlyoutButtonMarker m => m.Shell,
            ShellFlyoutItemMarker m => m.Shell,
            ShellTabMarker m => m.Shell,
            NavBarTitleMarker => Shell.Current,
            _ => null
        };

        if (shell?.Handler?.PlatformView is global::Android.Views.View shellView)
        {
            if (marker is NavBarTitleMarker or FlyoutButtonMarker)
            {
                var toolbar = FindAndroidView<global::AndroidX.AppCompat.Widget.Toolbar>(shellView);
                if (toolbar != null) info.NativeType = toolbar.GetType().FullName ?? toolbar.Class?.Name;
            }
            else if (marker is ShellTabMarker)
            {
                var bottomNav = FindAndroidView<Google.Android.Material.BottomNavigation.BottomNavigationView>(shellView);
                if (bottomNav != null) info.NativeType = bottomNav.GetType().FullName ?? bottomNav.Class?.Name;
            }
        }
    }
#endif

    protected override string? EnsurePlatformStableId(object platformObj)
    {
        try
        {
#if IOS || MACCATALYST
            if (platformObj is UIKit.UIView uiView)
            {
                if (string.IsNullOrEmpty(uiView.AccessibilityIdentifier))
                    uiView.AccessibilityIdentifier = Guid.NewGuid().ToString();
                return uiView.AccessibilityIdentifier;
            }
#elif ANDROID
            if (platformObj is global::Android.Views.View androidView)
            {
                var existing = androidView.ContentDescription;
                if (string.IsNullOrEmpty(existing))
                {
                    existing = Guid.NewGuid().ToString();
                    androidView.ContentDescription = existing;
                }
                return existing;
            }
#elif WINDOWS
            if (platformObj is Microsoft.UI.Xaml.UIElement uiElement)
            {
                var existing = Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(uiElement);
                if (string.IsNullOrEmpty(existing))
                {
                    existing = Guid.NewGuid().ToString();
                    uiElement.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.AutomationIdProperty, existing);
                }
                return existing;
            }
#elif MACOS
            if (platformObj is AppKit.NSView nsView)
            {
                if (string.IsNullOrEmpty(nsView.AccessibilityIdentifier))
                    nsView.AccessibilityIdentifier = Guid.NewGuid().ToString();
                return nsView.AccessibilityIdentifier;
            }
#endif
        }
        catch { }
        return null;
    }

#if WINDOWS
    public override bool SupportsNativeElements => true;

    public override IReadOnlyList<IntPtr> GetKnownNativeWindowHandles(Application app, int? windowIndex = null)
    {
        var handles = new List<IntPtr>();

        if (windowIndex is not null)
        {
            var window = windowIndex.Value >= 0 && windowIndex.Value < app.Windows.Count
                ? app.Windows[windowIndex.Value]
                : null;
            var handle = GetWindowHandle(window);
            if (handle != IntPtr.Zero)
                handles.Add(handle);
            return handles;
        }

        foreach (var window in app.Windows)
        {
            var handle = GetWindowHandle(window);
            if (handle != IntPtr.Zero)
                handles.Add(handle);
        }

        return handles;
    }

    public override List<ElementInfo> WalkNativeTree(IReadOnlyList<IntPtr> knownWindowHandles, int maxDepth = 0)
    {
        var roots = new List<ElementInfo>();
        var nativeObjects = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        _nativeProbe.AppendNativeWindows(roots, nativeObjects, knownWindowHandles, maxDepth);

        lock (_nativeObjectsLock)
        {
            _nativeObjects = nativeObjects;
            _nativeHitObjects.Clear();
            _nativeHitInfos.Clear();
        }

        return roots;
    }

    public override List<ElementInfo> HitTestNativeElements(
        IReadOnlyList<IntPtr> knownWindowHandles,
        double x,
        double y)
    {
        lock (_nativeObjectsLock)
        {
            var hitObjects = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var hits = _nativeProbe.HitTest(hitObjects, knownWindowHandles, x, y);
            foreach (var hit in hits)
            {
                if (hitObjects.TryGetValue(hit.Id, out var nativeObject))
                {
                    if (!_nativeHitObjects.ContainsKey(hit.Id)
                        && _nativeHitObjects.Count >= MaxNativeHitCacheSize)
                    {
                        var expiredId = _nativeHitObjects.Keys.First();
                        _nativeHitObjects.Remove(expiredId);
                        _nativeHitInfos.Remove(expiredId);
                    }

                    _nativeHitObjects[hit.Id] = nativeObject;
                    _nativeHitInfos[hit.Id] = hit;
                }
            }

            return hits;
        }
    }

    public override object? GetNativeElementById(string id)
    {
        if (id.StartsWith("native:registered:", StringComparison.Ordinal))
            return base.GetNativeElementById(id);

        lock (_nativeObjectsLock)
        {
            if (NativeWindowProbe.TryGetAutomationElement(_nativeHitObjects, id) is { } hitElement)
                return hitElement;
            if (NativeWindowProbe.TryGetAutomationElement(_nativeObjects, id) is { } cached)
                return cached;
        }

        if (_nativeProbe.FindByRuntimeId(id) is { } runtimeElement)
            return runtimeElement;
        if (_nativeProbe.FindByHitId(id) is { } recoveredHitElement)
            return recoveredHitElement;

        // Preserve native ID stability on cache miss: re-walk with the same HWND
        // the id was originally produced under. A plain Array.Empty<IntPtr>() walk
        // would skip AppendKnownWindowDialogSubtrees entirely, so ":dialog:{n}"
        // prefixes from a previous tree/query would never be regenerated.
        var seedHwnds = NativeWindowProbe.ExtractHwndsFromId(id);
        WalkNativeTree(seedHwnds);
        lock (_nativeObjectsLock)
            return NativeWindowProbe.TryGetAutomationElement(_nativeObjects, id);
    }

    public override ElementInfo? GetNativeElementInfoById(string id)
    {
        if (id.StartsWith("native:registered:", StringComparison.Ordinal))
            return base.GetNativeElementInfoById(id);

        // Cache-first: avoid a full UIA tree walk (which calls EnumerateProcessTopLevels
        // and enumerates every same-process window) when the requested id was already
        // resolved by a recent tree/query call.
        Dictionary<string, object> cache;
        lock (_nativeObjectsLock)
        {
            if (_nativeHitInfos.TryGetValue(id, out var hitInfo))
                return hitInfo;
            cache = _nativeObjects;
        }

        if (NativeWindowProbe.TryBuildCachedElementInfo(cache, id) is { } cached)
            return cached;

        if (_nativeProbe.FindByRuntimeId(id) is { } runtimeElement)
            return NativeWindowProbe.TryBuildElementInfo(runtimeElement, id);
        if (_nativeProbe.FindByHitId(id) is { } recoveredHitElement)
            return NativeWindowProbe.TryBuildElementInfo(recoveredHitElement, id);

        var seedHwnds = NativeWindowProbe.ExtractHwndsFromId(id);
        return FlattenElementInfos(WalkNativeTree(seedHwnds))
            .FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public override string TryNativeElementTap(string elementId)
    {
        if (elementId.StartsWith("native:registered:", StringComparison.Ordinal))
            return base.TryNativeElementTap(elementId);

        var element = GetNativeAutomationElement(elementId);
        if (element is null)
            return $"Native element '{elementId}' was not found";

        return NativeWindowProbe.TryInvoke(element)
            ? "ok"
            : $"Native element '{elementId}' does not support invoke, toggle, selection, or expand/collapse";
    }

    protected internal override string TryNativeElementTap(string elementId, object nativeElement)
    {
        if (elementId.StartsWith("native:registered:", StringComparison.Ordinal))
            return base.TryNativeElementTap(elementId, nativeElement);

        return nativeElement is System.Windows.Automation.AutomationElement element
            && NativeWindowProbe.TryInvoke(element)
                ? "ok"
                : $"Native element '{elementId}' does not support invoke, toggle, selection, or expand/collapse";
    }

    public override string TryNativeElementSetValue(string elementId, string value)
    {
        if (elementId.StartsWith("native:registered:", StringComparison.Ordinal))
            return base.TryNativeElementSetValue(elementId, value);

        var element = GetNativeAutomationElement(elementId);
        if (element is null)
            return $"Native element '{elementId}' was not found";

        return NativeWindowProbe.TrySetValue(element, value)
            ? "ok"
            : $"Native element '{elementId}' does not support writable value";
    }

    protected internal override string TryNativeElementSetValue(
        string elementId,
        object nativeElement,
        string value)
    {
        if (elementId.StartsWith("native:registered:", StringComparison.Ordinal))
            return base.TryNativeElementSetValue(elementId, nativeElement, value);

        return nativeElement is System.Windows.Automation.AutomationElement element
            && NativeWindowProbe.TrySetValue(element, value)
                ? "ok"
                : $"Native element '{elementId}' does not support writable value";
    }

    public override string TryNativeElementFocus(string elementId)
    {
        if (elementId.StartsWith("native:registered:", StringComparison.Ordinal))
        {
            return GetNativeElementById(elementId) is Microsoft.UI.Xaml.Controls.Control control
                && control.Focus(Microsoft.UI.Xaml.FocusState.Programmatic)
                    ? "ok"
                    : $"Native element '{elementId}' could not be focused";
        }

        var element = GetNativeAutomationElement(elementId);
        if (element is null)
            return $"Native element '{elementId}' was not found";

        return NativeWindowProbe.TryFocus(element)
            ? "ok"
            : $"Native element '{elementId}' could not be focused";
    }

    protected internal override string TryNativeElementFocus(string elementId, object nativeElement)
    {
        if (elementId.StartsWith("native:registered:", StringComparison.Ordinal))
        {
            return nativeElement is Microsoft.UI.Xaml.Controls.Control control
                && control.Focus(Microsoft.UI.Xaml.FocusState.Programmatic)
                    ? "ok"
                    : $"Native element '{elementId}' could not be focused";
        }

        return nativeElement is System.Windows.Automation.AutomationElement element
            && NativeWindowProbe.TryFocus(element)
                ? "ok"
                : $"Native element '{elementId}' could not be focused";
    }

    public override string TryNativeElementScroll(string elementId, double deltaX, double deltaY)
    {
        if (elementId.StartsWith("native:registered:", StringComparison.Ordinal))
        {
            if (GetNativeElementById(elementId) is Microsoft.UI.Xaml.UIElement uiElement)
            {
                uiElement.StartBringIntoView();
                return "ok";
            }

            return $"Native element '{elementId}' was not found";
        }

        var element = GetNativeAutomationElement(elementId);
        if (element is null)
            return $"Native element '{elementId}' was not found";

        return NativeWindowProbe.TryScroll(element, deltaX, deltaY)
            ? "ok"
            : $"Native element '{elementId}' does not support scrolling";
    }

    protected internal override string TryNativeElementScroll(
        string elementId,
        object nativeElement,
        double deltaX,
        double deltaY)
    {
        if (elementId.StartsWith("native:registered:", StringComparison.Ordinal))
        {
            if (nativeElement is Microsoft.UI.Xaml.UIElement uiElement)
            {
                uiElement.StartBringIntoView();
                return "ok";
            }

            return $"Native element '{elementId}' was not found";
        }

        return nativeElement is System.Windows.Automation.AutomationElement element
            && NativeWindowProbe.TryScroll(element, deltaX, deltaY)
                ? "ok"
                : $"Native element '{elementId}' does not support scrolling";
    }

    private System.Windows.Automation.AutomationElement? GetNativeAutomationElement(string id)
        => GetNativeElementById(id) as System.Windows.Automation.AutomationElement;

    private static IntPtr GetWindowHandle(Microsoft.Maui.Controls.Window? window)
    {
        if (window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
            return WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);

        return IntPtr.Zero;
    }

    private BoundsInfo? ResolveBoundsWindows(object marker)
    {
        // Windows NavigationView doesn't expose easily queryable sub-parts
        // for nav bar / tab regions. Return null for now — can be enhanced later.
        return null;
    }

    private void PopulateNativeInfoWindows(ElementInfo info, object marker)
    {
        Shell? shell = marker switch
        {
            FlyoutButtonMarker m => m.Shell,
            ShellFlyoutItemMarker m => m.Shell,
            ShellTabMarker m => m.Shell,
            NavBarTitleMarker => Shell.Current,
            _ => null
        };

        if (shell?.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement fe)
        {
            info.NativeType = fe.GetType().FullName;
        }
    }
#endif
}
