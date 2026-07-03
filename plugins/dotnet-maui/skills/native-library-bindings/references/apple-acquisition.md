# Apple Native Artifact Acquisition

Acquire and verify Apple native assets before authoring bindings.

## Preferred order

1. Direct `.xcframework` from the vendor when available.
2. Swift Package Manager resolved and built into an `.xcframework`.
3. Carthage with `--use-xcframeworks` when the SDK ships a Cartfile-friendly release.
4. Source/Xcode project built as part of the binding.
5. CocoaPods workspace when it is the maintained distribution path.
6. Raw `.framework`/`.a` only when you can verify all required platform slices.

## Direct downloads and GitHub releases

For direct downloads:

- Prefer `.xcframework` over older fat `.framework` or `.a` layouts.
- Record vendor version and release URL in project/package docs.
- Verify checksum/signature when published.
- Inspect the archive before adding it to the binding project.

Use `scripts/Test-AppleXCFramework.ps1` for `.xcframework` inspection.

Manual inspection commands, using `find` instead of shell-specific recursive globs:

```bash
plutil -p MySdk.xcframework/Info.plist
find MySdk.xcframework -maxdepth 3 -type f
find MySdk.xcframework -type f -perm -111 -exec lipo -info {} \; 2>/dev/null
find MySdk.xcframework -type f -perm -111 -exec file {} \; 2>/dev/null
find MySdk.xcframework -type f -perm -111 -exec otool -L {} \; 2>/dev/null
```

Verify:

- Platform variants: iOS device, iOS simulator, Mac Catalyst, macOS, tvOS as needed.
- Architectures: arm64 device, arm64/x64 simulator where applicable.
- Headers or generated Swift headers exist for ObjC binding.
- Swift modules exist when Swift code is exposed.
- Dynamic libraries declare expected dependent frameworks.

### Direct GitHub release xcframework download (MSBuild-driven)

When the vendor publishes an `.xcframework` (often zipped) as a GitHub release
asset, pin the version in a single MSBuild property and drive download/unzip
as part of the build/restore instead of committing a manually-downloaded
binary. This keeps the "single source of truth" version in one place for
future updates.

```xml
<PropertyGroup>
  <!-- Single source of truth for the vendor SDK version. Bump this to update. -->
  <VendorSdkVersion>2.4.0</VendorSdkVersion>
  <VendorSdkDownloadUrl>https://github.com/vendor/MySdk/releases/download/$(VendorSdkVersion)/MySdk.xcframework.zip</VendorSdkDownloadUrl>
  <VendorSdkDownloadDir>$(MSBuildProjectDirectory)/native/download</VendorSdkDownloadDir>
</PropertyGroup>

<Target Name="DownloadVendorSdk" BeforeTargets="BeforeBuild"
        Condition="!Exists('$(VendorSdkDownloadDir)/MySdk.xcframework')">
  <DownloadFile SourceUrl="$(VendorSdkDownloadUrl)"
                DestinationFolder="$(VendorSdkDownloadDir)" />
  <Unzip SourceFiles="$(VendorSdkDownloadDir)/MySdk.xcframework.zip"
         DestinationFolder="$(VendorSdkDownloadDir)" />
</Target>
```

Notes:

- `DownloadFile` and `Unzip` are standard MSBuild tasks; no extra tooling is
  required for the common case.
- Record the exact release tag/URL used, since GitHub release assets can be
  replaced upstream even at the same version string.
- Verify checksum/signature when the vendor publishes one, before trusting the
  downloaded archive.

**Optional trimming.** If only some platform slices are needed (for example
dropping macOS/tvOS from a multi-platform `.xcframework`), it is possible to
strip unneeded slices to reduce package size. If you do this:

1. Remove the unneeded slice directory from inside the `.xcframework`.
2. Strip the code signature on the remaining/modified framework binaries
   (`codesign --remove-signature`) since removing files invalidates the
   original signature.
3. **Also edit `Info.plist`** and remove the corresponding `AvailableLibraries`
   dictionary entry for each removed slice. An `.xcframework` whose
   `Info.plist` still lists a slice that no longer exists on disk can fail to
   load or fail Xcode validation even though the remaining binaries are
   otherwise fine.
4. Re-run the inspection commands above against the trimmed `.xcframework`
   before using it in a binding project.

Do not trim slices casually — only do it when there is a concrete size/build
reason, and always keep the original untrimmed archive available in case the
trimming needs to be redone for a different platform combination later.

## Swift Package Manager

SPM resolves source packages, not necessarily ready-to-bind `.xcframework` output. If a package does not ship binary targets, build it into an `.xcframework` first.

For source packages:

1. Resolve the package at an explicit version or commit.
2. Build device and simulator variants for each target platform.
3. Enable distribution-stable settings where needed, such as `BUILD_LIBRARY_FOR_DISTRIBUTION=YES`.
4. Create an `.xcframework` with `xcodebuild -create-xcframework`.
5. Inspect the result before binding.

