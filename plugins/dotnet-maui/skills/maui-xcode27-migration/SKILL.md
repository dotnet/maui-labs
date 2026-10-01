---
name: maui-xcode27-migration
description: >-
  Only for existing .NET MAUI iOS/Mac Catalyst apps needing Xcode 27.0
  compatibility or SDK 27 scene-lifecycle repair. USE FOR: preparing a MAUI app for Xcode 27,
  SDK 27 upgrades, or an app that builds but exits after its splash screen with
  "UIScene lifecycle is required"; audit scene manifests, deployment targets,
  AppDelegate callbacks, deep links, authentication, and quick actions even
  when the request does not mention migration. Also use for an Xcode 27.0
  workload mismatch audit (including when Xcode 27.1 is selected).
  DO NOT USE FOR: iPhone Duo
  hinge/layout work, Xcode 27.1 beta enablement, ordinary UI changes, non-MAUI
  apps, listing installed Xcodes, new auth features unrelated to an SDK upgrade,
  or projects without iOS/Mac Catalyst targets. Xcode being mentioned in an
  otherwise unrelated request is not a reason to activate this skill.
---

# Xcode 27 compatibility for existing MAUI apps

## Purpose and inputs

Make an existing app scene-ready without losing its lifecycle behavior. This
requirement affects ordinary iPhones and iPads too, not just new form factors:
apps built against SDK 27 and running on iOS 27 need the scene lifecycle.
An existing binary built with an older SDK does not suddenly require a rebuild.
This is not a foldable-layout migration or permission to use Xcode 27.1.

Obtain the project/solution, supported OS policy, current SDK/workload/package
versions, selected Xcode, and any known startup error. For an audit-only or plan
request, return findings in the response and stop before any edits, including
"safe" partial scene migrations or version bumps. Do not restore or build during
an audit-only request: those create `obj`/`bin` files even without source edits.
Use file inspection and read-only version/property queries; if they cannot
resolve without restore, report the missing evidence. Otherwise perform the read-only inventory first, resolve
support decisions, then make the smallest safe changes.

Read [compatibility.md](references/compatibility.md) for release evidence and
[lifecycle.md](references/lifecycle.md) before changing callbacks. Recheck dated
release facts against Microsoft Learn and the linked official release/source;
do not treat a merged PR as an available workload.

## Workflow

### 1. Inventory before modifying the project or toolchain

Read the applicable repository instructions and current diff. Locate all MAUI
Apple heads (including conditional TargetFrameworks), imported
Directory.Build.props/targets, Directory.Packages.props, global.json, NuGet.config,
package references/lockfiles, platform source inclusions, Info.plist files,
AppDelegate and SceneDelegate classes, and ConfigureLifecycleEvents calls.
Include CI SDK/workload/Xcode pins: changing a project SDK pin while leaving
CI installing only the old SDK makes the migration fail on the build server.
Do not assume an empty project-level MauiVersion means there is no version pin:
the workload supplies defaults through BundledVersions.targets.

Use the project's own SDK selection and, where available:

```sh
dotnet --info
dotnet workload list
xcodebuild -version
dotnet msbuild MyApp.csproj -p:TargetFramework=net10.0-ios -getProperty:MauiVersion,MauiWorkloadVersion,SupportedOSPlatformVersion,TargetPlatformVersion
```

Repeat property inspection for Catalyst and relevant build configurations.
MSBuild evaluation can be blocked by a missing SDK/workload; report that rather
than claiming the effective versions are known. Inspect installed workload
manifests and project.assets.json when needed to establish actual package/packs.
Do not install/update workloads, edit global SDK settings, run xcode-select, or
disable Apple SDK/Xcode version checks as part of this inventory.

If there are no MAUI iOS/Catalyst targets, stop with "not applicable"; add no
Apple files. For custom scenes, multiwindow support, or lifecycle overrides,
record existing behavior and the required reconciliation before editing.

### 2. Establish a supported version and OS policy

For .NET 10, use MAUI **10.0.110 or a later compatible servicing release**
containing the scene fixes, plus a released .NET 10 Apple workload supporting
the selected Xcode 27.0. MAUI package and Apple workload versions are independent.
New templates do not retrofit existing projects.

