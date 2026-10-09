---
name: maui-xcode27-migration
description: >-
  USE FOR Xcode 27 compatibility decisions and migration of existing .NET MAUI
  iOS/Mac Catalyst apps, INCLUDING partially migrated apps that already have
  scene manifests/delegates but still use AppDelegate or application-level
  callbacks. Load BEFORE file discovery; file access is not a prerequisite.
  Existing scene files do not mean migration is finished. Also use for SDK 27
  "UIScene lifecycle is required" startup failures, preserving warm/cold links
  and quick actions during this upgrade, readiness/no-op audits, read-only
  .NET 9/.NET 11 compatibility or pinning questions, and Apple 27.0 packs with
  Xcode 27.1 mismatches. Compatibility audits do not authorize toolchain
  installation or major upgrades. DO NOT USE FOR Android-only, Windows-only,
  or no-Apple-head apps; ordinary UI or hinge/layout requests; listing Xcodes;
  non-MAUI apps; or unrelated auth work, even if Xcode 27 is mentioned.
---

# Xcode 27 compatibility for existing MAUI apps

## Purpose and inputs

Make an existing app scene-ready without losing its lifecycle behavior. This
requirement affects ordinary iPhones and iPads too, not just new form factors:
apps built against SDK 27 and running on iOS 27 need the scene lifecycle.
An existing binary built with an older SDK does not suddenly require a rebuild.
This is not a foldable-layout migration or permission to use Xcode 27.1.

**Preserve side effects, not obsolete delivery locations.** In a scene-enabled
app, `AppDelegate.Window` is null **even in FinishedLaunching**; there is no
"pre-scene window" exception. Keeping cold-only window initialization there
does not preserve it. Move its original body to cold connection-option work
that runs once the owning scene's window exists. Likewise, application-level
`ContinueUserActivity` registration is not reached by scene forwarding: use
`SceneContinueUserActivity` (or the verified scene override) for warm delivery
and process cold `UserActivities` separately. Once its behavior is moved, remove
the obsolete application-level registration; do not leave both paths wired.
Each custom shortcut registration,
including a logging-only observer, must complete its own acknowledgement.
These are migration requirements, not optional cleanup of old code.

Obtain the project/solution, supported OS policy, current SDK/workload/package
versions, selected Xcode, and any known startup error. Match the requested mode:
**implement/migrate** means edit the applicable files; **show/propose/explain**
means provide concrete changes without editing; **audit-only** means findings
without edits. When code changes are requested as a proposal, include the
requested snippets and edit locations, not just filenames or a reference to
this skill. Do not claim a proposal was applied. For an audit-only or plan
request, stop before any edits, including
"safe" partial scene migrations or version bumps. Do not restore or build during
an audit-only request: those create `obj`/`bin` files even without source edits.
Use file inspection and read-only version/property queries when needed; if they
cannot resolve without restore, report the missing evidence. Otherwise perform
the read-only inventory first, resolve support decisions, then make the
smallest safe changes.

Read [compatibility.md](references/compatibility.md) for covered version
decisions. Use its tagged evidence for those exact releases; check Microsoft
Learn and the official release/source when the task depends on newer releases,
uncovered versions, or current publication status. If that check is unavailable,
date the known evidence and report what remains unverified. Do not infer
publication from a merged PR. Read lifecycle references only when reconciling
custom callbacks or scene behavior, not for every basic version audit.

## Workflow

### Fast path: no custom lifecycle code

For a standard single-window app, first check its Apple TFMs, effective MAUI/
Apple versions and applicable Info.plist files. Search app sources (excluding
bin/obj) for `ConfigureLifecycleEvents`, AppDelegate overrides other than the
template's `CreateMauiApp`, `OpenUrl`, `ContinueUserActivity` and `ShortcutItem`.
If none are found and the delegates have no custom base classes or scene setup,
skip the detailed callback inventory and step 4: verify `MauiUISceneDelegate`
availability (especially on .NET 11 previews), add the delegate + manifest in
step 3, run the audit in step 5, then complete step 6, including **built-bundle**
and runtime checks. Absence of custom callbacks does not waive validation.

**Skip every Mac Catalyst step** (files, properties, minima, builds and runtime
checks) when no `maccatalyst` TFM is present, including conditional TFMs.

### 1. Inventory before modifying the project or toolchain

