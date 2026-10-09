# DevFlow protocol spec

This directory contains the canonical DevFlow protocol contract used by the MAUI implementation in this repository.

- `openapi.yaml` defines the versioned HTTP surface under `/api/v1/*` and is the canonical OpenAPI document, including logical storage root discovery and sandboxed file management. The current shared implementation advertises only the `appData` root.
- `asyncapi.yaml` defines the streaming channels under `/ws/v1/*`
- `schemas/` contains the shared payload models
- `examples/` contains representative request and response payloads, including platform job listing and run requests

These spec files are intended to stay framework-agnostic so the same DevFlow contract can be implemented across MAUI and other UI stacks.

## API versions

The current Agent API is versioned under `/api/v1/*`, and its WebSocket channels
are versioned under `/ws/v1/*`. The unversioned `/api/*` and `/ws/*` endpoints
documented in the original `Redth/MauiDevFlow` repository predate this contract
and are not compatibility aliases in the maui-labs implementation.

The agent listens on IPv4 loopback. The broker normally discovers its dynamic
port through its separate port 19223. Direct clients should use the configured
or discovered agent port. Port 9223 is only the agent's last-resort default when
no configured or broker-assigned port is available. The typed `AgentClient` in
`Microsoft.Maui.DevFlow.Driver` is the preferred .NET client for this protocol.

Do not commit a generated JSON copy of the OpenAPI document. If a consumer needs JSON, generate it from `openapi.yaml` as part of that workflow so there is only one source of truth.

The DevFlow unit tests parse `openapi.yaml` with OpenAPI tooling and validate YAML/JSON syntax plus `$ref` targets across this directory.

## WebView hosts

`/api/v1/webview/*` operates on registered standard WebView, HybridWebView and
BlazorWebView hosts. Owner-aware contexts report stable `webview-<index>` IDs,
`hostKind`, their current `elementId` correlation, readiness, and active state.
Use the reported context ID for unambiguous selection; UI element IDs,
AutomationIds and numeric indices remain aliases. Default resolution follows
active hosts, not hidden retained pages. Hosts without AutomationIds are supported.

`/api/v1/webview/screenshot` is native-first and is used by CLI/MCP. Browser-network
capture is unsupported and `/api/v1/webview/network` returns the standard 501
`webview.network` envelope. Native .NET HTTP requests still use
`/api/v1/network/requests`.

Layout requests can set `scope.includeWebViewElements` for generic DOM enrichment.
If omitted, `includeBlazorElements` retains its legacy default/alias behavior.

## Extension discovery

Agents can expose app-specific diagnostics or automation under `/api/v1/ext/{namespace}/...`. Extension namespaces use reverse-domain notation such as `com.example.diagnostics`.

Extensions are discovered through `GET /api/v1/agent/capabilities`. The response includes an `extensions` object keyed by namespace. Each extension descriptor includes:

- `version`: semantic version for the extension descriptor contract
- `description`: human-readable summary
- `tools[]`: self-describing tool descriptors with `name`, `description`, `method`, `path`, optional JSON Schema `parameters`, optional JSON Schema `returns`, and optional behavior `annotations`

`GET /api/v1/agent/status` includes an `extensions` marker with `count` and `hash`. Clients can cache extension descriptors by hash and avoid fetching full capabilities when the marker has not changed.