For binary SPM targets:

- Locate the referenced binary artifact.
- Verify the binary has the required slices.
- Treat it like a direct `.xcframework` download.

Do not point Objective Sharpie at Swift package source and expect it to bind pure Swift APIs. Bind ObjC-visible headers or use a direct Swift binding approach.

## CocoaPods

Use CocoaPods as a fallback when the SDK is distributed or maintained only through Pods.

Typical static-linking Podfile pattern:

```ruby
platform :ios, '15.0'
use_frameworks! :linkage => :static

target 'MyBinding' do
  pod 'VendorSdk', '1.2.3'
end
```

After `pod install`:

- Reference the generated `.xcworkspace` if the binding build depends on Pods.
- Avoid manual edits to generated Pods project files.
- Pin versions in `Podfile.lock` for reproducible builds.
- Document Pod source and license obligations.

## Carthage

Some Swift SDKs (e.g. Datadog iOS) publish a `Cartfile`. Carthage can produce
xcframeworks directly, which is convenient for binding:

```sh
carthage update --use-xcframeworks
```

- `--use-xcframeworks` yields `Carthage/Build/*.xcframework` you can bind or
  bundle; without it you only get per-platform `.framework`s.
- Commit `Cartfile.resolved` for reproducible, pinned versions.
- Carthage resolves and builds the full dependency set; expect several
  interdependent xcframeworks (bind/bundle each one you actually use plus its
  transitive dependencies).

## Credentialed / authenticated sources

Some Apple SDKs require a token to download the artifact regardless of channel (SPM, CocoaPods, or a direct URL). Mapbox, for example, needs a download token to fetch its iOS SDK.

- Inject the credential from the environment, `~/.netrc`, an SPM/CocoaPods credential mechanism, or a git-ignored `*.props` copied from a committed `*.props.template`. Never commit it and never ship it in the package.
- In CI, provide the token via a secret; do not write it into `.targets`/`.props` that end up in the NuGet.
- Keep the **build-time download token** separate from any **runtime API key** (for example a Mapbox access token set in `Info.plist` as `MBXAccessToken`). They are distinct secrets; the binding package must contain neither.

## Xcode projects and workspaces

When using `XcodeProject`:

- Ensure the scheme is shared and buildable from command line.
- Build Release configuration unless debugging native code.
- Keep deployment targets compatible with the .NET target framework.
- Use XcodeGen or checked-in project files for reproducibility.

For workspaces:

- Run package manager restore (`pod install`, SPM resolve) before `dotnet build`.
- Prefer deterministic lockfiles when available.

## Common acquisition failures

| Failure | Likely cause | Fix |
|---------|--------------|-----|
| `Framework not found` | Native asset not copied or referenced correctly | Verify `NativeReference` path and package layout. |
| `Undefined symbols for architecture arm64` | Missing slice or dependency framework | Inspect binary slices and `otool -L`; add required frameworks. |
| Works on simulator but not device | Missing device slice | Add/rebuild arm64 device variant. |
| Works on iOS but not Mac Catalyst | Catalyst slice missing | Build or acquire Catalyst-specific variant. |
| Vendor xcframework has no simulator slice (device-only) | Heavy SDKs sometimes ship arm64-device only | Pin `<RuntimeIdentifier>ios-arm64</RuntimeIdentifier>` for device testing, or request/build a simulator slice; do not expect simulator runs to work. |
| Sharpie cannot see Swift types | API is not ObjC-visible | Add Swift wrapper or use Swift direct binding generator. |
| `.xcframework` fails to load/validate after trimming | Removed slice directory but left its `AvailableLibraries` entry in `Info.plist` | Remove the matching `Info.plist` entry whenever a slice directory is removed; re-inspect before use. |

## Updating an existing Apple binding

Tie the update to the single pinned version property described above so the
update is a one-line change plus verification, not a re-discovery:

1. Check the vendor's release page/changelog for the latest version.
2. Bump the pinned version property (for example `VendorSdkVersion`).
3. Read the release notes and diff them against the APIs the wrapper/binding
   actually exposes — do not assume unrelated release notes require code
   changes.
4. Re-run the download/dependency resolution step (`DownloadFile`/`Unzip`,
   SPM resolve, or CocoaPods install) so the new artifact is verified with the
   same inspection steps as initial acquisition (slices, architectures,
   headers/modules).
5. Only update `ApiDefinition.cs`/the Swift wrapper if the exposed surface
   actually changed; summarize any breaking change for the wrapper's C#
   consumers before committing to it.
6. If the SDK also exists on Android, re-evaluate the Android dependency graph
   in parallel (`references/android-bindings.md`) — Apple and Android vendor
   releases do not always ship in lockstep.
7. Rebuild and validate from the sample/consumer app before considering the
   update complete.
