# Theming

Light and dark mode support with automatic theme change detection.

## Setting the App Theme

Set the application theme programmatically:

```csharp
Application.Current.UserAppTheme = AppTheme.Dark;   // Force dark
Application.Current.UserAppTheme = AppTheme.Light;   // Force light
Application.Current.UserAppTheme = AppTheme.Unspecified; // Follow system
```

The platform maps MAUI themes to macOS appearances:

| `AppTheme` | `NSAppearance` |
|------------|----------------|
| `Light` | `NSAppearance.NameAqua` |
| `Dark` | `NSAppearance.NameDarkAqua` |
| `Unspecified` | System setting (follows macOS Appearance preference) |

## Responding to Theme Changes

When macOS switches between light and dark mode (via System Settings or auto-schedule), the platform automatically detects the change and fires `Application.RequestedThemeChanged`:

```csharp
Application.Current.RequestedThemeChanged += (s, e) =>
{
    Console.WriteLine($"Theme changed to: {e.RequestedTheme}");
};
```

### AppThemeBinding in XAML

Use `AppThemeBinding` for theme-aware resources (works the same as iOS/Android):

```xml
<Label Text="Hello"
       TextColor="{AppThemeBinding Light=Black, Dark=White}"
       BackgroundColor="{AppThemeBinding Light=White, Dark=#1a1a1a}" />
```

### AppThemeBinding in C#

```csharp
var label = new Label { Text = "Hello" };
label.SetAppThemeColor(Label.TextColorProperty, Colors.Black, Colors.DarkGray);
label.SetAppThemeColor(Label.BackgroundColorProperty, Colors.White, Color.FromHex("#1a1a1a"));
```

## How It Works

The platform uses `FlippedNSView.ViewDidChangeEffectiveAppearance()` to detect macOS appearance changes. When the effective appearance changes, it reads the new `NSAppearance.Name` and maps it back to `AppTheme`, then calls `Application.ThemeChanged()` to notify all MAUI bindings.

The sidebar (`NSVisualEffectView`) automatically adapts to theme changes — the behind-window vibrancy material responds to light/dark mode without any additional code.
