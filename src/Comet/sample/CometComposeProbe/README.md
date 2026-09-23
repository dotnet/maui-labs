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
