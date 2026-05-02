---
updated_at: 2026-04-03T02:52:02.281Z
focus_area: "CometBaristaNotes complete rewrite — matching original BaristaNotes layout/feature/visual parity using Comet theme system"
active_issues: []
---

# What We're Focused On

## Current Mission

Complete rewrite of `sample/CometBaristaNotes` to match the original `~/work/BaristaNotes` MauiReactor app:
- **Layout parity** — same screens, same structure, same flow
- **Feature parity** — all CRUD, filtering, AI advice, voice commands
- **Visual parity** — same coffee-themed light/dark palette
- **Comet idiom** — use Token/Theme/ControlStyle/ViewModifier system instead of inline styling

## Plan

See session plan.md for full 7-phase, 39-todo implementation plan.

## Key References

- **Original app:** `~/work/BaristaNotes/BaristaNotes/BaristaNotes/`
- **Reference screenshots:** `~/Downloads/baristanotes-screenshots/` (18 images)
- **Styling docs:** `docs/styling.md` (Token, Theme, ControlStyle, ViewModifier)
- **AGENTS.md:** Build/test commands, API reference, common mistakes

## User Directives

1. Samples must match real apps — never fabricate UI
2. Use Comet theme/style features instead of inline styling
3. Syncfusion radial gauges can be "reimagined" (not required to match exactly)
4. All AI features must be implemented
5. DevFlow + maui-ai-debugging for verification
