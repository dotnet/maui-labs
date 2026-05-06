# Comet Documentation

## For AI Agents

Start with [AGENTS.md](../AGENTS.md) for a complete framework reference.
For specific topics: [controls](controls.md) | [state](reactive-state-guide.md) | [layout](layout.md) | [navigation](navigation.md) | [troubleshooting](troubleshooting.md)

---

Documentation for the Comet MVU framework -- a declarative, code-only UI layer
built on .NET MAUI. Views are defined as C# functions, state changes
automatically trigger re-rendering, and platform-native controls are used
throughout.


## Guides

These are the primary docs for developers building with Comet.

- [Getting Started](getting-started.md) -- Zero to a running Comet app.
  Prerequisites, project setup, build commands, and a walkthrough of the counter
  example.
- [Control Catalog](controls.md) -- Every control in Comet: constructors,
  properties, code examples, and the fluent API pattern.
- [Layout System](layout.md) -- VStack, HStack, ZStack, Grid, FlexLayout,
  spacing, padding, margin, alignment, and responsive patterns.
- [Reactive State Guide](reactive-state-guide.md) -- Practical, code-forward
  guide to every state management pattern: `Reactive<T>`, `Signal<T>`,
  `Component<TState>`, and automatic dependency tracking.
- [Migration Guide](migration-guide.md) -- How to move from the prior Comet API
  surface to the evolved MVU API without renaming the project.
- [Testing Guide](testing.md) -- Test infrastructure, reactive state testing,
  view tree verification, hot reload tests, and build/run commands.
- [Accessibility Guide](accessibility.md) -- Screen reader support, semantic
  properties, automation IDs, keyboard navigation, and platform bridging.
- [Troubleshooting and FAQ](troubleshooting.md) -- Common issues with reactive
  state, hot reload, builds, and debugging tips.
- [Changelog](changelog.md) -- Release notes for the current development cycle.
- [Performance Optimization](performance.md) -- Body rebuild cost, fine-grained
  vs. body-level updates, Signal.Peek(), SetState batching, SignalList, diff
  algorithm, and common anti-patterns.
- [Styling and Theming](styling.md) -- Design tokens, ControlStyle, built-in
  button styles, ViewModifiers, theme switching, and scoped overrides.
- [Form Handling and Validation](forms.md) -- Form controls, two-way binding,
  validation patterns, error display, multi-step forms, and callback reference.
- [Contributing](contributing.md) -- Development setup, build order, code style,
  source generator internals, testing, and PR process.
- [Navigation Guide](navigation.md) -- Stack navigation, tab navigation, Shell
  routes, modal presentation, data passing, and adaptive layouts.
- [Platform-Specific Guides](platform-guides.md) -- Platform file conventions,
  conditional compilation, handler architecture, and per-platform details for
  iOS, Android, Mac Catalyst, and Windows.
- [MAUI Integration Guide](maui-interop.md) -- Embedding MAUI views in Comet,
  Comet views in MAUI, native platform views, and DI service access.
- [Animations and Gestures](animations.md) -- Property animations, spring
  physics, keyframe animations, animation sequences, and gesture recognizers.


## Architecture and Design

Design proposals, ADRs, and technical analysis that informed implementation
decisions. Useful for contributors and anyone working on the framework internals.

- [Architecture Overview](architecture.md) -- Key layers, source generator
  pipeline, reactive system, diff algorithm, hot reload, and build system.
- [Handler Architecture](handlers.md) -- How Comet views map to MAUI handlers,
  property mappers, customization patterns, and creating custom handlers.
- [ADR: Dual Reactive State Tracking](architecture/adr-dual-tracking-systems.md)
  -- Decision record for maintaining both classic and signal-based tracking.
- [State Management v2 Proposal](architecture/state-management-proposal.md) --
  Engineering design for the next-generation state system (Rev 4).
- [State Tracking Unification Analysis](architecture/state-unification-analysis.md)
  -- Technical analysis of unifying the two tracking systems.
- [Source Generator Design (Phase 2)](architecture/phase2-generator-design.md) --
  Template design for the Roslyn source generator under state unification.
- [Style and Theme Spec](architecture/STYLE_THEME_SPEC.md) -- Technical
  specification for `ControlStyle<T>`, `ViewModifier`, and theme propagation.
- [Slider Drag Bug RCA](architecture/slider-drag-bug-rca.md) -- Root cause
  analysis of the slider stuck-drag issue and the fix.


## Research and Comparison

Background research, multi-model analysis, and independent reviews that fed into
design decisions. Reference material, not prescriptive.

- [Comet vs MauiReactor Comparison](research/state-management-comparison.md) --
  Side-by-side analysis of both frameworks' state management systems.
- [State Management Deep Dive (GPT)](research/state-management-gpt.md) --
  Comprehensive GPT-generated analysis of the Comet state system.
- [State Management Deep Dive (Opus)](research/state-management-opus.md) --
  Independent Opus-generated analysis of the same system.
- [State Management Fact-Check](research/state-management-factcheck.md) --
  Line-by-line verification of code blocks and claims in the state docs.
- [Style/Theme API Comparison](research/STYLE_THEME_COMPARISON.md) -- Comet vs
  MauiReactor vs SwiftUI styling and theming APIs.

### Proposal Reviews

Independent reviews of the state management v2 proposal across multiple rounds:

- [Architect Review](research/state-management-proposal-architect-review.md)
- [Skeptic Review (Round 1)](research/state-management-proposal-skeptic-review.md)
- [Skeptic Review (Round 2)](research/state-management-proposal-skeptic-review-r2.md)
- [Skeptic Review (Round 3)](research/state-management-proposal-skeptic-review-r3.md)

### Style/Theme Spec Reviews

- [GPT-5.4 Review](research/reviews/SPEC_REVIEW_GPT54.md)
- [Gemini Review](research/reviews/SPEC_REVIEW_GEMINI.md)
- [GPT-5.4 Final Review](research/reviews/SPEC_FINAL_REVIEW_GPT54.md)
- [Review Response (Holden)](research/reviews/REVIEW_RESPONSE.md)



