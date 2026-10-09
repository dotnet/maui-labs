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
`Microsoft.Maui.DevFlow.Client` is the preferred portable .NET client for this
protocol. `Microsoft.Maui.DevFlow.Driver` references that package and forwards
its public types.

Do not commit a generated JSON copy of the OpenAPI document. If a consumer needs JSON, generate it from `openapi.yaml` as part of that workflow so there is only one source of truth.

The DevFlow unit tests parse `openapi.yaml` with OpenAPI tooling and validate YAML/JSON syntax plus `$ref` targets across this directory.

## WebView hosts and contract

`GET /api/v1/webview/contexts` returns `{ "webviews": [...] }` for registered CDP
standard WebView, HybridWebView and BlazorWebView bridges, not just active hosts.
Every context has a unique `webview-<index>` ID for the attachment and a single
`ready` field. `hostKind` describes the host (`webview`, `hybrid`, or `blazor`),
not the UI backend and not a behavior-dispatch switch. `active` and `elementId`
reflect native-owner correlation, including unnamed controls and duplicate
AutomationIds. Default selection prefers active hosts rather than hidden pages.

`POST /api/v1/webview/evaluate` accepts a raw CDP command such as
`{ "method": "Runtime.evaluate", "params": { "expression": "document.title", "returnByValue": true } }`.
Select its bridge with the `contextId` query parameter. All context-selecting
routes accept only canonical IDs; query values override the JSON body's
`contextId` on the typed action/DOM-query endpoints. Numeric indices,
AutomationIds, element IDs and the old `webview` query parameter are rejected.
`/webview/dom` and `/webview/source` both return HTML, not a DOM-tree
JSON object. `/webview/screenshot` returns PNG, trying the registered native
element before the CDP screenshot fallback.

Browser fetch/XHR capture is explicitly unsupported: `/api/v1/webview/network`
returns the standard 501 `webview.network` envelope. Native .NET traffic remains
available at `/api/v1/network/requests`.

Layout scope uses only `includeWebViewElements` (default `true`) for generic DOM
enrichment; `includeBlazorElements` is removed. Layout capabilities use `webview`
and the `webview-dom` feature, without the former Blazor-only duplicates. Update the agent, CLI, Client,
WebView and Blazor packages together and rebuild older consumers. There are no
WebView compatibility shims or duplicate readiness fields.

## Streaming payloads

There is no universal `{ type, timestamp, data }` wire envelope. Network and log
events use top-level `entries` or `entry`; stream errors use `error`. Profiler
and UI events use `data`. Sensor readings include `sensor`, sensor-specific
`data`, and the compatibility `reading` object. Log entries retain the compact
`t`, `l`, `c`, `m`, `e`, and `s` keys on both HTTP and WebSocket surfaces.

Client commands do not require timestamps. Network details use
`{ "type": "get_details", "id": "..." }`; UI subscriptions use
`{ "type": "subscribe", "data": { "events": ["navigation"] } }`.
The log stream supports a source filter and replay count, not a severity filter.
Profiler samples, markers, and spans arrive together as `batch` events, not
individual `samples`, `marker`, or `span` events.
Profiler DTOs retain PascalCase property names (`SessionId`, `Samples`, `TsUtc`,
and so on) inside HTTP and WebSocket payloads; the surrounding dictionary
envelopes and profiler capability keys use lower camel case.

## Extension discovery

Agents can expose app-specific diagnostics or automation under `/api/v1/ext/{namespace}/...`. Extension namespaces use reverse-domain notation such as `com.example.diagnostics`.

Extensions are discovered through `GET /api/v1/agent/capabilities`. The response includes an `extensions` object keyed by namespace. Each extension descriptor includes:

- `version`: semantic version for the extension descriptor contract
- `description`: human-readable summary
- `tools[]`: self-describing tool descriptors with `name`, `description`, `method`, `path`, optional JSON Schema `parameters`, optional JSON Schema `returns`, and optional behavior `annotations`

`GET /api/v1/agent/status` includes an `extensions` marker with `count` and `hash`. Clients can cache extension descriptors by hash and avoid fetching full capabilities when the marker has not changed.
