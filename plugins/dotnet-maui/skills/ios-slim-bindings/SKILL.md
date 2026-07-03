---
name: ios-slim-bindings
description: >-
  Deprecated compatibility redirect for the old iOS slim binding skill.
  USE FOR: only when the user explicitly invokes ios-slim-bindings or asks for
  the legacy iOS slim binding skill by name. DO NOT USE FOR: binding
  implementation, troubleshooting, strategy, packaging, or updates; use
  native-library-bindings instead. INVOKES: native-library-bindings.
---

# iOS Slim Bindings Redirect

This compatibility shim preserves the old skill name. Do not implement iOS or
Mac Catalyst slim bindings from this file.

Use **native-library-bindings** instead. That skill now owns Apple slim/NLI
bindings, traditional Apple bindings, acquisition, Objective Sharpie cleanup,
packaging, and upstream SDK update workflows.