If the app resolves an older MAUI version through `$(MauiVersion)`, a local
`<MauiVersion>10.0.110</MauiVersion>` can pin .NET 10 references. Edit the actual
version owner instead when centrally managed; preserve a newer compatible pin.
Check explicit package versions and all MAUI packages for coherence. Do not
insert a competing project pin that a later import overrides, and do not
silently move between .NET major versions.
Reconcile any approved project `global.json` update with its CI setup steps;
this is distinct from changing the developer machine's global tool selection.
If toolchain provisioning is outside the request, explicitly report the
remaining SDK/workload/CI actions instead of claiming the project builds there.

The iOS 27 SDK minimum deployment target is **15.0**; Catalyst is **17.0**
(macOS 14). Preserve higher minima. If the app currently supports older OS
versions, explain the lost support and obtain an explicit decision before
raising them. An instruction to "upgrade Xcode" is not approval to drop users.
If no decision is available, report the blocker and stop before migration edits;
do not treat explaining the tradeoff as the user's approval.
Once approved, update the relevant conditional properties, preserving non-Apple
targets and any intentional per-configuration policies:

```xml
<SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'ios'">15.0</SupportedOSPlatformVersion>
<SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'maccatalyst'">17.0</SupportedOSPlatformVersion>
```

Keep version text inline, without surrounding whitespace. Audit plist deployment
keys and other platform-version declarations for conflicting values.

For .NET 9, present an intentional .NET 10 upgrade plan: there are no .NET 9
Apple 27 packs. For .NET 11 previews/RCs, check that exact release's support and
forwarding APIs; do not pin .NET 10 packages into .NET 11. RC1 is not the .NET 10
recipe. If Xcode does not match the installed Apple workload, stop before a
build and propose a supported pairing; do not infer 27.1 support from 27.0.

### 3. Reconcile scenes on each Apple head

For a head without scene configuration, add `Platforms/iOS/SceneDelegate.cs`
(and the equivalent file under `Platforms/MacCatalyst` for that head):

```csharp
using Foundation;
using Microsoft.Maui;

namespace MyApp;

[Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate
{
}
```

Use the app's namespace and platform compilation conventions. Ensure precisely
one Objective-C registration named `SceneDelegate` per target; platform folders
are normally filtered by MAUI, but custom source inclusions need inspection.
Keep `AppDelegate : MauiUIApplicationDelegate` and its startup responsibilities.

Merge this key into the **root dictionary** of each applicable Info.plist:

```xml
<key>UIApplicationSceneManifest</key>
<dict>
  <key>UIApplicationSupportsMultipleScenes</key>
  <false/>
  <key>UISceneConfigurations</key>
  <dict>
    <key>UIWindowSceneSessionRoleApplication</key>
    <array>
      <dict>
        <key>UISceneConfigurationName</key>
        <string>__MAUI_DEFAULT_SCENE_CONFIGURATION__</string>
        <key>UISceneDelegateClassName</key>
        <string>SceneDelegate</string>
      </dict>
    </array>
  </dict>
</dict>
```

This is a default for a previously unconfigured single-window app, not a
replacement template. Preserve custom registered names, scene delegates,
configuration names, session roles, storyboard decisions, and real multiwindow
intent. A plain UISceneDelegate with app-specific behavior needs deliberate
integration with MauiUISceneDelegate, not deletion. Check dynamic scene
configuration overrides as well as the plist. If the custom implementation
cannot be safely reconciled, explain the exact unresolved behavior and stop
short of claiming completion. Never append a duplicate manifest or registration.

### 4. Preserve lifecycle, links, authentication and quick actions

Audit **both** ConfigureLifecycleEvents registrations and AppDelegate overrides,
including callbacks inherited from custom base types. Use the signature and
ownership guidance in [lifecycle.md](references/lifecycle.md). When any custom
callback exists, read the additive transformation and per-scene deferred-work
example in [custom-lifecycle-example.md](references/custom-lifecycle-example.md)
before editing. Leftover AppDelegate callbacks are unfinished migration work,
even if a scene manifest already exists; reconcile their intended behavior
rather than treating the presence of the manifest as a completed migration.

- Before editing, make a before/after mapping of every custom callback's side
  effects, including logging-only observers and multiple registrations of the
  same event. After editing, verify each side effect still has a delivery path.
  A compiling manifest does not prove behavior was preserved.
- Explicitly wire `OnActivated`, `OnResignActivation`, `DidEnterBackground`,
  and `WillEnterForeground` behavior to `SceneOnActivated`,
  `SceneOnResignActivation`, `SceneDidEnterBackground`, and
  `SceneWillEnterForeground`. Leaving the old AppDelegate override unchanged
  is not a scene migration. If retaining it for a deliberate fallback, also
  implement the scene path and prevent duplicate delivery.
