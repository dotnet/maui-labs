# DevFlow protocol spec

This directory contains the canonical DevFlow protocol contract used by the MAUI implementation in this repository.

- `openapi.yaml` defines the versioned HTTP surface under `/api/v1/*`
- `asyncapi.yaml` defines the streaming channels under `/ws/v1/*`
- `schemas/` contains the shared payload models
- `examples/` contains representative request and response payloads

These spec files are intended to stay framework-agnostic so the same DevFlow contract can be implemented across MAUI and other UI stacks.
