# naomi — History

Session history for naomi.

## Learnings

**2025-07-22 — CoffeeModifiers + FormFields rewrite**

- Created `sample/CometBaristaNotes/Styles/CoffeeModifiers.cs` with 7 ViewModifier subclasses (Card, ListCard, FormField, SectionHeader, PrimaryButton, SecondaryButton, DangerButton) plus a `CoffeeModifiers` static class with singleton instances.
- Rewrote `sample/CometBaristaNotes/Components/FormFields.cs` to apply CoffeeModifiers via `.Modifier()` for buttons and section headers. Form field methods (entry, picker, editor, slider) keep Border-based inline styling since ViewModifier operates on generic `View` but form fields need Border-specific methods (CornerRadius, StrokeColor, StrokeThickness).
- `Theme` class was renamed to `CoffeeColors` by another agent per team decision (name collision with `Comet.Styles.Theme`). Both files aligned to use `CoffeeColors`.
- For `ViewModifier.Apply(View)`, use `RoundedBorder()` (generic extension) instead of `CornerRadius()`/`StrokeColor()` which are Border-only. `ClipShape(new RoundedRectangle(r))` works for generic views too.
- Added optional `action`/`actionLabel` params to `MakeEmptyState` and configurable `height` to `MakeFormEditor` — backward compatible with all existing callers.
- MakeFormSlider: removed redundant Border wrapper — sliders don't need a bordered container.
- Pre-existing build errors (Icons.cs `Theme` refs, ShotLoggingPage Syncfusion types) are outside my scope.

**2025-07-25 — AI advice + voice UI wired into ShotLoggingPage**

- Wired AI advice section into ShotLoggingPage below save button. Uses `IAIAdviceService.GetShotAdvice(ShotRecord)` — the on-disk interface returns a plain string, not structured DTOs (DTOs and ported services from Bobbie's spec don't exist on disk yet). UI has 3 states: toggle button → loading (ActivityIndicator) → success card with advice text / error card with retry.
- Added voice command overlay as ZStack-based bottom sheet. FAB mic button at bottom-right opens the sheet. Sheet has header (close + clear history), scrollable chat history, state indicator, and circular mic button. Uses `ISpeechRecognitionService.RecognizeSpeechAsync` for STT and `IVoiceCommandService.ProcessCommand` for AI processing.
- Chat bubbles: user messages right-aligned with Primary background, AI responses left-aligned with SurfaceVariant background, errors in red-tinted bubbles.
- **Gotcha: `Theme` not `CoffeeColors`** — The rename from `Theme` to `CoffeeColors` noted in decisions.md hasn't actually landed on disk. The class is still called `Theme` in `Components/Theme.cs`. All existing pages use `Theme.*`.
- **Gotcha: `OnTap` takes `Action<T>` not `Action`** — Must use `_ =>` lambda pattern, not `() =>`.
- **Gotcha: Comet `Frame()` has no `minHeight` parameter** — Only `width` and `height`. Use `height` as a fixed size instead.
- **Gotcha: `Border.CornerRadius` takes 4 floats, not a `CornerRadius` struct** — Use `.CornerRadius(topLeft, topRight, bottomLeft, bottomRight)`.
- **Gotcha: `Navigate<T>()` requires `new()` constraint** — A constructor with default params (`int shotId = 0`) doesn't satisfy it. Must add an explicit parameterless constructor: `public ShotLoggingPage() : this(0) { }`.
- Fixed pre-existing maccatalyst SupportedOSPlatformVersion from 15.0 → 17.0 (required by SDK net11.0-maccatalyst).
- Services resolved via `IPlatformApplication.Current?.Services.GetService<T>()` (consistent with other pages). Mocks are already registered in MauiProgram.cs.
- Build: 0 errors, 3 warnings (all pre-existing framework warnings).
