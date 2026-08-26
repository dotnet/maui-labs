---
applyTo: "src/AI/**,src/AIExtensions/**,tests/AI/**,tests/AIExtensions/**,samples/*AI*/**,samples/*Classification*/**,samples/*Detection*/**,samples/*Extraction*/**,samples/*Recognition*/**,samples/*Speech*/**,samples/*TextAnalysis*/**"
---

# AI Libraries, Providers, and Samples

For task-specific AI work, load the `maui-ai-task-scenarios` project skill and use the canonical
`dotnet-maui-app` skills it routes to for app architecture, platform capabilities, testing,
performance, accessibility, networking, and DevFlow/runtime validation.

- Keep app-facing task contracts provider-neutral and semantic. Provider implementations own
  tensors, tokenizers, native handles, model/runtime settings, and preprocessing/postprocessing.
- Keep sample projection and workflow local until multiple scenarios prove a reusable boundary.
- Make availability, readiness, capabilities, fidelity gaps, provider identity, and fallback policy
  explicit. Never silently switch providers, use cloud, or invent unsupported result fidelity.
- External providers must use public APIs only. Keep new shared provider-authoring seams
  Experimental until a second task validates them.
- Propagate cancellation, define disposal ownership, and preserve trimming/NativeAOT compatibility.
- Record model origin, license, hash, format/opset, I/O contract, conversion steps, and provenance.
- Use fixed fixtures and documented tolerances; separate unit, provider, device, and
  DevFlow/runtime tests.
