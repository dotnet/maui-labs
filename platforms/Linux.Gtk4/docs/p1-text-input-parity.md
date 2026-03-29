# P1 — Text Input Handler Parity

Track mapper-key parity for `EntryHandler`, `EditorHandler`, `SearchBarHandler`,
`PickerHandler`, `DatePickerHandler`, and `TimePickerHandler`
against MAUI reference handlers.

Also covers P2 styling/polish handlers.

## EntryHandler — 10 missing mapper keys

| Property | GTK Approach | Status |
|---|---|---|
| `CharacterSpacing` | CSS `letter-spacing` | ✅ Done |
| `ClearButtonVisibility` | `Gtk.Entry` secondary icon | ✅ Done |
| `CursorPosition` | `Gtk.Editable.SetPosition` | ✅ Done |
| `SelectionLength` | `Gtk.Editable.SelectRegion` | ✅ Done |
| `PlaceholderColor` | — (Entry inherits placeholder CSS from theme) | ⏭ Skipped |
| `ReturnType` | No-op (mobile IME concept) | ✅ Done |
| `VerticalTextAlignment` | Widget `valign` | ✅ Done |
| `IsSpellCheckEnabled` | No GTK Entry spell-check; no-op | ✅ Done |
| `IsTextPredictionEnabled` | No GTK Entry prediction; no-op | ✅ Done |
| `Keyboard` | `SetInputPurpose` | ✅ Done |

## EditorHandler — 10 missing mapper keys

| Property | GTK Approach | Status |
|---|---|---|
| `CharacterSpacing` | CSS `letter-spacing` | ✅ Done |
| `CursorPosition` | `TextBuffer.PlaceCursor` via iter | ✅ Done |
| `SelectionLength` | `TextBuffer.SelectRange` | ✅ Done |
| `HorizontalTextAlignment` | `SetJustification` | ✅ Done |
| `MaxLength` | Buffer change signal + clamp | ✅ Done |
| `PlaceholderColor` | Overlay label not yet implemented | ⏭ Skipped |
| `VerticalTextAlignment` | Widget `valign` | ✅ Done |
| `IsSpellCheckEnabled` | No built-in spell-check; no-op | ✅ Done |
| `IsTextPredictionEnabled` | No prediction; no-op | ✅ Done |
| `Keyboard` | `SetInputPurpose` | ✅ Done |

## SearchBarHandler — 13 missing mapper keys

| Property | GTK Approach | Status |
|---|---|---|
| `CharacterSpacing` | CSS `letter-spacing` | ✅ Done |
| `HorizontalTextAlignment` | CSS `text-align` | ✅ Done |
| `IsReadOnly` | `SetEditable(false)` | ✅ Done |
| `MaxLength` | `SetMaxWidthChars` (hint; no hard clamp) | ✅ Done |
| `PlaceholderColor` | CSS `placeholder` selector | ✅ Done |
| `CancelButtonColor` | CSS on last image child | ✅ Done |
| `SearchIconColor` | — (not exposed separately) | ⏭ Skipped |
| `ReturnType` | — (not in ISearchBar interface) | ⏭ Skipped |
| `VerticalTextAlignment` | Widget `valign` | ✅ Done |
| `IsSpellCheckEnabled` | No-op | ✅ Done |
| `IsTextPredictionEnabled` | No-op | ✅ Done |
| `Keyboard` | — (not in ISearchBar interface) | ⏭ Skipped |
| `Font` | Already mapped (audit false-positive) | ✅ Done |

## Notes
- GTK4 does not support spell-check or text-prediction on `Entry`/`SearchEntry`
  natively. Those mappers are intentional no-ops that prevent MAUI warnings.
- `ReturnType` is a mobile IME concept; mapped as no-op on desktop Linux.
- `Keyboard` maps to `Gtk.InputPurpose` for IME hints.

## PickerHandler — 7 missing mapper keys (5 wired, 2 no-op)

| Property | GTK Approach | Status |
|---|---|---|
| `CharacterSpacing` | CSS `letter-spacing` | ✅ Done |
| `HorizontalTextAlignment` | CSS `text-align` | ✅ Done |
| `TextColor` | CSS `color` | ✅ Done |
| `TitleColor` | No-op (DropDown has no title) | ✅ Done |
| `VerticalTextAlignment` | Widget `valign` | ✅ Done |
| `IsOpen` | No-op (desktop uses DropDown) | ⏭ Skipped |
| `Font` | Already mapped | ✅ Done |

## DatePickerHandler — 6 missing mapper keys (4 wired)

| Property | GTK Approach | Status |
|---|---|---|
| `CharacterSpacing` | CSS `letter-spacing` | ✅ Done |
| `TextColor` | CSS `color` | ✅ Done |
| `MinimumDate` | Clamp current date | ✅ Done |
| `MaximumDate` | Clamp current date | ✅ Done |
| `IsOpen` | Not applicable (desktop) | ⏭ Skipped |
| `Font` | Already mapped | ✅ Done |

## TimePickerHandler — 4 missing mapper keys (2 wired)

| Property | GTK Approach | Status |
|---|---|---|
| `CharacterSpacing` | CSS `letter-spacing` | ✅ Done |
| `TextColor` | CSS `color` | ✅ Done |
| `IsOpen` | Not applicable (desktop) | ⏭ Skipped |
| `Font` | Already mapped | ✅ Done |

## P2 Styling/Polish — All Done

| Handler | Properties Added | Status |
|---|---|---|
| ButtonHandler | CharacterSpacing, CornerRadius, StrokeColor, StrokeThickness, Padding(CSS) | ✅ Done |
| LabelHandler | CharacterSpacing, LineHeight, VerticalTextAlignment | ✅ Done |
| SliderHandler | MinimumTrackColor, MaximumTrackColor, ThumbColor | ✅ Done |
| RadioButtonHandler | CharacterSpacing, TextColor, CornerRadius, StrokeColor, StrokeThickness | ✅ Done |
| ProgressBarHandler | ProgressColor | ✅ Done |
| ActivityIndicatorHandler | Color | ✅ Done |
| SwitchHandler | TrackColor, ThumbColor (was empty stubs) | ✅ Done |
| CheckBoxHandler | Foreground (was empty stub) | ✅ Done |
| ScrollViewHandler | HorizontalScrollBarVisibility, VerticalScrollBarVisibility (was empty stubs) | ✅ Done |
| LayoutHandler | Background (was empty stub) | ✅ Done |
