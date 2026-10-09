# Scene migration validation

## Inspect the built manifest

Source `plutil -lint` proves syntax, not what ships. After building each affected
Apple head/configuration, locate its actual `.app` output for that TFM,
configuration and RID (Catalyst may use `Contents/Info.plist`) and inspect:

```sh
plutil -p "<build-output>/<App>.app/Info.plist" | grep -A12 UIApplicationSceneManifest
```

Require the manifest, intended configuration name and registered delegate.
If the excerpt is truncated, inspect the full `plutil -p` output.

In the reported MauiDay/event-app run with .NET 11 Preview 6, adding the source
manifest and even touching Info.plist did not refresh the incremental
`_CompileAppManifest` output. The bundle still lacked the manifest. After
confirming the exact active intermediate directory, remove **only these two
generated files** (replace the placeholder; do not delete source files):

```sh
rm "<active-obj-directory>/AppManifest.plist" "<active-obj-directory>/_CompileAppManifest.inputs"
dotnet build MyApp.csproj -f net11.0-ios -p:RuntimeIdentifier=iossimulator-arm64
```

Use the same configuration/RID as the affected build. Alternatively, clean and
rebuild that target/configuration. Reinspect the resulting bundle; do not assume
invalidation worked. If still missing, inspect the effective AppManifest input,
conditional plist selection and build logs rather than repeating deletion.
Install/launch this freshly rebuilt bundle, not a previously deployed copy.

## Runtime evidence

On an ordinary iOS 27 simulator, launch past the splash screen and exercise the
app's applicable scene behavior. Inspect logs for the actual executable process
name (`CFBundleExecutable`, not necessarily the display name):

```sh
xcrun simctl spawn <udid> log show --last 2m --predicate 'process == "<App>"'
```

Look for `UIScene life cycle is now required`: a fault on this launch indicates
the scene lifecycle has not taken effect; check the deployed bundle's manifest.
Its absence alone is not proof: confirm startup and any custom scene callbacks.

A newer simulator runtime may require `simctl` from another installed Xcode.
If needed, select it for each simulator command only:

```sh
DEVELOPER_DIR="/Applications/<simulator-capable-Xcode>.app/Contents/Developer" xcrun simctl spawn <udid> log show --last 2m --predicate 'process == "<App>"'
```

Do not export this globally, run `xcode-select`, or apply it to `dotnet build`.
The build must retain the Xcode pairing required by its installed Apple packs.
Testing on a newer simulator OS does not establish build support for that OS's
SDK or authorize layout/27.1 feature work.

## Adjacent privacy check

Apps using MAUI Preferences/NSUserDefaults for their own data normally declare
`NSPrivacyAccessedAPICategoryUserDefaults` with `CA92.1` in
`PrivacyInfo.xcprivacy`. Check the actual XML, not a grep hit inside a comment;
the MAUI template may ship this entry commented out. Preserve existing privacy
entries and verify the reason fits the app's actual access (shared suites may
need a different reason). Lint the source and confirm the privacy manifest is
included in the built bundle.
