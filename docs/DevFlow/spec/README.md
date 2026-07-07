# DevFlow protocol spec

This directory contains the canonical DevFlow protocol contract used by the MAUI implementation in this repository.

- `openapi.yaml` defines the versioned HTTP surface under `/api/v1/*` and is the canonical OpenAPI document, including logical storage root discovery and sandboxed file management. The current shared implementation advertises only the `appData` root.
- `asyncapi.yaml` defines the streaming channels under `/ws/v1/*`
- `schemas/` contains the shared payload models
- `examples/` contains representative request and response payloads, including platform job listing and run requests

These spec files are intended to stay framework-agnostic so the same DevFlow contract can be implemented across MAUI and other UI stacks.

Do not commit a generated JSON copy of the OpenAPI document. If a consumer needs JSON, generate it from `openapi.yaml` as part of that workflow so there is only one source of truth.

The DevFlow unit tests parse `openapi.yaml` with OpenAPI tooling and validate YAML/JSON syntax plus `$ref` targets across this directory.

## Extension discovery

Agents can expose app-specific diagnostics or automation under `/api/v1/ext/{namespace}/...`. Extension namespaces use reverse-domain notation such as `com.example.diagnostics`.

Extensions are discovered through `GET /api/v1/agent/capabilities`. The response includes an `extensions` object keyed by namespace. Each extension descriptor includes:

- `version`: semantic version for the extension descriptor contract
- `description`: human-readable summary
- `tools[]`: self-describing tool descriptors with `name`, `description`, `method`, `path`, optional JSON Schema `parameters`, optional JSON Schema `returns`, and optional behavior `annotations`

`GET /api/v1/agent/status` includes an `extensions` marker with `count` and `hash`. Clients can cache extension descriptors by hash and avoid fetching full capabilities when the marker has not changed.
