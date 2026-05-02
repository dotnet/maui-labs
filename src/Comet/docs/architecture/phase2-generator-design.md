# Phase 2 — Source Generator Template Design for State Unification

**Author:** Naomi (Source Generator Dev)  
**Date:** 2025-07-24  
**Status:** Design / Prototype — DO NOT IMPLEMENT YET  
**Prerequisite:** Holden must land `PropertySubscription<T>` first (Phase 1)

---

## 1. Current Template Analysis

### 1.1 Generator Architecture

The source generator (`CometViewSourceGenerator.cs`) uses Mustache templates (via Stubble) to produce **four output files per control**:

| Output | Template Constant | Purpose |
|--------|-------------------|---------|
| `{Control}.g.cs` | `classMustacheTemplate` | Class body: constructors, fields, properties, interface implementations |
| `{Control}Extension.g.cs` | `extensionMustacheTemplate` | Fluent extension methods (`.Background()`, `.FontSize()`, etc.) |
| `{Control}Factory.g.cs` | `factoryMustacheTemplate` | Static factory methods in `CometControls` |
| `{Control}StyleBuilder.g.cs` | `styleBuilderMustacheTemplate` | Style builder class in `Comet.Styles` |

### 1.2 All 19 Generated Controls

From `src/Comet/Controls/ControlsGenerator.cs`:

| # | ClassName | MAUI Interface | Key Properties (constructor params) |
|---|-----------|----------------|-------------------------------------|
| 1 | Button | ITextButton | Text, Clicked |
| 2 | ImageButton | IImageButton | Source, Clicked |
| 3 | IndicatorView | IIndicatorView | Count |
| 4 | RefreshView | IRefreshView | IsRefreshing |
| 5 | Text | ILabel | Text (renamed → Value) |
| 6 | SecureField | IEntry | Text, Placeholder, Completed |
| 7 | ActivityIndicator | IActivityIndicator | IsRunning |
| 8 | CheckBox | ICheckBox | IsChecked |
| 9 | DatePicker | IDatePicker | Date, MinimumDate, MaximumDate |
| 10 | ProgressBar | IProgress | Progress (renamed → Value) |
| 11 | SearchBar | ISearchBar | Text, Search |
| 12 | TextEditor | IEditor | Text |
| 13 | TextField | IEntry | Text, Placeholder, Completed |
| 14 | Slider | ISlider | Value, Minimum=0, Maximum=1 |
| 15 | Toggle | ISwitch | IsOn (renamed → Value) |
| 16 | TimePicker | ITimePicker | Time |
| 17 | Stepper | IStepper | Value, Minimum, Maximum, Interval |
| 18 | Toolbar | IToolbar | BackButtonVisible, IsVisible |
| 19 | FlyoutView | IFlyoutView | (none — no key properties) |

### 1.3 Where `Binding<T>` Appears in Templates

**Reference counts in `CometViewSourceGenerator.cs`:** 13 occurrences of `Binding<`

#### Class template (`classMustacheTemplate`)

```
Location 1 — Constructor (Binding overload):
  public {{ClassName}} ({{#ParametersFunction}} Binding<{{{Type}}}> {{LowercaseName}}...

Location 2 — Field declaration:
  Binding<{{{Type}}}> {{LowercaseName}};

Location 3 — Property declaration:
  public Binding<{{{Type}}}> {{Name}}

Location 4 — Property setter:
  private set => this.SetBindingValue(ref this.{{LowercaseName}}, value);
```

#### Signal constructor assignment (in model data, line 492):
```csharp
$"{name} = {lowercaseName} == null ? null : new Binding<{type}>(() => {lowercaseName}.Value, v => {lowercaseName}.Value = v);"
```

#### Func constructor assignment (line 495):
```csharp
$"{name} = {lowercaseName} == null ? null : new Binding<{type}>({lowercaseName}, null);"
```

#### Computed constructor assignment (line 498):
```csharp
$"{name} = {lowercaseName} == null ? null : new Binding<{type}>(() => {lowercaseName}.Value, null);"
```

#### Value constructor assignment (lines 501-503):
```csharp
// nullable: $"{name} = {lowercaseName} == null ? null : new Binding<{type}>(() => {valueBindingValue}, null);"
// non-null: $"{name} = new Binding<{type}>(() => {lowercaseName}, null);"
```

