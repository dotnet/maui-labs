# Community desktop backends on .NET 11

The Linux GTK4, native macOS AppKit, and Windows WPF backends have experimental
.NET 11 targets that consume the public community-platform contracts in .NET
MAUI 11 RC2. The packages retain their .NET 10 compatibility targets. This is
an implementation baseline, not a native-support certification: Linux,
Windows, and Xcode 27 CI must still execute the target-specific gates described
below.

## Foundation

| Component | Version |
|-----------|---------|
| .NET SDK | `11.0.100-rc.2.26504.105` |
| .NET MAUI packages | `11.0.0-rc.2.26505.5` |
| ASP.NET Core WebView packages | `11.0.0-rc.2.26504.105` |

There is no published workload set named `11.0.100-rc.2.26513.2` on the
configured dnceng feeds. The SDK bundle also contains a mixed preview-6/RC2
manifest set, which cannot compile the Xcode 27 lanes. CI therefore installs
the verified loose manifests in `eng/workloads/dotnet11-rc2.json` with
`--from-rollback-file`. The packages resolve from the repository's configured
`dotnet11-workloads` and `dotnet-public` feeds. A fresh isolated install on
macOS resolved:

| Workload | Manifest |
|----------|----------|
| `macos` | `27.0.12212-net11-rc.2/11.0.100-rc.2` |
| `maccatalyst` | `27.0.12212-net11-rc.2/11.0.100-rc.2` |
| `maui` | `11.0.0-rc.2.26505.5/11.0.100-rc.2` |

The workload supplies SDK and native reference packs; the projects explicitly
reference the same RC2 MAUI managed packages above. The Apple RC2 manifests
provide OS 27 reference packs for `net11.0-*27.0`; their .NET 10 compatibility
packs remain at OS 26. Essentials.AI and its Xcode 27 device lane therefore use
.NET 11 Apple targets while retaining the normal .NET 10 Apple targets in the
package. Do not pass a .NET 10 workload set to the .NET 11 SDK: that combination
was tested and produced conflicting SDK 10/11 manifests.

The MAUI RC2 release commit descends from the merge commits for the public
Essentials bridge, dialog interfaces, gesture factory and dispatch APIs,
external Resizetizer processing, platform-specific folders, Blazor platform
handlers, and view screenshots used by these backends.

The repository keeps its existing MAUI 10 package baseline for other products.
The community backend projects use the separately pinned
`CommunityPlatformMauiVersion`.

See [the implementation and validation report](community-platforms-net11-implementation.md)
for the work-package map, commands actually executed, native boundaries, and
remaining release gates.

## Observable compatibility changes

- GTK reports `DevicePlatform.Create("GTK")`, rather than `Linux`.
- WPF reports `DevicePlatform.Create("WPF")`, rather than `WinUI`.
- AppKit continues to report `macOS`.
- Backend-specific source folders can use `MauiPlatformSpecificFolder`.
  The backend-owned `MauiActiveBackend` values are `linux`, `macos`, and `wpf`;
  these are intentionally separate from the public `DevicePlatform` display
  identities.
- Inline XAML platform properties are case-sensitive. Use `GTK`, `macOS`, and
  `WPF`. Applications that must preserve an older branch can use the
  `OnPlatform<T>` element form and explicitly list both values during migration.
- Templates target `net11.0`, `net11.0-macos`, or Windows 10-versioned
  `net11.0-windows` as
  appropriate.

These packages remain experimental community backends and are not covered by
the official .NET MAUI support policy.

## Integration ownership

.NET MAUI now owns the shared framework contracts:

- Essentials static facades are initialized from application DI.
- Dialogs use `IAlertManagerSubscription`.
- Native gesture managers use `IGesturePlatformManagerFactory` and public
  recognizer/controller dispatch.
- Blazor integrations select their native handler with `UsePlatformHandler`
  after all `AddMauiBlazorWebView` registrations.
- Resizetizer emits `MauiProcessedImage`, `MauiProcessedFont`, and
  `MauiProcessedAsset`.
- Per-view capture is provided through `IViewScreenshot`.

The backends continue to own native rendering, dispatchers, resource
destinations, icons and manifests, font registration, native WebView hosting,
window lifecycle callbacks, and OS packaging.

## Native validation boundaries

Native GTK tests require Linux with GTK4, WebKitGTK, an X11/Wayland display, and
a DBus session. Secret storage additionally requires libsecret's `secret-tool`
and an unlocked Secret Service provider. GeoClue foreground updates use
`where-am-i` polling rather than a persistent D-Bus subscription. WPF native
tests require Windows 10 version 1809 or later, STA execution, SAPI for speech,
location privacy grants, MSIX identity for app actions, and WebView2
where applicable. GTK photo/video capture requires GStreamer 1.0 command-line
tools plus the `v4l2src`, `videoconvert`, `jpegenc`, `vp8enc`, and `webmmux`
plugins and an accessible `/dev/video*` device. It deliberately does not use
`autovideosrc`, whose no-camera fallback can produce synthetic test-pattern
media. WPF camera capture is explicitly unsupported until the backend integrates
the Windows App SDK desktop `Microsoft.Windows.Media.Capture.CameraCaptureUI`;
the UWP-only API is not used. AppKit photo/video capture requires a camera, an
application `NSCameraUsageDescription`, and a granted camera
privacy permission. AppKit video is camera-only and does not request microphone
access. AppKit .NET 11
runtime scenarios and the Apple OS 27 Essentials.AI lanes require Xcode 27 and
the pinned Apple workloads above. The implementation host has Xcode 26.4.1, so
AppKit .NET 11 checks are compile/package checks and the Essentials.AI Apple 27
native bridge cannot compile locally. A cross-platform compile is not evidence
that a native runtime path passed.

Portable package acceptance has run for GTK and WPF using freshly packed local
NuGets and a referenced resource library. It covers exact logical paths/bytes,
non-ASCII and zero-byte assets, incremental content and metadata mutation,
resource deletion, no-build publish, collision errors, and a same-consumer
GTK-to-WPF backend switch. The equivalent AppKit package consumer reaches the
native link step locally but requires the macOS 27 SDK from Xcode 27. Native
image pixels, installed bundle/package lookup, fonts, accessibility, power,
speech, dialogs, gestures, capture, and WebView behavior are certified only by
their target-OS jobs, not by these portable checks.
