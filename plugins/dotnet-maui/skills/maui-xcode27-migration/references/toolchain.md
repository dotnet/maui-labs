# Workload and CI evidence

Scene adoption and build-tool compatibility are separate checks. A matching
Xcode does not add a missing scene manifest; changing Xcode cannot substitute
for adopting scenes.

## Identify the selected workload set

Run from the app's SDK-selection directory:

```sh
dotnet --info
dotnet workload --version
dotnet workload --info
dotnet workload list
xcodebuild -version
```

`--version` identifies the current workload set; `--info` supplies installed
manifest versions, paths and installation sources. `list` gives installed
workload IDs, not enough on its own to establish the set that CI will use.
Inspect `global.json` SDK/workload pins and CI restore/install arguments too.
Check `dotnet workload config --update-mode` without supplying a new value:
loose-manifest mode can resolve different individual versions, so do not
mistake a reported set version for proof that every installed manifest matches it.

When evaluating an approved toolchain upgrade, distinguish a candidate from
the active set. On SDKs that support it, `dotnet workload search version
<candidate-workload-version>` inspects a specific candidate; use that SDK's
`--help` for version discovery options rather than assuming all SDKs expose
the same flags. Query only permitted feeds. Discovery is not permission to
install or update anything.

Trace the selected set to its iOS/Catalyst manifest and pack versions using the
installed metadata or the exact released workload-set package. Match these to
the Apple release's documented Xcode requirements and actual build validation.
If dependency metadata gives minimum/recommended Xcode versions, confirm their
meaning against the release notes; do not manufacture a supported range from
two values or treat a recommendation as a compatibility guarantee.
Report unresolved evidence rather than guessing package paths or versions.
Never infer the Apple pack/Xcode pairing solely from the .NET major version.

## Reproduce the pairing in CI

Pin the SDK, workload set and Xcode consistently, using the repository's
established mechanism. Verify what CI actually restores rather than only what
the developer machine has installed. If provisioning is authorized, restore
workloads after selecting the SDK and intended set; then build affected TFMs
with the existing release/signing settings.

Check the selected runner image's current software inventory: an OS label
does not prove it includes a particular Xcode. `setup-xcode` selects an
installed Xcode; it does not install a newer one. GitHub's September 2026
Xcode 27 image announcement describes an arm64-only public preview; recheck
current labels, availability and software before editing a workflow.
Keep host architecture separate from the app's `RuntimeIdentifier`. An arm64
runner does not authorize making a Catalyst app arm64-only or dropping Intel
support; verify any existing cross-architecture build instead.

## Sources

- [dotnet workload command](https://learn.microsoft.com/dotnet/core/tools/dotnet-workload)
- [dotnet workload search](https://learn.microsoft.com/dotnet/core/tools/dotnet-workload-search)
- [Workload sets](https://learn.microsoft.com/dotnet/core/tools/dotnet-workload-sets)
- [Xcode 27 runner announcement](https://github.blog/changelog/2026-09-10-xcode-27-runner-image-now-runs-on-macos-27/)
- [Runner image inventories](https://github.com/actions/runner-images)
- [setup-xcode](https://github.com/maxim-lobanov/setup-xcode)