Read the applicable repository instructions and current diff. Inspect a supplied
project path relative to the current workspace unless it is explicitly absolute.
Prefer direct file reads and workspace-scoped glob/grep tools for discovery,
not shell `find` pipelines: shell execution may be unavailable even when
project file tools are available. Keep discovery under that workspace; never
search the filesystem root for an app. If the supplied path is missing or
inaccessible, report that blocker rather than broadening access. Locate all MAUI
Apple heads (including conditional TargetFrameworks), imported
Directory.Build.props/targets, Directory.Packages.props, global.json, NuGet.config,
package references/lockfiles, platform source inclusions, Info.plist files,
AppDelegate and SceneDelegate classes, and ConfigureLifecycleEvents calls.
Include configuration- or flavor-specific Info.plist files selected by MSBuild;
updating only the default plist can leave one shipping configuration on the
legacy lifecycle.
Include CI SDK/workload/Xcode pins: changing a project SDK pin while leaving
CI installing only the old SDK makes the migration fail on the build server.
Do not assume an empty project-level MauiVersion means there is no version pin:
the workload supplies defaults through BundledVersions.targets.

For a conceptual audit or proposal with supplied configuration, distinguish
those stated facts from locally verified facts. Do not probe an unrelated host
toolchain or missing project tree to answer a configuration question. If the
provided app is already correctly migrated, report no changes rather than
rebuilding or repeating the migration.

Use the project's own SDK selection and, where available:

```sh
dotnet --info
dotnet workload list
xcodebuild -version
dotnet msbuild MyApp.csproj -p:TargetFramework=net10.0-ios -getProperty:MauiVersion,MauiWorkloadVersion,SupportedOSPlatformVersion,TargetPlatformVersion
```

Repeat property inspection for Catalyst only if targeted, and relevant build configurations.
MSBuild evaluation can be blocked by a missing SDK/workload; report that rather
than claiming the effective versions are known. Inspect installed workload
manifests and project.assets.json when needed to establish actual package/packs.
Do not install/update workloads, edit global SDK settings, run xcode-select, or
disable Apple SDK/Xcode version checks as part of this inventory.

If there are no MAUI iOS/Catalyst targets, stop with "not applicable"; add no
Apple files. For custom scenes, multiwindow support, or lifecycle overrides,
record existing behavior and the required reconciliation before editing.
Use the read-only helper in step 5 to record existing warning patterns before
editing, then repeat it on the proposed result.

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
For a conditional pin, verify when the condition is evaluated: an empty-only
fallback does not replace an older value supplied by an earlier import. Inspect
the effective result when possible; XML presence alone is not proof of resolution.
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
forwarding APIs, including `MauiUISceneDelegate` in the installed
`Microsoft.Maui.dll` as described in [compatibility.md](references/compatibility.md);
do not apply the .NET 10 MAUI 10.0.110 pin to .NET 11. The documented
**.NET 11 RC1 pairing requires Xcode 26.6**, not an arbitrary 26.x installation.
RC1 has scene infrastructure but lacks the scene App Actions selector and
Essentials URL/user-activity forwarding fixes. It is not the .NET 10 recipe.
Keep that known RC1 pairing distinct from a fresh check for published RC2
support; an unavailable publication lookup does not erase the tagged RC1 facts.
If Xcode does not match the installed Apple workload, stop before a
build and propose a supported pairing; do not infer 27.1 support from 27.0.

### 3. Reconcile scenes on each Apple head

For an already configured head, read and preserve the existing Objective-C
`[Register]` name and its `UISceneDelegateClassName` binding before changing
callbacks. The CLR class name does not determine that registration string.
Merge into the existing delegate instead of copying the new-head template
below. If the actual binding is unavailable, report it as unresolved rather
than guessing a default and claiming it was preserved.

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
Do not remove or replace the app's existing `MauiSplashScreen` or
`UILaunchStoryboardName` configuration while editing the manifest, and do not
add `UISceneStoryboardFile`; MAUI creates the scene window programmatically.

### 4. Preserve lifecycle, links, authentication and quick actions

Audit **both** ConfigureLifecycleEvents registrations and AppDelegate overrides,
including callbacks inherited from custom base types. Use the signature and
ownership guidance in [lifecycle.md](references/lifecycle.md). When any custom
callback exists, read the additive transformation and per-scene deferred-work
example in [custom-lifecycle-example.md](references/custom-lifecycle-example.md)
before editing. Leftover AppDelegate callbacks are unfinished migration work,
even if a scene manifest already exists; reconcile their intended behavior
rather than treating the presence of the manifest as a completed migration.

