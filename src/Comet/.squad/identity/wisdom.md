---
last_updated: 2026-04-03T02:52:02.281Z
---

# Team Wisdom

Reusable patterns and heuristics learned through work. NOT transcripts — each entry is a distilled, actionable insight.

## User Directives

These are David's standing instructions. They override any agent decision.

1. **NOT renaming Comet to Orbit.** The framework stays "Comet."
2. **Tooling order:** maui-ai-debugging → MauiDevFlow → Appium (as fallback)
3. **Render-only ≠ done.** Screenshots without interactive flow exercise do not count as verification.
4. **Samples must match real apps.** CometBaristaNotes ground truth is `~/work/BaristaNotes` (MauiReactor). Do NOT fabricate UI.
5. **Always use Opus 4.6** for Squad agents.
6. **Never use emoji in UI.** For icons, use SF Symbols via FontImageSource or Material Symbols font.
7. **Nothing is too difficult or costly (tokens/time) to not obey instructions to the letter.**
8. **Own your dependencies.** When a tool or dependency is broken (DevFlow won't connect, API key missing, simulator issue), FIX IT. Do not report blockers when the solution is in the repo, reference code, or docs you were given. You have the source code — build custom versions, PR fixes, solve it.
9. **Follow the reference code.** When original implementation exists (e.g., BaristaNotes), that IS the roadmap. Do what it does. Don't invent when you can port. Don't punt with "needs X" when the reference shows exactly how to get X.
10. **Verify everything end-to-end.** Read `.squad/skills/verification-protocol/SKILL.md` before any verification step. Build passing ≠ done. Fresh binary confirmed, DevFlow connected, tree inspected, screenshots compared against reference, navigation exercised, features tested. Every step, every time.

## Patterns

<!-- Append entries below. Format: **Pattern:** description. **Context:** when it applies. -->

**Pattern:** ConditionalWeakTable to prevent duplicate handler subscriptions in AppendToMapping callbacks. **Context:** Comet's Reload cycle calls SetVirtualView repeatedly, causing AppendToMapping to fire multiple times on the same native view. Track subscribed views to skip duplicates.

**Pattern:** Agent decisions must be captured separately from user directives in decisions.md. **Context:** User directives are standing orders. Agent decisions are implementation choices. Mixing them causes user intent to get buried.

**Pattern:** Build order matters — Comet.SourceGenerator MUST build before Comet.csproj. **Context:** The source generator produces factory methods, On-prefixed extensions, and StyleBuilders. If skipped, the main Comet build will fail with missing type errors.

**Pattern:** Suppress ReactiveScheduler notifications during text change callbacks and PropertySubscription.Set() WriteBack. **Context:** Prevents TextField focus loss when typing triggers state updates that rebuild the view.

**Pattern:** On Android, setting Clickable=true on layout ViewGroups causes them to consume touch events, blocking child Button clicks. **Context:** Only set Clickable=true on ViewGroups with explicit Comet gestures.

**Pattern:** CometBaristaNotes uses Component<TState> pattern with SetState for all pages, NOT View+[Body]. **Context:** The sample app follows the Component pattern for consistency with the original MauiReactor BaristaNotes which uses MauiReactor Components.
