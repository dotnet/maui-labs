---
name: maui-release-notes
description: >-
  Generate or update maintainer-oriented MAUI workload release notes. USE FOR:
  MAUI workload notes, SDK/workload set summaries, manifest/dependency tables,
  Xcode/JDK/Android SDK requirements, and dotnet-workload-info data. DO NOT USE
  FOR: app store notes, app marketing copy, general MAUI debugging, or workload
  installation troubleshooting.
---

# MAUI Release Notes

Use this skill narrowly for maintainers writing .NET MAUI workload or package
release notes. The notes must be grounded in live workload/package data, not
hardcoded guesses.

## Required Inputs

- Target .NET channel and SDK band, such as `10.0` / `10.0.100`.
- Current release SDK or workload set version.
- Previous release version for comparison when producing deltas.
- Release scope: workload manifests, MAUI NuGet packages, platform dependency
  requirements, known issues, or all of these.

If version data is missing, use `dotnet-workload-info` before drafting.

## Data Collection Workflow

1. Query live workload data with `dotnet-workload-info`:
   - latest SDK;
   - workload set CLI version;
   - workload set NuGet package/version;
   - MAUI, iOS, Android, Mac Catalyst, macOS, and other relevant manifest
     versions;
   - `WorkloadDependencies.json` contents for Xcode, JDK, Android SDK packages,
     and related requirements.
2. Query latest MAUI package versions when the notes mention out-of-band packages:
   `Microsoft.Maui.Controls`, `Microsoft.Maui.Essentials`,
   `Microsoft.Maui.Graphics`, and product-specific packages.
3. Compare previous and current versions. Separate:
   - requirement changes;
   - package/manifest version changes;
   - behavior changes;
   - known issues and mitigations;
   - validation steps.
4. Pull change summaries from authoritative repo release notes, merged PRs, or
   curated maintainer notes when available. Do not invent feature descriptions
   from version numbers alone.
5. Draft concise notes with commands and exact version tables.

## Suggested Release Note Shape

````markdown
# .NET MAUI workload release notes for <version>

## Highlights

## Install or update

```bash
dotnet workload install maui --version <workload-set-version>
dotnet workload update --version <workload-set-version>
```

## Workload and manifest versions

| Workload | Manifest version | SDK band |
| --- | --- | --- |

## Platform requirements

| Platform | Requirement | Version/range |
| --- | --- | --- |

## MAUI packages

| Package | Version |
| --- | --- |

## Breaking changes and known issues

## Validation
````

Adjust headings to the repository's existing release-note template if one exists.

## Version Rules

- Convert workload set CLI versions to NuGet package versions using the
  `dotnet-workload-info` skill's conversion rules.
- Include SDK band with manifest versions.
- Include exact NuGet or blob URLs only when useful for reproducibility.
- Prefer version ranges from `WorkloadDependencies.json` over prose guesses.
- State "not found" or "not changed" when the data source confirms it.

## Guardrails

- Do not hardcode current Xcode/JDK/Android SDK requirements from memory.
- Do not claim workload install commands were tested unless they were.
- Do not mix app release notes with workload/platform release notes.
- Do not summarize all MAUI changes as "performance improvements" without
  source-backed details.
- Keep maintainer notes actionable for SDK, CI, and workload consumers.

## Validation Checklist

- Live workload/package data was queried or provided by the user.
- Current and previous versions are clearly identified.
- Dependency requirements come from workload manifests.
- Install/update commands use the correct workload set version.
- Known issues and breaking changes are sourced or explicitly marked unknown.
