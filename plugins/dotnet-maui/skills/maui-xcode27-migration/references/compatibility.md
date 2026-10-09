# Compatibility evidence

Snapshot: **2026-10-01**. Recheck official releases before recommending installation.
Use Microsoft Learn for platform/lifecycle guidance, GitHub release notes and
tagged source for exact versions and APIs. Do not turn this snapshot into a
future promise of Xcode 27.1 support.

Authenticated GitHub release queries can include unpublished drafts. Require
`draft: false`, a non-null `published_at`, and an actual publicly available
version/workload set before recommending installation. Reject `TBD` placeholders
and verify the corresponding packages through the permitted feeds. A published
prerelease is not the same as a draft: the `prerelease` flag alone does not
establish or rule out support. Report unavailable publication evidence as a
blocker, not as permission to use draft instructions.

## Version decisions

| App | Decision |
|---|---|
| .NET 10, MAUI older than 10.0.110 | Update MAUI to a compatible release containing the scene forwarding fixes before migrating. |
| .NET 10, newer compatible MAUI | Preserve it; 10.0.110 is a floor, not a downgrade instruction. |
| .NET 9 | No Apple 27 packs; obtain approval for a .NET 10 upgrade, including dependencies/CI, or retain the supported older toolchain. |
| .NET 11 | Verify `MauiUISceneDelegate` in the installed app-resolved `Microsoft.Maui.dll`; check custom forwarding APIs separately. Scene support was observed in Preview 6; recheck the actual GA SDK/workload/MAUI artifacts as they become available. Do not apply the .NET 10 MAUI 10.0.110 pin. |
| .NET 11 RC1 | Has scene infrastructure but lacks the scene quick-action selector and Essentials SceneOpenUrl/SceneContinueUserActivity forwarding; its Apple requirements are Xcode 26.6. Do not apply a .NET 10 package pin. |
| .NET 11 RC2 source | Contains fixes; source presence alone does not establish publication or supported Xcode. Check the actual released workload and MAUI package. |
| Xcode 27.1 beta | Feature enablement is out of scope; diagnosing a mismatch with released 27.0 packs is in scope. A merged PR into an xcode27.1 branch is not a released workload. |

The public .NET 10 Apple release `27.0.10722` requires **Xcode 27.0**.
Its documented pairing is **.NET SDK 10.0.401**, workload set **10.0.401.1**,
and MAUI **10.0.110+**. Xcode 27.0 requires **macOS 26.6+** on the build host.
The iOS simulator deployment floor is 16.0; distinguish this from the physical
iOS device minimum of 15.0, and use an iOS 27 simulator for runtime validation.
The version check compares major/minor; do not disable it or assume 27.1 is
interchangeable. Workload installation should follow the exact release's
instructions and SDK feature band, with approval and preferably an isolated
toolchain. Do not invent a workload-set version from the Apple pack version.

## .NET 11 evidence

The reported MauiDay/event-app run used the .NET 11 Preview 6 SDK
**11.0.100-preview.6.26359.118**,
Microsoft.iOS **26.5.11720-net11-p6**, and **Xcode 26.6**, with a MAUI iOS head
and no custom lifecycle/URL/shortcut code. `MauiUISceneDelegate` was present;
the delegate + manifest worked at runtime on a newer iOS simulator while the
app remained linked against SDK 26.5. This is observed scene-migration evidence,
not a promise of Xcode 27 build support or complete custom forwarding behavior
for .NET 11 generally.

Resolve the actual MAUI assembly from the app's package assets/installed packs
(use the iOS implementation/reference assembly, not an unrelated cached version).
For the upcoming GA, repeat this against the actual released SDK, workload and
MAUI package; do not extrapolate from Preview 6 or RC1:

```sh
strings "<resolved-package-or-pack>/Microsoft.Maui.dll" | grep -F MauiUISceneDelegate
```

Reflection or an assembly browser can confirm the type and needed signatures.
A string hit is an initial presence check; compile against the same resolved
assembly to verify usability. If absent, check that .NET 11 release's APIs
before proposing a compatible update; never substitute .NET 10 packages.
Do not assume .NET 11 releases share RC1's API surface or Xcode requirements.
Keep the selected build Xcode matched to that release's Apple pack; newer-OS
simulator tooling is a separate concern (see [validation.md](validation.md)).

## Sources

- [MAUI app lifecycle](https://learn.microsoft.com/dotnet/maui/fundamentals/app-lifecycle?view=net-maui-10.0)
- [Supported platforms](https://learn.microsoft.com/dotnet/maui/supported-platforms?view=net-maui-10.0)
- [MAUI scene changes, dotnet/maui#38601](https://github.com/dotnet/maui/pull/38601)
- [Backport, dotnet/maui#38664](https://github.com/dotnet/maui/pull/38664)
- [MAUI 10.0.110](https://github.com/dotnet/maui/releases/tag/10.0.110)
- [Apple .NET 10 Xcode 27.0 release](https://github.com/dotnet/macios/releases/tag/dotnet-10.0.1xx-xcode27.0-10722)
- [27.1 branch work, not release evidence](https://github.com/dotnet/macios/pull/26705)
- [BundledVersions.targets source template](https://github.com/dotnet/maui/blob/10.0.110/src/Workload/Microsoft.Maui.Sdk/Sdk/BundledVersions.in.targets)
- [Apple SDK deployment minima](https://github.com/dotnet/macios/blob/dotnet-10.0.1xx-xcode27.0-10722/tools/common/SdkVersions.cs)
- [Apple SDK Xcode validation](https://github.com/dotnet/macios/blob/dotnet-10.0.1xx-xcode27.0-10722/dotnet/targets/Xamarin.Shared.Sdk.targets)
- [MAUI .NET 11 RC1](https://github.com/dotnet/maui/releases/tag/11.0.100-rc.1.26458.5)
- [Apple .NET 11 RC1](https://github.com/dotnet/macios/releases/tag/dotnet-11.0.1xx-rc1-12193)

General MAUI supported-platform documentation may describe a lower historical
minimum. The selected Apple SDK's deployment minimum also applies: SDK 27
requires iOS 15.0 and Catalyst 17.0 (macOS 14). Preserve higher app minima.
