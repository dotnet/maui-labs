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

## Existing WebView contract

`GET /api/v1/webview/contexts` returns `{ "webviews": [...] }` for registered CDP
bridges, not just active hosts. Context `id` values use the registration's
AutomationId, then elementId, then decimal index; they are not a separate stable
ID namespace. The index can select a registration directly. `ready` and `isReady`
are aliases; `active` reflects the backend's active AutomationIds, not generic
host-owner correlation.

`POST /api/v1/webview/evaluate` accepts a raw CDP command such as
`{ "method": "Runtime.evaluate", "params": { "expression": "document.title", "returnByValue": true } }`.
Select its bridge with the `webview` query parameter. Other context-selecting
WebView endpoints also accept `contextId`; query values override the JSON body's
`contextId`. `/webview/dom` and `/webview/source` both return HTML, not a DOM-tree
JSON object. `/webview/screenshot` returns PNG, trying the registered native
element before the CDP screenshot fallback.

`/api/v1/webview/network` is an existing HTTP 200 alias of
`/api/v1/network/requests`: it reads the native .NET HTTP capture store, not browser
fetch/XHR traffic. It does not associate requests with a WebView context.

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