- Reconcile activation/background and URL/user-activity callbacks with Scene*
  events; their parameters differ, so a mechanical rename is unsafe.
- Preserve base calls in SceneDelegate overrides so MAUI still creates windows,
  raises lifecycle events, and forwards Essentials authentication callbacks.
- FinishedLaunching still runs, but AppDelegate.Window remains null in scene
  mode. Defer window-dependent initialization until the scene's window exists.
- Handle warm and cold links separately. SceneWillConnect receives connection
  options before the MAUI window is created; queue cold navigation until ready.
  If the app has a ContinueUserActivity handler, moving it to
  SceneContinueUserActivity only repairs **warm** delivery. Also enumerate
  `connectionOptions.UserActivities` and queue that app-specific activity
  behavior for **cold** delivery. Do not assume base.WillConnect replays those
  activities through SceneContinueUserActivity. Check both collections even
  when the former FinishedLaunching code inspected only LaunchOptionsUrlKey.
  WebAuthenticator forwarding is not a general deep-link router.
- AppDelegate.PerformActionForShortcutItem is no longer the callback path.
  Move custom handling to the iOSLifecycle.PerformActionForShortcutItem
  registration. The default builder already forwards Essentials: do not add
  another Platform.PerformActionForShortcutItem forwarding call. **Each custom
  registration** must acknowledge its completion exactly once, true if handled
  or false if not, including logging-only observers. Async handling is allowed;
  complete after the decision and handle failure without double completion.
  Preserve existing observers and their side effects when moving a separate
  AppDelegate shortcut handler; replacing an observer with that handler loses
  behavior even if both previously listened for the same shortcut.

Before proceeding to validation, close this behavior checklist against the
actual edited code, not comments or the plan:

| Gate | Evidence required |
|---|---|
| Activation/background | Each original side effect is reached from its Scene* path, not only an AppDelegate fallback. |
| Warm URLs and activities | Both SceneOpenUrl and SceneContinueUserActivity (or verified equivalent scene overrides) retain the app's original handlers. |
| Cold URLs and activities | Both connection-option collections are considered, with typed native elements and window-dependent work deferred. |
| Shortcut observers | Every existing logging-only registration now calls its own completion with false; none is left unchanged with a missing acknowledgement. |
| Custom shortcuts | The original handling decision and side effects remain, completion is exactly once, and default Essentials forwarding is not duplicated. |

An incomplete gate is a migration blocker. Fix it before claiming completion,
or explicitly return an unresolved-behavior report without calling the partial
patch complete. Preserving a pre-existing missing shortcut acknowledgement
"unchanged" is not safe: the scene dispatch now depends on that acknowledgement.

### 5. Validate and report honestly

Check XML/plist structure (on macOS `plutil -lint -- <path>`), inspect the
evaluated package/minimum versions, and review the diff for preserved settings.
Run the migration audit again: an already-correct app should produce no edits.

With a supported toolchain, restore/build every affected target, for example:

```sh
dotnet restore MyApp.csproj -p:TargetFramework=net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64
dotnet build MyApp.csproj -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64 --no-restore
dotnet restore MyApp.csproj -p:TargetFramework=net10.0-maccatalyst
dotnet build MyApp.csproj -f net10.0-maccatalyst --no-restore
```

Targeted restore avoids unrelated workloads obscuring the Apple validation.
Adapt names, frameworks and architecture to the actual project and host. Do not
substitute an unsigned/stub compile or a custom 27.1 build for supported 27.0
validation. A build alone does not exercise lifecycle behavior.

With user-approved simulator/device access, launch on an ordinary iOS 27
iPhone/iPad; verify startup past splash, foreground/background, warm/cold links,
auth return, quick actions (handled and unhandled), and any custom multiwindow
behavior. Validate Catalyst separately. Coordinate shared devices, do not
reset/uninstall apps or modify global Xcode selection without permission.
If DevFlow is already integrated, use its debugging skill for observation;
installing DevFlow is not a prerequisite for this migration.

Report changed files, resolved versions and support decisions, preserved/custom
behavior, actual commands and results, and blocked or untested runtime cases.
Distinguish static checks, successful compilation, and executed device tests.
Never report compatibility as verified when the supported toolchain is missing.
