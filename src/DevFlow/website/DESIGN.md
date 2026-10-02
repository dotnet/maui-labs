# MAUI DevFlow website design

## Visual Theme

**Native app loop, brought to life.** A clear, welcoming .NET MAUI-inspired system built around a polished mock device. Soft lavender surfaces carry the narrative while interactive product states provide the proof.

## Design Philosophy

Lead with immediate product understanding rather than atmosphere. The page should feel like a capable developer tool with a human touch: every visual either demonstrates the inspect-operate-diagnose-verify loop, exposes a capability, or advances setup.

## Color Palette

| Role | Value | Usage |
|------|-------|-------|
| Background | `oklch(0.985 0.008 293)` | Main reading surface |
| Surface | `oklch(0.955 0.025 293)` | Lavender functional surfaces |
| Ink | `oklch(0.16 0.055 282)` | Primary text |
| Primary | `oklch(0.49 0.215 293)` | .NET-inspired DevFlow purple |
| Primary Deep | `oklch(0.30 0.17 293)` | Code surfaces and dark sections |
| Signal | `oklch(0.64 0.22 325)` | Restrained active-state emphasis |
| Voltage | `oklch(0.84 0.095 250)` | Cool blue verification accent |
| Success | `oklch(0.69 0.17 151)` | Connected and verified states |

## Typography Rules

- Display and headings: **Archivo**, weights 500–800.
- Body and interface: **Source Sans 3**, weights 400–700.
- Commands and telemetry: **Azeret Mono**, weights 400–700.
- Hero display is fluid and capped near 5.25rem with tracking no tighter than `-0.038em`.
- Body copy stays at 1rem or larger with a maximum measure of 70 characters.

## Shape and Effects

- Functional surfaces use 1.25–2.5rem radii; primary pills use full rounding.
- Borders are full-perimeter hairlines, never decorative side stripes.
- Shadows are broad, low-opacity, and purple-tinted.
- Noise and dotted technical texture are subtle. Blur appears only during focus transitions and stacking motion.

## Layout

- Mobile-first, single-column reading order.
- Desktop sections use balanced asymmetric compositions with the product demonstration given at least half the canvas.
- Spacing follows a 4px base with tight interface groups and compact narrative transitions; the full-viewport hero and focused purple comparison provide the larger pauses.
- The hero, manifesto, capability cards, connected workflow, and setup each have a distinct composition.

## Motion

- One orchestrated hero entrance establishes the product and its live app loop.
- The mock device cycles through inspect, operate, diagnose, and verify states; its step controls remain directly interactive.
- The coding-agent terminal types one user request, visibly submits it, and keeps exactly one green AGENT status line synchronized with the active visual step. The final status becomes Complete before the demonstration fades and restarts.
- Each hero step has one causal transfer: the visual tree collapses into Inspect, a tap command travels into the native button, the failed request trace is collected into Diagnose, and a flashed screen capture shrinks into Verify.
- Capability-card hover states change elevation and border emphasis without changing text wrapping or card geometry.
- The connected workflow uses a static spatial map so the source, agent, DevFlow bridge, running app, and returned evidence remain understandable at a glance.
- All GSAP work is scoped and cleaned up; reduced motion removes scrubbed and repeating motion.

## Components

- Floating white navigation island
- Closed-loop mock device with four explicit steps, visible tap feedback, and a synchronized agent terminal
- Source-versus-runtime comparison showing how DevFlow turns a plausible code change into a verified app outcome
- Four capability cards with icon-matched feature badges
- Connected source-to-runtime workflow map with an explicit evidence return path
- Copyable setup commands
- Status-led footer