#### Extension method template (`extensionProperty`):
```
public static T {{Name}}<T>(this T view, Binding<{{{Type}}}> ...) where T : {{ClassName}} =>
  view.SetEnvironment(nameof({{FullName}}), {{LowercaseName}}, cascades);

public static T {{Name}}<T>(this T view, Func<{{{Type}}}> ...) where T : {{ClassName}} =>
  view.SetEnvironment(nameof({{FullName}}), (Binding<{{{Type}}}>){{LowercaseName}}, cascades);
```

#### Token extension template (`tokenExtensionProperty`):
```
public static T {{Name}}<T>(this T view, Token<{{{Type}}}> token) where T : {{ClassName}} =>
  view.SetEnvironment(nameof({{FullName}}), (Binding<{{{Type}}}>)(Func<{{{Type}}}>)(...), true);
```

#### Factory template (`factoryMustacheTemplate`):
```
public static {{ClassName}} {{ClassName}}({{#ParametersFunction}} Binding<{{{Type}}}> ...
```

#### Interface property templates (in static constructor, lines 261-293):
```
{{{Type}}} {{FullName}} {
  get => {{Name}}?.CurrentValue ?? {{DefaultValue}};    // reads from Binding<T>
  set => {{Name}}?.Set(value);                           // writes to Binding<T>
}
```

### 1.4 `SetBindingValue` Usage

- **1 occurrence** in templates (class template property setter)
- **1 definition** in `DatabindingExtensions.cs:18`:
  ```csharp
  public static void SetBindingValue<T>(this View view, ref Binding<T> currentValue, 
      Binding<T> newValue, [CallerMemberName] string propertyName = "")
  ```

### 1.5 `StateManager` / `ProcessGetFunc` in Other Generators

- `StateWrapperGenerator.cs`: 3 references to `StateManager`
- `AutoNotifyGenerator.cs`: 2 references to `StateManager`
- These generators are **not affected** by the `Binding<T>` → `PropertySubscription<T>` migration

---

## 2. Proposed Template Changes

### 2.1 Class Template — BEFORE / AFTER

#### Constructor (Binding overload) — UNCHANGED (kept for backward compat)
```csharp
// BEFORE (keep as-is during transition)
public {{ClassName}} ({{#ParametersFunction}} Binding<{{{Type}}}> {{LowercaseName}}{{DefaultValueString}}{{/ParametersFunction}})
```

#### Field declaration — CHANGE
```csharp
// BEFORE
Binding<{{{Type}}}> {{LowercaseName}};

// AFTER
PropertySubscription<{{{Type}}}> {{LowercaseName}};
```

#### Property declaration — CHANGE
```csharp
// BEFORE
public Binding<{{{Type}}}> {{Name}}
{
    get => {{LowercaseName}};
    private set => this.SetBindingValue(ref this.{{LowercaseName}}, value);
}

// AFTER
public PropertySubscription<{{{Type}}}> {{Name}}
{
    get => {{LowercaseName}};
    private set => this.SetPropertySubscription(ref this.{{LowercaseName}}, value);
}
```

#### Signal constructor assignment — CHANGE
```csharp
// BEFORE
$"{name} = {lowercaseName} == null ? null : new Binding<{type}>(() => {lowercaseName}.Value, v => {lowercaseName}.Value = v);"

// AFTER
$"{name} = {lowercaseName} == null ? null : PropertySubscription<{type}>.FromSignal({lowercaseName});"
```

#### Func constructor assignment — CHANGE
```csharp
// BEFORE
$"{name} = {lowercaseName} == null ? null : new Binding<{type}>({lowercaseName}, null);"

// AFTER
$"{name} = {lowercaseName} == null ? null : PropertySubscription<{type}>.FromFunc({lowercaseName});"
```

#### Computed constructor assignment — CHANGE
```csharp
// BEFORE
$"{name} = {lowercaseName} == null ? null : new Binding<{type}>(() => {lowercaseName}.Value, null);"

// AFTER
$"{name} = {lowercaseName} == null ? null : PropertySubscription<{type}>.FromComputed({lowercaseName});"
```

#### Value constructor assignment — CHANGE
```csharp
// BEFORE (nullable)
$"{name} = {lowercaseName} == null ? null : new Binding<{type}>(() => {valueBindingValue}, null);"

// AFTER (nullable)
$"{name} = {lowercaseName} == null ? null : PropertySubscription<{type}>.FromValue({valueBindingValue});"

// BEFORE (non-null)
$"{name} = new Binding<{type}>(() => {lowercaseName}, null);"

// AFTER (non-null)
$"{name} = PropertySubscription<{type}>.FromValue({lowercaseName});"
```

