# .NET MAUI Browser (WebAssembly) Essentials

MAUI Essentials implementations for the browser — run MAUI Essentials APIs in Blazor WebAssembly and plain wasm browser apps using standard web APIs, with no native platform.

> ⚠️ **This package is experimental.** APIs may change between releases. It is not covered by the [.NET MAUI Support Policy](https://dotnet.microsoft.com/platform/support/policy/maui) and is provided as-is.

## Packages

| Package | Description |
|---------|-------------|
| `Microsoft.Maui.Platforms.Browser.Essentials` | Essentials APIs for the browser (WebAssembly) |

## How it works

The library targets plain `net10.0` and talks to the browser through `[JSImport]` interop (`System.Runtime.InteropServices.JavaScript`) with a self-contained ES module that is embedded in the assembly and imported at startup via a `data:` URL — no Blazor dependency, no static-asset plumbing. Apps register the implementations against the standard MAUI Essentials interfaces (`IPreferences`, `ISecureStorage`, `IClipboard`, …) with `IServiceCollection.AddBrowserEssentials()` and resolve them via DI.

```csharp
using Microsoft.Maui.Platforms.Browser.Essentials;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddBrowserEssentials();

// Import the JS interop module before the first Essentials call.
await BrowserEssentials.InitializeAsync();

await builder.Build().RunAsync();
```

See the [package README](src/Browser.Essentials/README.md) for the full API support matrix and security notes.

## Highlights

- **Preferences** — `localStorage`, with shared-name containers
- **SecureStorage** — AES-GCM-256 via WebCrypto; the non-extractable key lives in IndexedDB
- **Clipboard, Share, Launcher, Browser** — async Clipboard API, Web Share (incl. Level 2 file sharing), `window.open` and `mailto:`/`tel:`/`sms:` protocol handlers
- **Device APIs** — device/display/app info, connectivity, battery, vibration, haptics, wake lock (`KeepScreenOn`)
- **Geolocation & sensors** — `navigator.geolocation`, accelerometer/gyroscope/orientation/compass from devicemotion/deviceorientation events
- **Files** — file picker via `<input type="file">`, app package files over `fetch`, in-memory VFS for app data
- **Accessibility** — `ISemanticScreenReader` announcements through an `aria-live` region

APIs with no web equivalent throw `FeatureNotSupportedException`.

## Repository layout

```
platforms/Browser/
├── src/Browser.Essentials/            # The shipping library (+ embedded JS interop module)
├── samples/Browser.Essentials.Sample.Blazor/  # Blazor WebAssembly demo app
├── tests/Browser.Essentials.TestApp/  # Blazor WASM app hosting the in-browser test suite
├── tests/Browser.Essentials.Tests/    # xunit + Playwright driver for the test suite
└── Browser.slnx
```

## Building

```bash
dotnet build platforms/Browser/Browser.slnx
```

## Running the sample

```bash
dotnet run --project platforms/Browser/samples/Browser.Essentials.Sample.Blazor
```

## Testing

The test suite runs **inside the browser**: `Browser.Essentials.Tests` publishes the `Browser.Essentials.TestApp` Blazor app, serves it from Kestrel, loads it in headless Chromium via Playwright, and asserts the results of the in-browser tests (which exercise real `localStorage`, WebCrypto, clipboard, geolocation, and `fetch`).

```bash
dotnet test platforms/Browser/tests/Browser.Essentials.Tests
```

Set `BROWSER_ESSENTIALS_SKIP_TESTS=1` to skip in environments where the Playwright Chromium download is unavailable.
