# Slider Drag Bug — Root Cause Analysis

**Date:** March 13, 2026
**Status:** Resolved
**Severity:** High — slider control completely unusable (stuck, cannot drag)
**Affected:** Any `Slider` bound to `State<T>` with inline text interpolation

---

## Symptom

A slider bound to a `State<double>` could not be dragged. Grabbing the thumb and moving it had no effect — the slider appeared frozen. Removing the state binding (`Slider(0, 0, 100)`) allowed normal dragging.

## Root Cause

The bug was caused by **full body rebuilds on every drag tick**, triggered by an inline string interpolation that registered the slider's state as a **GlobalProperty**.

### The problematic pattern

```csharp
readonly State<double> sliderValue = 50;

// In the body:
Slider(sliderValue.Value, 0, 100)
    .OnValueChanged(value => sliderValue.Value = value),
Text($"Slider: {sliderValue.Value:F0}")  // ← THIS CAUSED THE BUG
```

### Why this breaks

Comet's binding system has two property registration paths:

1. **ViewUpdateProperties** — targeted updates via `ViewPropertyChanged`. The handler updates a single property on the native view. No body rebuild. Fast.
2. **GlobalProperties** — triggers a full `Reload()` → `ResetView()` → body rebuild → diff → `SetVirtualView`. Slow, and destructive for interactive controls.

The `Text($"Slider: {sliderValue.Value:F0}")` expression reads `sliderValue.Value` during body construction. The string interpolation evaluates inline (not inside a lambda), producing a `string` value. This enters the `T → Binding<T>` implicit conversion where `T = string`.

Inside the conversion, `StateManager.EndProperty()` captures the property read `{sliderValue, "Value"}`. The binding then checks:

```csharp
// Binding.cs, BindToProperty, line 193
if (EqualityComparer<T>.Default.Equals(stateValue, CurrentValue))
```

Here `stateValue` is the result of `prop.BindingObject.GetPropertyValue("Value").Cast<string>()` — casting a `double` to `string`. This doesn't match `CurrentValue` (the interpolated string `"Slider: 50"`), so the binding falls through to `isGlobal = true`, registering `sliderValue` as a **GlobalProperty**.

### The rebuild loop

On every drag tick:

1. Native `UISlider.ValueChanged` fires
2. MAUI's `SliderProxy` calls `virtualView.Value = platformView.Value`
3. The binding's `Set` delegate updates `state.Value`
4. `CallPropertyChanged` → `StateManager.OnPropertyChanged` → `BindingPropertyChanged`
5. `State.UpdateValue()` finds `sliderValue` in **GlobalProperties** → returns `false`
6. `Reload(false)` → `ResetView()` → full body rebuild
7. New Slider created → `Diff` → `UpdateFromOldView` → `SetVirtualView(newSlider)`
8. `Mapper.UpdateProperties` → `MapValue` → `uiSlider.UpdateValue(slider)`
9. Native slider value is reset to the virtual view's value

Steps 1–9 repeat on every drag event (~60Hz), creating a tight loop where the native slider is constantly reset to its own value. The slider appears frozen because each drag movement is immediately undone by the rebuild.

### Additional contributing factor: `.OnValueChanged`

The `.OnValueChanged(value => sliderValue.Value = value)` callback created a second event handler on the native `UISlider.ValueChanged` event (via `CometSliderValueChanged` mapper in `AppHostBuilderExtensions.cs`). This was redundant with the two-way binding's `Set` delegate and added another state update path during rebuilds.

## The Fix

Three changes, all in the sample code pattern:

```csharp
// BEFORE (broken)
Slider(sliderValue.Value, 0, 100)
    .OnValueChanged(value => sliderValue.Value = value),
Text($"Slider: {sliderValue.Value:F0}")

// AFTER (working)
Slider(sliderValue, 0, 100),
Text(() => $"Slider: {sliderValue.Value:F0}")
```

### What each change does

1. **`Slider(sliderValue, 0, 100)`** — passes the `State<double>` directly instead of `.Value`. This triggers the `State<T> → Binding<T>` implicit conversion which creates a proper two-way binding with `IsFunc = true`. The binding is registered in **ViewUpdateProperties**, not GlobalProperties.

2. **`Text(() => $"...")`** — wraps the interpolation in a lambda (`Func<string>`). This triggers the `Func<T> → Binding<T>` conversion, creating a read-only binding with `IsFunc = true`. The lambda is re-evaluated on state change via `EvaluateAndNotify()`, updating only the text label — no body rebuild.

3. **Removed `.OnValueChanged()`** — the two-way binding from `State<T> → Binding<T>` already handles value writeback via `_set = (v) => { state.Value = v; }`. The callback was redundant and added unnecessary event handler churn during rebuilds.

### Safety net: ModifyMapping

A `ModifyMapping` for `IRange.Value` was added as insurance — it checks `UISlider.Tracking` on iOS/Mac Catalyst and skips `MapValue` during active drag gestures. This protects against body rebuilds triggered by other state changes while the user is dragging.

```csharp
SliderHandler.Mapper.ModifyMapping(nameof(IRange.Value), (handler, view, existingAction) =>
{
#if __IOS__ || MACCATALYST
    if (handler.PlatformView is UIKit.UISlider uiSlider && uiSlider.Tracking)
        return;
#endif
    existingAction?.Invoke(handler, view);
});
```

## Debugging Notes

### Mac Catalyst sandbox gotcha

File-based logging (`System.IO.File.AppendAllText("/tmp/...")`) is silently swallowed by the Mac Catalyst sandbox. The app cannot write to `/tmp/` or other system paths. This wasted significant investigation time as three levels of logging all appeared to show "nothing is being called" when in fact the code was executing normally.

For future debugging on Mac Catalyst, use `NSLog` or write to the app's container directory.

### Binding system key concepts

| Pattern | Binding Type | Registration | Body Rebuild? |
|---------|-------------|--------------|---------------|
| `Slider(state, 0, 100)` | `State<T> → Binding<T>` | ViewUpdateProperties | No |
| `Slider(state.Value, 0, 100)` | `T → Binding<T>` → `State<T> → Binding<T>` | ViewUpdateProperties | No |
| `Text(() => $"{state.Value}")` | `Func<T> → Binding<T>` | ViewUpdateProperties | No |
| `Text($"{state.Value}")` | `T → Binding<T>` (type mismatch) | **GlobalProperties** | **Yes** |

The critical distinction: when `T` in the binding doesn't match the state's type (e.g., `string` binding reading a `double` state), the 1:1 value comparison in `BindToProperty` fails, and the property is registered as global.

## Recommendations

1. **Always use lambdas for reactive text**: `Text(() => $"...")` not `Text($"...")`
2. **Pass `State<T>` directly** to controls instead of `.Value` when two-way binding is needed
3. **Avoid `.OnValueChanged()` / `.OnToggled()` / etc.** when using `State<T>` directly — the two-way binding handles writeback
4. **Consider a Roslyn analyzer** that warns when `State<T>.Value` is read inside a non-lambda binding expression — this is almost always a bug