#### Interface property template — CHANGE
```csharp
// BEFORE
{{{Type}}} {{FullName}} {
    get => {{Name}}?.CurrentValue ?? {{DefaultValue}};
    set => {{Name}}?.Set(value);
}

// AFTER
{{{Type}}} {{FullName}} {
    get => {{Name}}?.CurrentValue ?? {{DefaultValue}};
    set => {{Name}}?.Set(value);
}
// NOTE: CurrentValue and Set(value) must exist on PropertySubscription<T>
// with same semantics. No template change if API matches.
```

### 2.2 Extension Method Template — BEFORE / AFTER

```csharp
// BEFORE
public static T {{Name}}<T>(this T view, Binding<{{{Type}}}> {{LowercaseName}}, bool cascades = true)
    where T : {{ClassName}} =>
    view.SetEnvironment(nameof({{FullName}}), {{LowercaseName}}, cascades);

public static T {{Name}}<T>(this T view, Func<{{{Type}}}> {{LowercaseName}}, bool cascades = true)
    where T : {{ClassName}} =>
    view.SetEnvironment(nameof({{FullName}}), (Binding<{{{Type}}}>){{LowercaseName}}, cascades);

// AFTER
public static T {{Name}}<T>(this T view, PropertySubscription<{{{Type}}}> {{LowercaseName}}, bool cascades = true)
    where T : {{ClassName}} =>
    view.SetEnvironment(nameof({{FullName}}), {{LowercaseName}}, cascades);

public static T {{Name}}<T>(this T view, Func<{{{Type}}}> {{LowercaseName}}, bool cascades = true)
    where T : {{ClassName}} =>
    view.SetEnvironment(nameof({{FullName}}), PropertySubscription<{{{Type}}}>.FromFunc({{LowercaseName}}), cascades);

// NEW — Signal overload for extensions
public static T {{Name}}<T>(this T view, Signal<{{{Type}}}> {{LowercaseName}}, bool cascades = true)
    where T : {{ClassName}} =>
    view.SetEnvironment(nameof({{FullName}}), PropertySubscription<{{{Type}}}>.FromSignal({{LowercaseName}}), cascades);
```

#### Token extension — CHANGE
```csharp
// BEFORE
public static T {{Name}}<T>(this T view, Token<{{{Type}}}> token) where T : {{ClassName}} =>
    view.SetEnvironment(nameof({{FullName}}), (Binding<{{{Type}}}>)(Func<{{{Type}}}>)(() => view.GetToken(token)), true);

// AFTER
public static T {{Name}}<T>(this T view, Token<{{{Type}}}> token) where T : {{ClassName}} =>
    view.SetEnvironment(nameof({{FullName}}), PropertySubscription<{{{Type}}}>.FromFunc(() => view.GetToken(token)), true);
```

### 2.3 Factory Template — BEFORE / AFTER

The Binding overload stays for backward compat; new overloads added:

```csharp
// BEFORE — only Binding, Signal, Func, Computed, Value overloads
// Constructor types already diversified; factory just delegates.
// No change needed in factory template if constructor signatures stay.

// AFTER — PropertySubscription overload added
public static {{ClassName}} {{ClassName}}({{#PSParametersFunction}} PropertySubscription<{{{Type}}}> {{LowercaseName}}{{DefaultValueString}}{{/PSParametersFunction}})
    => new {{ClassName}}({{#ParameterNamesFunction}} {{LowercaseName}}{{/ParameterNamesFunction}});
```

### 2.4 Style Builder Template — NO CHANGE

Style builders use environment keys and raw values (not `Binding<T>`). No changes needed.

---

## 3. Required Runtime Support (from Holden)

Before templates can be changed, these must exist in `Comet`:

| Type / Method | Location | Purpose |
|---------------|----------|---------|
| `PropertySubscription<T>` | New file, `src/Comet/PropertySubscription.cs` | Replacement for `Binding<T>` in generated controls |
| `.CurrentValue` | Property on `PropertySubscription<T>` | Read current value (same as `Binding<T>.CurrentValue`) |
| `.Set(T value)` | Method on `PropertySubscription<T>` | Write value (same as `Binding<T>.Set`) |
| `.FromSignal(Signal<T>)` | Static factory | Wrap a `Signal<T>` |
| `.FromFunc(Func<T>)` | Static factory | Wrap a `Func<T>` (lazy evaluation) |
| `.FromComputed(Computed<T>)` | Static factory | Wrap a `Computed<T>` |
| `.FromValue(T)` | Static factory | Wrap a static value |
| `SetPropertySubscription<T>()` | Extension on `View` in `DatabindingExtensions.cs` | Replace `SetBindingValue` |
| `implicit operator PropertySubscription<T>(Binding<T>)` | Conversion | Backward compat during transition |