For **MAUI 10.0.110**, use these verified subclass signatures at the edit point:

| Callback | `MauiUISceneDelegate` override signature | Bodies to retain |
|---|---|---|
| Activation | `public override void OnActivated(UIScene scene)` | Existing scene activation **and** former application activation, separately. |
| Warm URLs | `public override bool OpenUrl(UIScene scene, NSSet<UIOpenUrlContext> contexts)` | Original warm URL handler for every context, plus unconditional base forwarding. |
| Warm activity | `public override bool ContinueUserActivity(UIScene scene, NSUserActivity activity)` | Original activity handler and its handled result, plus unconditional base forwarding. |

These are MAUI APIs, not the similarly named native UIKit APIs. Keep the
unconditional base call and combine its handled result with the app's result.
The linked example supplies the complete implementation pattern; a signature
alone is not an implementation of the app's original behavior.

An existing `OnActivated(UIScene)` method is **not** evidence that a separate
`AppDelegate.OnActivated(UIApplication)` body was moved. The additive shape is:

```csharp
public override void OnActivated(UIScene scene)
{
    base.OnActivated(scene);
    HandleExistingSceneActivation(scene);
    HandleApplicationActivation(scene);
}
```

Extract the two original custom bodies into those helpers before wiring the
calls; they are not framework APIs or empty placeholders. Keep the existing
scene method's work and add the former application method's work. The reference
example includes the required helper declarations and deferred queue drain.
A mapping such as "AppDelegate.OnActivated -> existing scene OnActivated,
unchanged" fails this audit unless the original application body is actually
called there.

Preserve **handled results** as well as side effects. If the original warm URL
override only logged and returned its base result, its scene equivalent keeps
that result:

```csharp
bool forwarded = base.OpenUrl(scene, contexts);
foreach (var context in contexts.ToArray<UIOpenUrlContext>())
    ObserveOriginalWarmUrl(context.Url);
return forwarded;
```

`ObserveOriginalWarmUrl` contains the original logging body. If extracted as
a bool-returning helper instead, it returns **false**, not true: observing a
URL does not handle it. Only real app handling may add a true result.

- Before editing, make a before/after mapping of every custom callback's side
  effects, including logging-only observers and multiple registrations of the
  same event. After editing, verify each side effect still has a delivery path.
  Follow each path from its scene callback or registration through any helpers
  to the moved body. Include those bodies in concrete proposals too: an
  undefined helper, empty block, or comment describing the old work does not
  implement it. A compiling manifest does not prove behavior was preserved.
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
  Retain every cold URL context in per-scene pending work; a single pending-URL
  field can overwrite earlier inputs before the window is ready.
  If the app has a ContinueUserActivity handler, moving it to
  SceneContinueUserActivity only repairs **warm** delivery. Also enumerate
  `connectionOptions.UserActivities` and queue that app-specific activity
  behavior for **cold** delivery. Do not assume base.WillConnect replays those
  activities through SceneContinueUserActivity. Check both collections even
  when the former FinishedLaunching code inspected only LaunchOptionsUrlKey.
  Treat absent connection-option collections as empty payloads, using
  `?.ToArray<T>() ?? []` or equivalent guards for both collections.
  An ordinary no-link launch must not throw before `base.WillConnect`.
  WebAuthenticator forwarding is not a general deep-link router.
- If the app handles notification taps or previously reads notification data
  from launch options, audit cold scene delivery through
  `connectionOptions.NotificationResponse` as a separate path. Preserve that
  app-specific behavior and defer UI/navigation work until the owning scene's
  window is ready. Push registration and ordinary
  `DidReceiveRemoteNotification` handling remain application-level unless the
  app's exact API contract says otherwise.
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

### 5. Run a read-only completion audit

For all migrations, including the fast path, run the bundled
[audit helper](scripts/audit-lifecycle.mjs) using an already available Node.js
22+ runtime. Resolve the script from this installed skill, not from an assumed
file in the app repository:

```sh
node "<installed-skill-directory>/scripts/audit-lifecycle.mjs" "<app-source-directory>" --json
```

It only reads source and prints findings with file/line locations. It does not
build, install dependencies, evaluate MSBuild, follow discovered symbolic links,
or modify the app. Exit **1** means warnings requiring review, not a failed
execution; **2** means the scan was incomplete or its input was invalid.
Exit **0** means only that these patterns were not found, **not** that the
migration is correct. Generated/cache directories are excluded and reported.
The report also identifies a simple-migration candidate when no lifecycle code
is found and lists source `SceneDelegate.cs` / `UIApplicationSceneManifest`
presence per discovered Apple platform folder. These are textual presence
checks, not proof of delegate registration, active TFMs or a built manifest.

