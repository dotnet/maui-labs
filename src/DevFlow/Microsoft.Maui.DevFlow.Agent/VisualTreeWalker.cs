using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
#if IOS || MACCATALYST
using UIKit;
#endif
#if MACOS
using AppKit;
#endif

namespace Microsoft.Maui.DevFlow.Agent;

/// <summary>
/// Platform-specific visual tree walker that provides native view info
/// for Android, iOS, Mac Catalyst, Windows, and macOS AppKit.
/// </summary>
public class PlatformVisualTreeWalker : VisualTreeWalker
{
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
                    props["stringValue"] = nsTextField.StringValue;
                    props["isEditable"] = nsTextField.Editable.ToString();
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
