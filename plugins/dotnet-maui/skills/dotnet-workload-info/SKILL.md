---
name: dotnet-workload-info
description: >-
  Discover MAUI workload metadata from live NuGet APIs. USE FOR: workload manifest lookup, WorkloadDependencies.json, v3-flatcontainer, CLI-to-NuGet version conversion, Xcode/JDK/Android SDK requirements, CI sdkmanager packages, `packageid:Microsoft.Maui.Controls`. DO NOT USE FOR: installing workloads, build debugging, or app version edits.
---

# .NET Workload Info Discovery

Query live NuGet APIs to discover authoritative .NET SDK versions, workload sets, and dependency requirements.

## Inputs

| Parameter | Required | Example | Notes |
|-----------|----------|---------|-------|
| dotnetVersion | yes | 9.0, 10.0 | Major.minor format |
| prerelease | no | true/false | Default false |
| workload | no | ios, android, maui | Alias or full id |

## Workload Aliases

| Alias | Full ID |
|-------|---------|
| ios | microsoft.net.sdk.ios |
| android | microsoft.net.sdk.android |
| maccatalyst | microsoft.net.sdk.maccatalyst |
| macos | microsoft.net.sdk.macos |
| tvos | microsoft.net.sdk.tvos |
| maui | microsoft.net.sdk.maui |

---

## Discovery Process

### Step 1: Get Latest SDK Version

```bash
curl -s "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json" | \
  jq '.["releases-index"][] | select(.["channel-version"]=="{MAJOR}.0")'
```

Extract `latest-sdk` and derive SDK band:
- `10.0.103` → band `10.0.100` (hundreds digit)
- `10.0.205` → band `10.0.200`

### Step 2: Find Workload Set Package / Version

Use the `dotnet workload search version` command to discover the latest workload set version (requires .NET SDK 9.0.200+ with workload sets support):

```bash
dotnet workload search version
# Returns: workload set version, e.g. 10.0.103
```

```powershell
dotnet workload search version
```

> **Note**: This command requires .NET SDK 9.0.200 or later. If unavailable, use the NuGet API instead:
> `https://api.nuget.org/v3/registration5-gz-semver2/microsoft.net.workloads.10.0.100/index.json`

The returned workloadVersion is the CLI version to use with --version flag.

**Version conversion**:
To convert this to the NuGet package version (needed for Steps 3-4):

CLI 10.0.103 → NuGet 10.103.0 (remove middle .0., combine)
Do not use the CLI version as the NuGet package version in flat-container URLs.
The NuGet package is `Microsoft.NET.Workloads.{band}` where `{band}` is the SDK band derived from the CLI version (for example, CLI 10.0.103 → package `Microsoft.NET.Workloads.10.0.100`, NuGet version `10.103.0`).


### Step 3: Download Workload Set Manifest

```bash
curl -o workloadset.nupkg "https://api.nuget.org/v3-flatcontainer/microsoft.net.workloads.{band}/{version}/microsoft.net.workloads.{band}.{version}.nupkg"
unzip -p workloadset.nupkg data/microsoft.net.workloads.workloadset.json
```

Format: `"{workload_id}": "{manifestVersion}/{sdkBand}"`

### Step 4: Download Workload Manifest

Build package id: `{WorkloadId}.Manifest-{sdkBand}` (e.g., `Microsoft.NET.Sdk.iOS.Manifest-10.0.100`)
Use the same SDK band discovered in Step 1 or extracted from the workload set entry. Do not hardcode `10.0.100` when the current SDK band is `10.0.200`, `10.0.300`, or another band.

```bash
curl -o manifest.nupkg "https://api.nuget.org/v3-flatcontainer/{packageid}/{version}/{packageid}.{version}.nupkg"
unzip -p manifest.nupkg data/WorkloadDependencies.json
```

### Step 5: Parse Dependencies

**Android** (`microsoft.net.sdk.android`):
```json
{
  "jdk": { "version": "[17.0,22.0)", "recommendedVersion": "17.0.14" },
  "androidsdk": {
    "packages": ["build-tools;35.0.0", "platform-tools", "platforms;android-35"],
    "apiLevel": "35", "buildToolsVersion": "35.0.0"
  }
}
```

After extracting `androidsdk.packages`, show how to install them in CI:

```bash
sdkmanager --install "platform-tools" "platforms;android-35" "build-tools;35.0.0"
```

Use the exact package IDs from `WorkloadDependencies.json`; do not copy the sample
API level or build-tools version blindly.

When the user asks for CI-ready Android SDK package discovery, include this
minimum flow in the answer:

1. Download `Microsoft.NET.Workloads.{sdkBand}` and read
   `data/microsoft.net.workloads.workloadset.json` (`workloadset.json`).
2. Read the `microsoft.net.sdk.android` entry to get
   `{manifestVersion}/{manifestSdkBand}`.
3. Download `Microsoft.NET.Sdk.Android.Manifest-{manifestSdkBand}` at that
   manifest version and read `data/WorkloadDependencies.json`.
4. Extract `androidsdk.packages`, including `platform-tools`,
   `platforms;android-*`, `build-tools;*`, and `cmdline-tools;*`. Filter by
   `"optional": "false"` when the manifest marks packages optional.
5. Feed the extracted package IDs to `sdkmanager --install`.

**iOS/macOS** (`microsoft.net.sdk.ios`):
```json
{
  "xcode": { "version": "[26.2,)", "recommendedVersion": "26.2" },
  "sdk": { "version": "26.2" }
}
```

**Version ranges**: `[17.0,22.0)` = >=17.0 AND <22.0; `[26.2,)` = >=26.2

---

## MAUI NuGet Packages

MAUI packages may be newer than workload versions. Query for latest:

```bash
curl -s "https://azuresearch-usnc.nuget.org/query?q=packageid:Microsoft.Maui.Controls&prerelease=false" | \
  jq '.data[0].versions | map(select(.version | startswith("{MAJOR}."))) | last'
```

When the user asks for the NuGet search API or an exact package ID filter, use
`https://azuresearch-usnc.nuget.org/query?q=packageid:<PackageId>` and show the
`packageid:` filter. Do not answer with only the flat-container or registration
endpoints; those are useful follow-up APIs but they are not the search API.

Key packages: `Microsoft.Maui.Controls`, `Microsoft.Maui.Essentials`, `Microsoft.Maui.Graphics`

To use newer version than workload:
```xml
<PackageReference Include="Microsoft.Maui.Controls" Version="10.0.30" />
```

---

## Output Format

```json
{
  "dotnetVersion": "10.0",
  "latestSdk": "10.0.102",
  "sdkBand": "10.0.100",
  "workloadSet": { "packageId": "...", "nugetVersion": "10.102.0", "cliVersion": "10.0.102" },
  "workloads": [{
    "workloadId": "microsoft.net.sdk.ios",
    "manifestVersion": "26.2.10191",
    "sdkBand": "10.0.100",
    "dependencies": { "xcode": { "versionRange": "[26.2,)", "recommendedVersion": "26.2" } }
  }],
  "mauiNugets": [{ "packageId": "Microsoft.Maui.Controls", "latestVersion": "10.0.30" }]
}
```

---

## Error Handling

- No results → retry with `prerelease=true`
- Missing WorkloadDependencies.json → report explicitly
- Missing dependency key → note which keys absent

## Best Practices

- ALWAYS fetch live data; never hardcode versions
- ALWAYS include sdkBand with manifest versions
- Show exact URLs used for transparency

## Reference

See `references/workload-discovery-process.md` for detailed NuGet API documentation and complete examples.