`X27_APP_WINDOW` flags possible app-delegate window access;
`X27_SINGLE_URL` flags direct single-item selection from `UrlContexts`.
For each warning, either repair the lost behavior or document a code-backed
justification, such as selecting a preview while a separate loop processes
every URL. Do not rewrite legitimate code merely to silence a warning.
This is a textual check: aliases, helper indirection, inheritance through custom
base types, preprocessor configuration and linked/generated sources still need
manual inspection. Audit linked source directories separately.

If Node or shell access is unavailable, do not install tools or bypass a
permission denial. Perform the searches below and explicitly report that the
helper was **not run** and custom-migration verification remains blocked.
Never invent a clean helper result or report the custom migration fully verified.

Use the workspace's file-search tool on the edited source, even when shell or
build commands are unavailable. These searches are conservative warning
signals, not semantic proof:

| Scope | Search expression | Investigate each match |
|---|---|---|
| Apple AppDelegate files | `\b(Window|LaunchOptionsUrlKey)\b` | Window-dependent work still in FinishedLaunching is not repaired by calling it cold-only. Its replacement must consume the owning scene's `connectionOptions.UrlContexts`, not a launch URL cached on AppDelegate and reused across scenes. |
| Apple C# source using MAUI 10.0.110 | `override\s+void\s+(OpenUrlContexts|ContinueUserActivity)\b` | These are not the MAUI bool override signatures; read the tagged API reference before replacing them. |
| Lifecycle-registration source | `\.ContinueUserActivity\s*\(\s*\(` | An application-level registration does not receive scene delivery. Verify its original body has a reachable scene counterpart. |
| Scene connection code | `UserActivities` | If an app activity handler exists, verify cold elements reach its actual body, not just an empty loop or a comment. |
| Cold collection extraction | `First`, `Single`, `ElementAt`, `Take`, indexing | Trace each selection back to its source. Do not reduce the URL/activity collection to one item before processing or queuing every input. |

Also search for each original callback's business calls and logging messages
from the inventory. Read the containing methods and helper implementations:
the existing scene activation observation does not substitute for a different
application activation observation. Follow both cold collections through any
queue to their real side effects. Record file/method evidence for every mapped
body. Resolve warnings or report the exact blocker; a summary table asserting
preservation cannot override a missing implementation in the files.

### 6. Validate and report honestly

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

**Required after building:** inspect the actual output bundle, not only the
source plist:

```sh
plutil -p "<build-output>/<App>.app/Info.plist" | grep -A12 UIApplicationSceneManifest
```

Confirm the intended configuration and registered delegate in that output.
If missing, follow the targeted `obj/.../AppManifest.plist` and
`_CompileAppManifest.inputs` invalidation (or clean build) in
[validation.md](references/validation.md), rebuild, then repeat this check.
A successful incremental build can retain a stale manifest.

When the app uses Preferences/NSUserDefaults, also check `PrivacyInfo.xcprivacy`
for an **uncommented** `NSPrivacyAccessedAPICategoryUserDefaults` entry with
reason `CA92.1` (app-only defaults access); the MAUI template may leave it
commented out. Preserve other entries and choose reasons matching actual usage.

With user-approved simulator/device access, launch on an ordinary iOS 27
iPhone/iPad; verify startup past splash, foreground/background, warm/cold links,
auth return, notification taps when applicable, quick actions (handled and
unhandled), and any custom multiwindow behavior. Check the simulator process
logs for the `UIScene life cycle is now required` fault using
[validation.md](references/validation.md). Validate Catalyst separately only
when targeted. For a newer simulator OS, scope any necessary `DEVELOPER_DIR`
override to `simctl` commands, **never to `dotnet build`**.
Coordinate shared devices, do not reset/uninstall apps or modify global Xcode
selection without permission.
If DevFlow is already integrated, use its debugging skill for observation;
installing DevFlow is not a prerequisite for this migration.

Report changed files, resolved versions and support decisions, preserved/custom
behavior, actual commands and results, and blocked or untested runtime cases.
Distinguish static checks, successful compilation, and executed device tests.
Never report compatibility as verified when the supported toolchain is missing.
