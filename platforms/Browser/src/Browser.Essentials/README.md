# Microsoft.Maui.Platforms.Browser.Essentials

MAUI Essentials implementations for the browser (WebAssembly) — preferences, clipboard, geolocation, sensors, and more, built on standard web APIs via `[JSImport]` interop. Works in Blazor WebAssembly apps and plain `wasmbrowser` apps with no Blazor dependency.

> ⚠️ **This package is experimental.** APIs may change between releases. It is not covered by the [.NET MAUI Support Policy](https://dotnet.microsoft.com/platform/support/policy/maui) and is provided as-is.

## Quick start (Blazor WebAssembly)

```csharp
using Microsoft.Maui.Platforms.Browser.Essentials;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddBrowserEssentials();

// Load the JS interop module before the first Essentials call.
await BrowserEssentials.InitializeAsync();

await builder.Build().RunAsync();
```

Then inject any Essentials interface:

```razor
@inject Microsoft.Maui.Storage.IPreferences Preferences

@code {
    void Save() => Preferences.Set("counter", 42);
}
```

## What's implemented

| API | Backing web API | Notes |
|-----|-----------------|-------|
| `IPreferences` | `localStorage` | Persistent per origin |
| `ISecureStorage` | — | **Not supported**; see below |
| `IClipboard` | Async Clipboard API | Reads require a user permission |
| `IConnectivity` | `navigator.onLine` + Network Information | Profile detail is Chromium-only |
| `IDeviceInfo` | `navigator.userAgentData` / UA string | Platform is `Browser` |
| `IDeviceDisplay` | `window.screen`, Wake Lock API | `KeepScreenOn` needs a secure context |
| `IAppInfo` | document title/origin, entry assembly | Theme from `prefers-color-scheme` |
| `IGeolocation` | `navigator.geolocation` | Browser prompts for permission |
| `IShare` | Web Share API (Level 2 for files) | Needs secure context + user gesture |
| `IBrowser` / `ILauncher` | `window.open`, protocol handlers | `mailto:`, `tel:`, `sms:` supported |
| `IBattery` | Battery Status API | Chromium-only; `Unknown` elsewhere |
| `IVibration` / `IHapticFeedback` | `navigator.vibrate` | Mobile browsers |
| `IFilePicker` | `<input type="file">` | File types filter is ignored |
| `ITextToSpeech` | `speechSynthesis` | `GetLocalesAsync` returns empty |
| `IFileSystem` | wasm in-memory VFS + `fetch` | App data does **not** persist; package files load from the base URL |
| `IAccelerometer` / `IGyroscope` / `IOrientationSensor` / `ICompass` | devicemotion / deviceorientation | iOS Safari requires a user-gesture permission prompt |
| `IEmail` / `IPhoneDialer` / `ISms` | `mailto:` / `tel:` / `sms:` links | No attachments / HTML body |
| `ISemanticScreenReader` | `aria-live` region | |
| `IVersionTracking` | preferences | |

Everything else (`IContacts`, `IMap`, `IMediaPicker`, `IScreenshot`, `IFlashlight`, `IBarometer`, `IMagnetometer`, `IAppActions`, `IGeocoding`) throws `FeatureNotSupportedException`.

## `ISecureStorage` is not supported

Browsers have no OS keychain, and an encryption key stored in the same origin as the
encrypted data does not protect it. `BrowserSecureStorage.GetAsync`, `SetAsync`, `Remove`,
and `RemoveAll` throw `PlatformNotSupportedException` immediately. The public type and the
service registration remain available.

### Use your own `ISecureStorage` implementation

An app can supply its own `Microsoft.Maui.Storage.ISecureStorage` implementation. Register it
before calling `AddBrowserEssentials()`:

```csharp
using Microsoft.Maui.Platforms.Browser.Essentials;
using Microsoft.Maui.Storage;

// MySecureStorage is an ISecureStorage implementation supplied by your app.
builder.Services.AddSingleton<ISecureStorage, MySecureStorage>();
builder.Services.AddBrowserEssentials();
```

`AddBrowserEssentials()` keeps an existing `ISecureStorage` registration. A browser app cannot
keep a secret from code that runs on its own origin, so keep secrets on a server (for example,
behind an HTTP-only cookie) and do not store them in plain text in browser storage.

## Content-Security-Policy

By default the interop module is imported through a `data:` URL. If your app's CSP disallows `data:` script sources, copy `BrowserEssentials.js` into your static assets and pass its URL: `await BrowserEssentials.InitializeAsync("./js/BrowserEssentials.js")`.

## Requirements

- .NET 10
- A WebAssembly host (Blazor WebAssembly or `wasmbrowser`)
- Secure context (HTTPS or localhost) for clipboard, share, and wake lock APIs
