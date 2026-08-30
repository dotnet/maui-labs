# Emulator validation evidence

Artifacts produced by `maui devflow flow run` against a real Android emulator
rather than a test fake. Each subdirectory is one run, kept in chronological
order so the failing record is not overwritten by the later passing one.

Common to both runs:

| | |
|---|---|
| Command | `maui devflow flow run samples/DevFlow.Sample/maui-tests/modal-roundtrip.md --project samples/DevFlow.Sample/DevFlow.Sample.csproj --platform android --device emulator-5554 --output <out> --json` |
| Device | Android emulator, AVD `devflow-tests-api35` (API 35, x86_64, `google_apis`) |
| Flow | `samples/DevFlow.Sample/maui-tests/modal-roundtrip.md` (two taps: open modal, close modal) |

## `2026-08-14-app-build-failed`

**This run did not pass.** It is kept as an honest record of what the harness
emitted before the artifact pipeline worked on a real device.

| | |
|---|---|
| Source revision | `72e7d2b1770d35620b7701408eb43310dbcedcc3` |
| Run id | `run-20260814191908180-cc9b7bd9` |
| Outcome | `infrastructure-error` / `app-build-failed`, process exit code 1 |
| Failed stage | `resolve-artifact` (stage 5 of the lifecycle) |

Stages 1-4 (`validate-request`, `source-identity`, `load-workflow`,
`validate-target`) passed. The run never reached install, launch, agent
binding, or replay, so nothing in that directory demonstrates those stages.

The build failed because `resolve-artifact` forced `TargetFramework=net10.0-android`
down into the app's transitive `ProjectReference` graph, rewriting the shared
`project.assets.json` of the `net10.0`-only DevFlow projects and then failing
with `NETSDK1005`. The operator-visible message did not say that: the
diagnostic was truncated to a window containing only NuGet `Restored ...` lines
and no raw build log was written.

## `2026-08-15-replay-passed`

**This run passed.** All sixteen lifecycle stages completed and both flow steps
replayed against the app running on the emulator.

| | |
|---|---|
| Source revision | `7643531e05626323ffe0989d7425e93a887bd6bf` plus the working-tree fixes described below |
| Run id | `run-20260815182033097-c0728f6c` |
| Outcome | `passed` / `Flow replay passed.` |
| Exit category | `unverified` |
| Stages | 16/16 passed, including `resolve-artifact` (262.8 s), `platform-launch` (21.8 s), `agent-forward`, `validate-agent`, `execute-flow` (3.5 s), `cleanup`, `artifact-cleanup` |
| Steps | `tap ShowModalButton` passed (1525 ms), `tap CloseModalButton` passed (1591 ms) |

`exitCategory: unverified` is not a failure. The replay executed and passed; the
report declines to call it *verified* because the flow declares no independent
business oracle and one declared scenario has no complete hard-assertion
coverage (`independent-oracle-absent`, `required-scenario-uncovered`). The CLI
still exits with `ok: false` for that reason, which is deliberate.

Three defects had to be fixed before this run could get past `resolve-artifact`
and `platform-launch`:

1. A Debug Android build emits `<package>.apk` and `<package>-Signed.apk` side
   by side, and artifact resolution failed closed with `artifact-ambiguous`.
   The artifact contract now carries `SigningState` derived from the Android
   SDK's own package properties, and resolution prefers the signed package.
2. `AdbRunner.ListReversePortsAsync` keeps only the lines that begin with the
   literal `(reverse)` prefix older adb builds emitted; modern adb prints the
   ADB transport id there (`host-17 tcp:5000 tcp:5000`), so every line was
   discarded, the list always came back empty, and the reverse mapping was
   treated as failed. A local subclass parses the raw output instead.
   (Confirmed against Xamarin.Android.Tools.AndroidSdk 1.0.189-preview.58:
   `ParseReverseListOutput` takes no serial argument, returns zero rules for
   `host-17 tcp:5000 tcp:5000`, and one rule for `(reverse) tcp:5000 tcp:5000`.)
3. A Debug Android build defaults to fast deployment
   (`EmbedAssembliesIntoApk` unset), which produces a package with no managed
   assemblies. Installing it with `adb install` aborted at startup with
   `No assemblies found in '/data/user/0/<app>/files/.__override__/x86_64'`.
   The artifact contract now asks for a self-contained package.

### Redaction applied to `flow-run.json`

`execution-manifest.json` is byte-for-byte what the run produced.

`flow-run.json` is **not**: the repository root prefix
`C:\Users\<user>\...\maui-labs\` was replaced with `<repo>\` in seven
`sourceAnchor` values before committing. Those anchors are emitted as
`<absolute XAML path>:<line>`, so a passing report embeds the build machine's
absolute paths and user name. That is a real gap - the report is not
leak-free on its own - and it is why the manifest's recorded digest for
`flow-run.json`
(`sha256:7b5249ee6c2d5a9b6795e7d25602967017cf9acbb8d16c12938cf66f4ef0378a`,
43340 bytes) does not match the committed file. Nothing else was changed. The
report contains no host name, IP address, port, device serial, or credential.

The committed `flow-run.json` hashes to
`sha256:9ea66dca83edd7ee695f57824234292a3e009f7860a2b52f8331f113f68cf882`
(42843 bytes), so this directory stays self-verifying for a reader who only has
these bytes.