---

## 4. Per-Control Overload Matrix

Every generated control needs these constructor overloads for its key properties:

| Overload | Parameter Types | Assignment Pattern |
|----------|----------------|-------------------|
| Binding (legacy) | `Binding<T>` | Direct assign |
| Signal | `Signal<T>` | `PropertySubscription<T>.FromSignal(...)` |
| Func | `Func<T>` | `PropertySubscription<T>.FromFunc(...)` |
| Computed | `Computed<T>` | `PropertySubscription<T>.FromComputed(...)` |
| Value (static) | `T` / `T?` | `PropertySubscription<T>.FromValue(...)` |
| Parameterless | (none) | Default |

Controls that need each overload (all 18 with key properties — FlyoutView has none):

| Control | # Key Props | Props with Defaults | Action Props (no PS) |
|---------|-------------|--------------------|-----------------------|
| Button | 2 | — | Clicked |
| ImageButton | 2 | — | Clicked |
| IndicatorView | 1 | — | — |
| RefreshView | 1 | — | — |
| Text | 1 | — | — |
| SecureField | 3 | — | Completed |
| ActivityIndicator | 1 | — | — |
| CheckBox | 1 | — | — |
| DatePicker | 3 | MinimumDate, MaximumDate | — |
| ProgressBar | 1 | — | — |
| SearchBar | 2 | — | Search |
| TextEditor | 1 | — | — |
| TextField | 3 | — | Completed |
| Slider | 3 | Minimum=0, Maximum=1 | — |
| Toggle | 1 | — | — |
| TimePicker | 1 | — | — |
| Stepper | 4 | Minimum, Maximum, Interval | — |
| Toolbar | 2 | IsVisible | — |
| FlyoutView | 0 | — | — |

**Action-type parameters** (e.g., `Clicked`, `Completed`, `Search`) are NOT wrapped in `Binding<T>` or `PropertySubscription<T>` — they remain plain `Action`/`Action<T>`. The generator already handles this correctly by checking `isDelegate`.

---

## 5. Special Cases & Risks

### 5.1 ImageButton — Handwritten Partial + Generated

`ImageButton` is the **only control** that is both generated AND has a handwritten partial class (`src/Comet/Controls/ImageButton.cs`). The handwritten part declares:

```csharp
Binding<string> _source;
public Binding<string> Source { get => _source; private set => this.SetBindingValue(ref _source, value); }
```

**Risk:** The generated code and handwritten code both declare `Binding<T>` fields. Migration must update BOTH. The handwritten `ImageButton.cs` must be updated manually alongside the generator change.

### 5.2 Handwritten Controls (15 files, ~35+ Binding<T> declarations)

These are NOT affected by the generator but must be migrated separately:

| File | Binding<T> Decls | SetBindingValue Calls |
|------|------------------|-----------------------|
| Image.cs | 2 | 2 |
| FlyoutPage.cs | 2 | 2 |
| Frame.cs | 3 | 3 |
| BoxView.cs | 2 | 2 |
| CollectionView.cs | 3 | 3 |
| FlyoutNavigationView.cs | 2 | 2 |
| ListView.cs | 1 | 1 |
| TableView.cs | ~10 | ~10 |
| WebView.cs | 2 | 2 |
| ShapeView.cs | 1 | 1 |
| RadioButton.cs | 2 | 2 |
| CarouselView.cs | 3 | 3 |
| TabbedPage.cs | 1 | 1 |
| Picker.cs | 3 | 3 |

**Total handwritten migration:** ~37 field/property pairs + ~37 `SetBindingValue` calls.

### 5.3 Implicit Conversions That Must Be Preserved or Replaced

Current `Binding<T>` implicit operators:

