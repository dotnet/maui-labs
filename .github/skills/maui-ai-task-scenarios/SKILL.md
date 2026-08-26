---
name: maui-ai-task-scenarios
description: >-
  Design and implement provider-neutral, task-specific AI capabilities and validate them in
  .NET MAUI samples. USE FOR: typed AI task clients such as image classification, object
  detection, document extraction, speech, or text analysis; provider authoring; capability
  and readiness design; model provenance; deterministic fixtures; and MAUI sample validation.
  DO NOT USE FOR: generic chat or embedding adoption, AI tool bindings, broad MAUI app
  architecture, or provider-specific code that has no task-facing contract.
---

# MAUI AI Task Scenarios

Use this skill for a narrow AI job with semantic inputs and results, not for a general model
runtime. A task client is the thin, provider-neutral boundary between a MAUI app and one or more
implementations.

## Route Related Work

Use the canonical MAUI plugin skills instead of repeating their guidance:

| Need | Skill |
| --- | --- |
| Existing on-device chat or embeddings | `maui-essentials-ai` |
| Source-generated AI tool bindings | `maui-ai-tool-bindings` |
| DI, MVVM, Shell, and app service wiring | `maui-app-architecture` |
| Project files, resources, and model assets | `maui-project-structure` |
| Native APIs or platform implementations | `maui-platform-invoke` |
| Camera, media, permissions, and durable files | `maui-device-capabilities` |
| Unit and device test boundaries | `maui-unit-testing` |
| Startup, memory, trimming, and NativeAOT | `maui-performance` |
| Accessible task states and results | `maui-accessibility` |
| Remote providers or offline data | `maui-networking-offline-data` |
| Add DevFlow to a sample | `maui-devflow-onboard` |
| Inspect and validate a running sample | `maui-devflow-debug` |
| Seed deterministic runtime states | `devflow-automation` |

Use `maui-devflow-session-review` only for an opt-in review of repeated DevFlow friction.

## Design Rules

### Task boundary

- Name the user job and define typed semantic inputs, options, results, errors, and cancellation.
- Keep the task client thin. It may sit over ONNX, platform APIs, deterministic fixtures, remote
  services, or capability clients such as `IChatClient`; the contract must not privilege one.
- Keep tensors, tokenizers, model/runtime settings, platform handles, and pipeline internals behind
  the provider boundary.
- Put preprocessing, inference, decoding, pooling, batching, and resource ownership in the
  implementation. Expose a setting only when it changes task semantics across providers.
- Keep scenario projection and workflow code local to the sample until more than one scenario
  proves a reusable boundary.
- External providers must be implementable through public APIs. Do not require internal types,
  reflection into provider state, or repository-only hooks.
- Mark new provider-authoring seams `Experimental` until a second task validates the shared
  primitive. Do not generalize a pipeline from one task.

### Availability and fidelity

Define these states explicitly rather than inferring them from a successful constructor:

1. **Available**: the provider can run on the current platform, OS, architecture, and device.
2. **Ready**: required assets, permissions, runtime initialization, and warmup have completed.
3. **Capabilities**: supported input forms, limits, execution modes, result fidelity, and optional
   features are mapped to task semantics.

Return or surface an actionable reason when a state is false. If a provider cannot supply a
semantic field such as confidence, geometry, offsets, language, or ordering, represent that
absence honestly; do not synthesize plausible values.

Do not silently switch backends, route local data to cloud, or downgrade fidelity. Any fallback
must be explicit in configuration and visible to the app. Samples must show the selected provider
identity and whether execution is local, platform-backed, fixture-backed, or remote.

### Lifetime and deployment

- Accept and propagate `CancellationToken` through initialization and execution.
- Make ownership clear. Dispose sessions, native handles, streams, model runtimes, and subscriptions
  deterministically; do not dispose injected dependencies the provider does not own.
- Keep UI work off the inference path and marshal only UI state updates to the main thread.
- Treat trimming and NativeAOT as design inputs. Avoid reflection-only discovery, dynamic code
  generation, and serializers without source-generated metadata on publish paths.
- Bound allocations for media, tensors, token buffers, result collections, and streaming updates.

### Model and runtime provenance

For every checked-in or downloaded model, record:

- origin and immutable source URL;
- license and redistribution terms;
- cryptographic hash;
- format and opset/runtime compatibility;
- input and output names, shapes, data types, normalization, and label/token files;
- preprocessing and postprocessing assumptions;
- conversion/export steps and upstream version;
- supported platforms and architectures.

Do not add a model when its origin, license, or expected hash is unknown.

## Workflow

1. **Frame the task.** Write one sentence describing the user job, semantic input, semantic result,
   and required fidelity. Separate it from the sample workflow.
2. **Survey existing abstractions.** Reuse a stable public task abstraction when it matches. Use
   broad capabilities such as `IChatClient` beneath a task adapter, not as the task contract.
3. **Define capability truth.** List platform support, availability checks, readiness transitions,
   fidelity gaps, provider identity, and fallback policy before implementing the provider.
4. **Design the thinnest contract.** Include cancellation and typed task semantics. Exclude runtime
   mechanics and scenario-only projection.
5. **Implement one real provider and one deterministic fixture.** The fixture must exercise the
   public provider seam without special access to internals.
6. **Build the MAUI scenario.** Register through DI, show provider/status/fidelity in the UI, make
   unavailable and cancelled states useful, add accessible labels, and keep scenario orchestration
   in the sample.
7. **Validate each boundary.**
   - Unit tests: options, mapping, ordering, error semantics, cancellation, and disposal.
   - Provider tests: fixed inputs and expected outputs with documented numeric tolerances.
   - Device tests: platform availability, permissions, native/runtime integration, and lifecycle.
   - DevFlow/runtime tests: visible provider identity, ready/unavailable states, user flow, and
     AutomationId-based result assertions.
8. **Validate publish behavior.** When the path is trim- or AOT-sensitive, run the relevant publish
   build and exercise the task. A successful Debug build is insufficient.

## Review Checklist

- [ ] The contract describes a task, not a model runtime.
- [ ] At least two materially different providers could implement it through public APIs.
- [ ] Provider-specific tensors, tokenizers, handles, settings, and pipeline stages do not leak.
- [ ] Availability, readiness, capabilities, fidelity gaps, and provider identity are explicit.
- [ ] Local/remote selection and fallback behavior are visible and never silent.
- [ ] Cancellation, ownership, disposal, threading, allocations, trimming, and AOT are addressed.
- [ ] Model provenance includes origin, license, hash, format/opset, I/O, and conversion details.
- [ ] Fixed fixtures and tolerances make provider mapping deterministic.
- [ ] Unit, provider, device, and DevFlow/runtime responsibilities are separated.
- [ ] Scenario-only projection remains in the sample.
- [ ] New shared authoring seams are Experimental and justified by more than one task.

## Stop Signals

- Stop contract design when provider runtime concepts start appearing in app-facing methods.
- Stop generalizing when only one task or one provider needs the proposed primitive.
- Stop implementation when provider selection or fallback cannot be surfaced honestly in the UI.
- Stop model integration when origin, license, hash, opset/runtime, or I/O metadata is missing.
- Stop calling a provider supported when required semantic fidelity is absent.
- Stop unit testing at the native/runtime boundary; move that evidence to device tests.
- Stop screenshot-only validation when AutomationIds and semantic result assertions can prove the
  flow through DevFlow.
