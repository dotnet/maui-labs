---
name: android-slim-bindings
description: >-
  Deprecated compatibility redirect for the old Android slim binding skill.
  USE FOR: only when the user explicitly invokes android-slim-bindings or asks
  for the legacy Android slim binding skill by name. DO NOT USE FOR: binding
  implementation, troubleshooting, strategy, packaging, or updates; use
  native-library-bindings instead. INVOKES: native-library-bindings.
---

# Android Slim Bindings Redirect

This compatibility shim preserves the old skill name. Do not implement Android
slim bindings from this file.

Use **native-library-bindings** instead. That skill now owns Android slim/NLI
bindings, traditional Android bindings, acquisition, dependency resolution,
packaging, and upstream SDK update workflows.
