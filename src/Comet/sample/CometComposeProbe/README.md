# Android sample payloads

`CometSample=baristanotes` builds the standalone BaristaNotes app in Debug and
Release. `StandaloneBarista.targets` excludes the other sample screens, their
drawables, Jetcaster feeds/art, and unused fonts. It does not depend on a
comparison or benchmark property.

The exact Barista fonts are Manrope Regular, Manrope Semibold, Material Symbols,
and coffee-icons. The `MaterialIcons` alias uses `material_symbols.ttf`, not
`material_icons.ttf`. The native host still uses `JetchatTheme.cs` as its shared
color generator. No colors or font outlines are changed.

The launcher icon, FileProvider XML, Android library resources, and enabled
comparison fixtures remain included. Barista photos are files in app storage;
missing photos use the existing person glyph, not probe placeholder art.
Keep future Barista assets outside the probe-only `Resources/drawable` folder,
or add them to the standalone include policy with dependency tests.

The plain probe and other standalone sample variants retain their original
source and payload sets, including screen-intent selection and the Jetchat
hot-reload demonstration. A Barista-only build rejects other screen intents
with the existing log-and-finish path; it does not fall back to a missing screen.

## Inset regression tests

Run from `src/Comet`. Set `DOTNET` to the selected SDK command or pinned wrapper,
and `HOST_PROPS` / `HOST_TARGETS` to the isolated managed-host build overrides.
The overrides must redirect outputs and replace the test project's Comet
HintPath with a project reference to the current Comet sources, built for
`net11.0`. Do not use an old Comet DLL.

```sh
"$DOTNET" test "$PWD/tests/Comet.Tests/Comet.Tests.csproj" \
  -p:BaristaInsetTests=true -p:InsetPlatform=Portable \
  -p:DirectoryBuildPropsPath="$HOST_PROPS" \
  -p:DirectoryBuildTargetsPath="$HOST_TARGETS" \
  --filter FullyQualifiedName~BaristaEditorInsetDependencyTests
```

The 14 host cases cover editor geometry, edge notifications, batching, people
picker/shared bottom-inset consumers, setting changes, owner changes and
disposal. Portable mode retains the top inset; the separate Android-branch
host helper tests Android edge filtering. These are not native rendering,
keyboard, device, or startup-time checks. Run `BaristaLifecycleTests=true`
in a separate process; the two modes cannot share comparison storage.

## Android ART baseline profiles

Release R8 builds now include the vendored Compose library baseline profile.
This also applies to standalone BaristaNotes and its NativeAOT comparison
profiles. It changes the generated artifact; do not attach a new build to an
old timing result.

From `src/Comet`, build an APK without changing installed apps:

```sh
dotnet publish sample/CometComposeProbe/CometComposeProbe.csproj \
  -f net11.0-android -c Release \
  -p:CometSample=baristanotes -p:EnableProbeAot=true \
  -p:AndroidPackageFormat=apk -p:AndroidPackageFormats=apk
python3 tools/check_compose_art_profile.py path/to/BaristaNotes-Signed.apk
```

For an unprofiled control, repeat with
`-p:MicrosoftAndroidXComposeEnableBaselineProfile=false`, then check that
artifact with `--expect-absent`. Keep both APKs and their SHA-256 values.
The opt-out does not disable the JNI/R8 correctness rules.

The build requires the Android SDK command-line tools' `profgen-classpath.jar`.
Override its path with `MicrosoftAndroidXComposeProfgenClasspath` if needed.
Profile generation uses the final DEX and a strict `profgen` readback; the archive
checker checks packaging, not ART compilation on a device.

For any later sideloaded performance experiment, record the actual ART state.
Jonathan's [Pixel 5 experiment](https://github.com/jonathanpeppers/Microsoft.AndroidX.Compose/issues/346#issuecomment-5779434779)
used a checksum-matched install-time `.dm` with explicit `v0_1_5_s` format.
The earlier default/v010 sidecar remained `verify/install-dm` and did not test
applied compilation. Confirm `speed-profile/install-dm` for the intended
profiled state, and recheck after each measured phase. This sidecar correction
does not change the format of the packaged `baseline.prof` asset.

Do not uninstall, clear app data, reset ART, or overwrite existing comparison
apps to prepare a test. Device experiments require separate approval and an
isolated app identity. No startup or transition improvement is claimed by this
build-support update.
