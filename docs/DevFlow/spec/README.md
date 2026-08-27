# DevFlow protocol spec

This directory contains the canonical DevFlow protocol contract used by the MAUI implementation in this repository.

- `openapi.yaml` defines the versioned HTTP surface under `/api/v1/*` and is the canonical OpenAPI document, including logical storage root discovery and sandboxed file management. The current shared implementation advertises only the `appData` root.
- `asyncapi.yaml` defines the streaming channels under `/ws/v1/*`
- `schemas/` contains the shared payload models
- `examples/` contains representative request and response payloads, including platform job listing and run requests

These spec files are intended to stay framework-agnostic so the same DevFlow contract can be implemented across MAUI and other UI stacks.

Do not commit a generated JSON copy of the OpenAPI document. If a consumer needs JSON, generate it from `openapi.yaml` as part of that workflow so there is only one source of truth.

The DevFlow unit tests parse `openapi.yaml` with OpenAPI tooling and validate YAML/JSON syntax plus `$ref` targets across this directory.

## Platform identity

`GET /api/v1/agent/status` reports `device.platform`, and the same value is sent in the broker registration. The canonical identifiers are:

| Identifier | Display name | Typically reported by an agent as |
|---|---|---|
| `android` | Android | `Android` |
| `ios` | iOS | `iOS` |
| `maccatalyst` | Mac Catalyst | `MacCatalyst` |
| `windows` | Windows | `WinUI`, `Windows`, `WPF` |
| `linux` | Linux | `Linux` |
| `macos` | macOS | `macOS` |
| `tizen` | Tizen | `Tizen` |

Rules that make this contract safe to extend:

- **Agents report their native spelling.** Nothing on the wire changed when the canonical identifiers were introduced; an agent that has always sent `"MacCatalyst"` keeps sending it.
- **Clients normalize before comparing.** `Microsoft.Maui.DevFlow.Driver.DevFlowPlatform.Normalize` in the `Microsoft.Maui.DevFlow.Client` package maps casing and known aliases (`winui` → `windows`, `gtk` → `linux`, `catalyst` → `maccatalyst`, `tizen-nui` → `tizen`) onto the canonical identifier. `AgentStatus.PlatformId` exposes the normalized value.
- **Unknown identifiers pass through.** An identifier DevFlow does not recognize is lowercased and returned unchanged rather than rejected or coerced onto another platform, so a newer or out-of-tree agent stays usable with an older CLI. `DevFlowPlatform.IsKnown` distinguishes the two cases. The wire schema deliberately accepts any non-empty platform string; the table above documents the identities current clients recognize, not a closed protocol enum.
- **Filters compare canonical identity.** When a requested filter resolves to a platform DevFlow knows, canonical equality is authoritative and nothing else is considered — an `ios` filter must not match `KaiOS`, and a `linux` filter must never match an agent reporting `Tizen (Linux) 8.0`. A substring fallback applies only when the filter is unrecognized, so partial filters such as `tiz` keep working.
- **A canonical identifier does not imply a host-side driver.** `AppDriverFactory.HasLocalDriver` reports whether this repository can launch, theme, record or drive alerts for a platform from the host. Tizen agents are fully usable over the HTTP protocol; only those host-side helpers are unavailable, and they fail with an explicit message instead of falling through to another platform's driver.

### External agents

An agent that lives outside this repository — for example the Tizen backend in [Redth/Maui.Tizen](https://github.com/Redth/Maui.Tizen) — reuses `Microsoft.Maui.DevFlow.Agent.Abstractions` and reports its platform in one of three ways, in precedence order:

1. Override `DevFlowAgentService.PlatformName`. MAUI backends get this for free: `Microsoft.Maui.DevFlow.Agent.Core` returns `DeviceInfo.Current.Platform.ToString()`, which is already `Tizen` on Tizen.
2. Set the `DEVFLOW_PLATFORM` environment variable. This wins over detection — even successful detection, since the failure it addresses is a confidently *wrong* answer rather than detection giving up — and covers the framework-neutral paths (broker registration, the runtime profiler) that run before a backend exists. The value is reported verbatim, so it is validated first: up to 32 characters of letters, digits, `.`, `-`, `_` or spaces. Anything else is ignored in favour of detection.
3. Rely on `DevFlowRuntimePlatform.DetectName()`. It probes the `TIZEN` OS platform, the runtime identifier, `/etc/tizen-release` and the `Tizen.Applications.Common` assembly, and it tests Tizen **before** Linux because Tizen is a Linux distribution and would otherwise report `Linux`.

**Minimum package version.** Tizen identity landed in `Microsoft.Maui.DevFlow.*` **0.1.0-preview.13**. `Maui.Tizen` should reference at least that version of `Microsoft.Maui.DevFlow.Agent.Abstractions` (and `Microsoft.Maui.DevFlow.Client` for consumers of `DevFlowPlatform`); with it, no platform spoofing and no local platform-name shim is needed on the Tizen side.

## Extension discovery

Agents can expose app-specific diagnostics or automation under `/api/v1/ext/{namespace}/...`. Extension namespaces use reverse-domain notation such as `com.example.diagnostics`.

Extensions are discovered through `GET /api/v1/agent/capabilities`. The response includes an `extensions` object keyed by namespace. Each extension descriptor includes:

- `version`: semantic version for the extension descriptor contract
- `description`: human-readable summary
- `tools[]`: self-describing tool descriptors with `name`, `description`, `method`, `path`, optional JSON Schema `parameters`, optional JSON Schema `returns`, and optional behavior `annotations`

`GET /api/v1/agent/status` includes an `extensions` marker with `count` and `hash`. Clients can cache extension descriptors by hash and avoid fetching full capabilities when the marker has not changed.