| Operator | Status |
|----------|--------|
| `implicit operator Binding<T>(T value)` | Already `[Obsolete]`. Can drop when PropertySubscription lands. |
| `implicit operator Binding<T>(Func<T> value)` | Must have equivalent on `PropertySubscription<T>` or keep. |
| `implicit operator Binding<T>(State<T> state)` | Must have equivalent for `Signal<T>`. |
| `implicit operator T(Binding<T> value)` | Must have equivalent on `PropertySubscription<T>`. |
| `implicit operator Binding<T>(Token<T> token)` | In `Styles/Token.cs`. Must update to target `PropertySubscription<T>`. |

### 5.4 Extension Methods on `Binding<T>` (in `BindingExtensions`)

Three extension methods in `Binding.cs` lines 399-432:

```csharp
Binding<T>.GetValueOrDefault<T>(T defaultValue)
Binding<TSource>.Convert<TSource, TTarget>(IValueConverter converter, ...)
Binding<TSource>.Convert<TSource, TTarget>(Func<TSource, TTarget> convert, ...)
```

These must get `PropertySubscription<T>` equivalents.

### 5.5 `SetEnvironment` Compatibility

Extension methods currently call `view.SetEnvironment(key, binding, cascades)` passing `Binding<T>`. The environment system stores `object`. As long as `PropertySubscription<T>` can be stored/retrieved the same way, no environment changes needed.

---

## 6. Estimated Scope

### Template Changes (in `CometViewSourceGenerator.cs`)

| Area | Lines Changed |
|------|---------------|
| `classMustacheTemplate` — field + property decl | ~4 lines |
| `classMustacheTemplate` — using directive | +1 line (if PS is in new namespace) |
| `extensionProperty` template | ~6 lines (+ new Signal overload) |
| `tokenExtensionProperty` template | ~2 lines |
| `factoryMustacheTemplate` | ~4 lines (new PS overload) |
| Model data — SignalAssignment | 1 line |
| Model data — FuncAssignment | 1 line |
| Model data — ComputedAssignment | 1 line |
| Model data — ValueAssignment | 2 lines |
| Interface property templates | 0 lines (if API matches) |
| `ParametersFunction` lambda — `Binding<System.Action>` replace | 1 line |
| **Total template/model changes** | **~23 lines** |

### Runtime Changes Needed (Holden)

| Area | Estimate |
|------|----------|
| `PropertySubscription<T>` class | ~150-200 lines |
| `SetPropertySubscription<T>` extension | ~5 lines |
| Implicit conversions / factory methods | ~30 lines |
| **Total new runtime code** | **~200 lines** |

### Handwritten Control Migration (Amos)

| Area | Estimate |
|------|----------|
| 15 control files × (field + property + constructor) | ~110 substitutions |
| `BindingExtensions` equivalents | ~15 lines |
| `Token.cs` conversion operator | ~3 lines |
| **Total handwritten changes** | **~130 lines** |

---

## 7. Migration Strategy

### Phase 2a — Generator Templates (Naomi, after Holden lands PropertySubscription<T>)

1. Add `PropertySubscription<T>` references to template `using` block
2. Replace `Binding<{{{Type}}}>` with `PropertySubscription<{{{Type}}}>` in field/property templates
3. Replace `SetBindingValue` with `SetPropertySubscription` in property setter
4. Update assignment strings in model data for Signal/Func/Computed/Value
5. Add `PropertySubscription` overload to extension and factory templates
6. **Keep** the `Binding<T>` constructor overload during transition (remove in Phase 3)
7. Build generator and verify output for one control (e.g., Button)

### Phase 2b — Handwritten Controls (Amos, parallel with 2a)

1. Mechanically replace `Binding<T>` → `PropertySubscription<T>` in all 15 files
2. Replace `SetBindingValue` → `SetPropertySubscription` in all 15 files
3. Update constructor signatures where they accept `Binding<T>`

### Phase 2c — Validation (Bobbie)

1. Run full test suite — all 640+ tests must pass
2. Verify generated output for all 19 controls matches expected pattern
3. Verify handwritten controls compile and interface implementations satisfied

---

## 8. Open Questions for Holden

1. **Will `PropertySubscription<T>` have `.CurrentValue` and `.Set(T)` with identical semantics?** If yes, interface property templates need zero changes.
2. **Will there be an implicit conversion from `Binding<T>` to `PropertySubscription<T>`?** Needed for backward compat during transition.
3. **Where does `PropertySubscription<T>` live?** Same namespace as `Binding<T>` (i.e., `Comet`) or in `Comet.Reactive`?
4. **Does `SetPropertySubscription` need `StateManager` integration?** Current `SetBindingValue` calls `BindToProperty` which deeply integrates with `StateManager`.
