# Fact-Check Report: `docs/state-management.md`

**Reviewer:** Copilot (automated source verification)
**Date:** 2025-07-18
**Method:** Line-by-line comparison of every code block and technical claim against the referenced source files.

---

## Verified Claims ✅

The following major technical claims are **accurate** and match the source code:

1. **`INotifyPropertyRead` interface** (§2.1): Correctly quoted from `BindingObject.cs:18-21`. Extends `INotifyPropertyChanged` with `PropertyRead` event.

2. **`CallPropertyRead` / `CallPropertyChanged<T>` methods** (§2.1): Code blocks at doc lines 77-89 are exact matches of `BindingObject.cs:93-98` and `BindingObject.cs:86-91`.

3. **`_argsCache` optimization**: Claim about cached `PropertyChangedEventArgs` is correct per `BindingObject.cs:30-41`.

4. **`BindingObject` class structure** (§2.2): `dictionary`, `GetProperty<T>`, `SetProperty<T>`, `GetValueInternal` — all correctly quoted from `BindingObject.cs:22-108`.

5. **`IAutoImplemented` marker interface** (§2.2): Correctly explains that `StateManager` skips event subscriptions for `IAutoImplemented` types. Verified at `StateManager.cs:315` and `StateManager.cs:337`.

6. **`State<T>` class** (§2.3): Dual storage, `_hasValue` flag, `GetValueInternal` fast path, equality check, implicit operators, `ValueChanged` callback — all accurately quoted from `State.cs:11-67`.

7. **`Binding` / `Binding<T>` class structure** (§2.4): `IsValue`, `IsFunc`, weak references, `_bindingStable`, `BoundFromView` vs `View` — all match `Binding.cs`.

8. **`BindingState` class** (§2.5): `GlobalProperties`, `ViewUpdateProperties`, `ChangedProperties`, `AddGlobalProperty`, `AddViewProperty`, `UpdateValue<T>` — all accurately quoted from `BindingObject.cs:111-196`.

9. **`UpdateValue` three-way routing logic** (§5.2): Return value semantics (`false` = global/reload, `true`+`bindingsHandled` = targeted, `true`+not handled = fallback) are correct per `BindingObject.cs:163-195`.

10. **`CheckForStateAttributes` reflection-based discovery** (§3.1): Correctly quoted from `StateManager.cs:244-274`. Readonly enforcement via `ReadonlyRequiresException` is accurate.

11. **`RegisterChild` and `ChildPropertyNamesMapping`** (§3.2): Correctly quoted from `StateManager.cs:276-293`.

12. **`ResolvePropertyName` helper** (§3.2): Correctly quoted from `StateManager.cs:498-509`.

13. **`CheckForBody` method** (§3.3): Correctly quoted from `View.cs:403-416`. Runtime `[Body]` wiring via `this.GetBody()` confirmed at `Internal/Extensions.cs:44-50`.

14. **`ProcessGetFunc`** (§4.2): Correctly quoted from `Binding.cs:100-111`.

15. **`BindingPropertyChanged<T>`** (§5.3): Accurately quoted from `View.cs:423-443`.

16. **`OnPropertyChanged` multi-view dispatch** (§5.4): ArrayPool usage, single-view fast path, disposed view cleanup — all match `StateManager.cs:384-496`.

17. **Direct value binding fallback logic** (§6.1): The `Binding<T>(T value)` implicit operator and `BindToProperty` branching logic are accurately quoted from `Binding.cs:75-236`.

18. **Binding stabilization** (§6.4): `_bindingStable` flag behavior and its limitation (stops tracking new deps once stable) correctly described per `Binding.cs:268-351`.

19. **StateManager batching** (§7.1): `_batchDepth`, `_dirtyBindings`, `_viewsNeedingReload`, `BeginBatch()`/`EndBatch()`/`FlushBatch()` — all accurately quoted from `StateManager.cs:34-98`.

20. **Binding batching deferral** (§7.2): `BindingValueChanged` check for `IsBatching` and `IsDirty`/`AddDirtyBinding` correctly quoted from `Binding.cs:241-266`.

21. **Component hierarchy** (§8): `Component`, `Component<TState>`, `Component<TState, TProps>` — class structure, `SetState`, `MergeStateFrom`, `TransferStateFrom`, `ShouldUpdate` are all accurate quotes from `Component.cs`.

22. **View-level batching** (§7.3): `BatchBegin()`/`BatchCommit()` correctly quoted from `View.cs:446-492`.

23. **Weak references** (§11.3): `Binding._view`, `Binding._boundFromView`, `View.parent` — all correctly described per `Binding.cs:18-29` and `View.cs:70, 120-131`.

24. **Lock strategy** (§11.5): `ReaderWriterLockSlim` usage, read vs write lock delineation, never-call-user-code-under-lock rule — accurate per `StateManager.cs:18` and dispatch patterns.

25. **Thread-static tracking state** (§12.1): `_viewStack`, `_currentReadProperties`, `_isTrackingProperties` are all `[ThreadStatic]` per `StateManager.cs:19-20, 116`.

26. **Lock-free reads during build** (§12.3): `OnPropertyRead` just appends to thread-local list, no locks. Correct per `StateManager.cs:369-375`.

27. **`SetGlobalEnvironment` dispatched to main thread** (§12.2, §9.3): Correctly quoted from `View.cs:549-558`.

28. **`SetState` dispatched to main thread** (§12.2): Correctly shown from `Component.cs:102-122`.

---

## Factual Errors ❌

### Error 1: `TransferHotReloadStateTo` Is Fabricated (§10.2, lines 1568-1598)

**Claim in doc:**
```csharp
protected virtual void TransferHotReloadStateTo(View newView)
{
    // Transfer handler
    newView.ViewHandler = ViewHandler;
    // Transfer state dictionary
    foreach (var change in State.ChangedProperties)
    {
        try { newView.SetPropertyValue(change.Key, change.Value); }
        catch { }
    }
    // Transfer environment data
    newView.PopulateFromEnvironment();
    // Call derived class hook
    TransferHotReloadStateToCore(newView);
}
```

**What the source code actually shows (`View.cs:1007-1023`):**
```csharp
internal void TransferHotReloadStateTo(View newView)
{
    if (newView == null)
        return;
    TransferHotReloadStateToCore(newView);
}
protected virtual void TransferHotReloadStateToCore(View newView)
{
    var oldState = this.GetState();
    if (oldState == null)
        return;
    var changes = oldState.ChangedProperties;
    foreach (var change in changes)
    {
        newView.SetDeepPropertyValue(change.Key, change.Value);
    }
}
```

**Specific inaccuracies:**
1. Method is `internal`, NOT `protected virtual`.
2. It does **not** transfer `ViewHandler`. (Handler transfer happens in `SetHotReloadReplacement`, `View.cs:330-344`.)
3. It does **not** call `PopulateFromEnvironment()`. (That happens in `SetHotReloadReplacement` at line 340.)
4. It does **not** have a `try/catch`.
5. State transfer uses `SetDeepPropertyValue`, NOT `SetPropertyValue`.
6. The core logic lives in `TransferHotReloadStateToCore`, not `TransferHotReloadStateTo`.

**Suggested correction:** Replace the entire §10.2 code block with the actual source. Move handler transfer and environment repopulation claims to §10.1 as part of `SetHotReloadReplacement`.

---

### Error 2: `AutoNotifyGenerator` Does NOT Process `[Body]` (§13.1, lines 1937-1965)

**Claim in doc (line 1938):**
> "Processes methods marked `[Body]` and generates the property wiring code. The generator emits a private field `__body` of type `Func<View>` and a constructor assignment or property setter that assigns the method to `Body`."

**What the source code actually shows (`AutoNotifyGenerator.cs`):**
- The generator processes **fields** marked with `[AutoNotify]` attribute (defined at line 21 as `AttributeTargets.Field`).
- It generates **property wrappers** with `StateManager.OnPropertyRead` in the getter and `StateManager.OnPropertyChanged` in the setter.
- It has **nothing to do** with `[Body]` attributes or methods.
- The `[Body]` attribute is handled purely at **runtime** via reflection: `View.CheckForBody()` → `this.GetBody()` → `Delegate.CreateDelegate(typeof(Func<View>), view, bodyMethod.Name)` (see `Internal/Extensions.cs:44-50`).

**The "Generated output (conceptual)" code block with `__body` field is entirely fabricated.** No source generator produces anything like it.

**Suggested correction:** Rewrite §13.1 entirely. Rename it "AutoNotifyGenerator" and describe what it actually does: generating property wrappers for `[AutoNotify]` fields. Add a separate subsection explaining `[Body]` as runtime-only reflection-based wiring, noting there is no compile-time generation for it.

---

### Error 3: Claim in §3.3 That Source Generator Processes `[Body]` (line 511)

**Claim in doc:**
> "The source generator (`AutoNotifyGenerator`) processes `[Body]` methods at compile time to inject property tracking hooks, but the actual delegate creation happens at runtime via `Delegate.CreateDelegate`."

**Reality:** `AutoNotifyGenerator` never touches `[Body]`. The `[Body]` attribute is a purely runtime concept. There are no compile-time hooks for it. The entire sentence is false.

**Suggested correction:** Remove the claim about source generator involvement. State: "`[Body]` is handled entirely at runtime. `View.CheckForBody()` uses `GetDeepMethodInfo(typeof(BodyAttribute))` to find the annotated method and `Delegate.CreateDelegate` to wrap it as `Func<View>`."

---

### Error 4: `IComponentWithState` Is Public, Not Internal (§10.3, line 1611)

**Claim in doc:**
```csharp
// Internal interface
interface IComponentWithState
```

**What the source code shows (`IComponentWithState.cs:9`):**
```csharp
public interface IComponentWithState
```

It is a **public** interface in its own file (`src/Comet/IComponentWithState.cs`), not an internal/nested one.

**Suggested correction:** Change `// Internal interface` to `// Public interface` and add the `public` modifier.

---

### Error 5: `View.Dispose` Code Block Is Significantly Wrong (§11.2, lines 1657-1673)

**Claim in doc:**
```csharp
protected override void Dispose(bool disposing)
{
    if (IsDisposed)
        return;
    if (disposing)
    {
        BuiltView?.Dispose();
        ViewHandler?.DisconnectHandler();
        StateManager.Disposing(this);
        // ... more cleanup
    }
    base.Dispose(disposing);
    IsDisposed = true;
}
```

**What the source code actually shows (`View.cs:664-714`):**
1. It's `protected virtual void Dispose(bool disposing)`, NOT `protected override` (View is the root — there's no base class with `Dispose`).
2. The guard is `if (!disposing) return;`, NOT `if (IsDisposed) return;`. The reentrancy guard is in a separate `OnDispose(bool)` wrapper (line 703-709) that sets `disposedValue = true` before calling `Dispose`.
3. There is no `ViewHandler?.DisconnectHandler()`. The actual disposal does: `var vh = ViewHandler; ViewHandler = null; (vh as IDisposable)?.Dispose();`
4. `BuiltView?.Dispose()` is not the first call — gestures are cleaned up first, then hot reload unregistration.
5. There is no `base.Dispose(disposing)` or `IsDisposed = true` inside `Dispose(bool)`.

**Suggested correction:** Replace the code block with the actual implementation or a faithful simplification that preserves the correct method signature, guard pattern, and disposal order.

---

### Error 6: `SetEnvironmentFields` Code Snippet Shows Wrong Call (§9.4, lines 1516-1527)

**Claim in doc:**
```csharp
void SetEnvironmentFields()
{
    var fields = this.GetFieldsWithAttribute(typeof(EnvironmentAttribute));
    if (!fields.Any())
        return;
    foreach (var f in fields)
    {
        var attribute = f.GetCustomAttributes(true).OfType<EnvironmentAttribute>().FirstOrDefault();
        var key = attribute.Key ?? f.Name;
        usedEnvironmentData.Add((f.Name, key));
        SetEnvironmentField(f, key);   // ← This method doesn't exist
    }
}
```

**What the source code actually shows (`View.cs:592-603`):**
```csharp
void SetEnvironmentFields()
{
    var fields = this.GetFieldsWithAttribute(typeof(EnvironmentAttribute));
    if (!fields.Any())
        return;
    foreach (var f in fields)
    {
        var attribute = f.GetCustomAttributes(true).OfType<EnvironmentAttribute>().FirstOrDefault();
        var key = attribute.Key ?? f.Name;
        usedEnvironmentData.Add((f.Name, key));
        State.AddGlobalProperty((View.Environment, key));  // ← Actual call
    }
}
```

The doc claims `SetEnvironmentField(f, key)` is called, but the actual code calls `State.AddGlobalProperty((View.Environment, key))`. This is significant because it means environment attribute fields are immediately registered as global properties — they trigger full view reloads when the corresponding environment key changes.

**Suggested correction:** Replace `SetEnvironmentField(f, key)` with `State.AddGlobalProperty((View.Environment, key))` and note the implication.

---

## Inaccuracies / Overstatements ⚠️

### 1. "Loaded" event timing description is slightly imprecise (§11.1, line 1651)

**Claim:** "When a view's `ViewHandler` transitions from `null` to non-null (the view becomes part of the live visual tree)."

**Reality:** The `Loaded` event fires in `SetViewHandler` at `View.cs:217-219`:
```csharp
if (oldViewHandler == null && viewHandler != null)
    OnLoaded();
```
This is accurate for the first handler assignment. But "becomes part of the live visual tree" is an overstatement — it fires whenever a handler transitions from null to non-null, which can also happen during handler reassignment (e.g., hot reload). The view may already have been in the tree previously.

### 2. Hot reload flow diagram omits the `SetHotReloadReplacement` path (§10.1)

The diagram shows `Diff → TransferHotReloadStateTo`, but the primary hot reload path goes through `GetRenderView() → SetHotReloadReplacement()` (View.cs:330-344), which handles handler transfer, navigation, parent assignment, and environment population BEFORE `TransferHotReloadStateTo`. The `Diff` path is a secondary mechanism used during tree reconciliation, not the primary entry point.

### 3. "No full rebuild" claim for lambda bindings is slightly overstated (§4.2, line 602)

**Claim:** "No full rebuild. The diff algorithm never runs."

This is true for the targeted binding path. However, if a binding becomes unstable (dependencies change) and calls `BindToProperty` again, this CAN trigger rebinding that involves registration changes. Also, if the same state property is BOTH a global property (read during Body) AND a bound property (read in a Func), UpdateValue will first dispatch to bindings AND then return false to trigger a reload. The doc describes this three-way routing correctly in §5.2 but the §4.2 statement could mislead readers into thinking lambda bindings never cause rebuilds.

### 4. PopulateFromEnvironment fallback order is slightly wrong (§9.4, lines 1534-1538)

**Claim:** The fallback order is:
1. Context lookup
2. DI container
3. Hot reload replaced view
4. Uppercase-key retry

**Reality per `View.cs:605-661`:** The order is:
1. Context lookup (`this.GetEnvironment(key)`)
2. DI container (`mauiContext.Services.GetService(...)`)
3. Replaced view check (`viewThatWasReplaced.GetEnvironment(key)`)
4. Uppercase-key retry
5. **Another replaced view check** (line 648-651, checks `viewThatWasReplaced.GetEnvironment(item.Key)` again if still null after uppercase retry)

The doc misses the second replaced-view check.

---

## Missing Important Details 📝

### 1. `State<T>` Constructor Writes to Dictionary AND Field (important for understanding dual-storage)

`State<T>(T value)` stores in both `_value` AND `dictionary[ValuePropertyName]` (State.cs:21). But the `Value` setter only writes to `_value` (no dictionary write). The doc mentions "dual storage" but doesn't explicitly note this asymmetry — `GetValueInternal` returns the field, not the dictionary entry. After a `Value` set, the dictionary is stale. This is fine because `GetValueInternal` bypasses the dictionary, but worth noting.

### 2. `EndProperty()` Deduplication and Buffer Reuse

`StateManager.EndProperty()` (StateManager.cs:514-547) has important optimizations not mentioned:
- Single-property fast path reuses a thread-static `_endPropertyBuffer`
- Multi-property path deduplicates via `HashSet` (`_endPropertySeen`)
These affect memory behavior and correctness (duplicate reads from the same property within one Func evaluation are collapsed to one).

### 3. `ConstructingView` Also Captures Pending Reads as Global Properties

`StateManager.ConstructingView` (StateManager.cs:118-146) checks if `_currentReadProperties` has entries and adds them as global properties to `CurrentView`. This means reads that happen between `StartBuilding` and the next `EndProperty` call are captured as global. This is not described in the document.

### 4. `Disposing` Deferred Event Unsubscription

The doc shows the code but doesn't explain that event unsubscription (`obj.PropertyChanged -= Obj_PropertyChanged`) happens **outside the lock** (StateManager.cs:196-207). This is a deliberate design choice to prevent deadlocks from reentrancy during event handler removal. Worth highlighting as a concurrency consideration.

### 5. `ShouldUpdate` Is Dead Code

The document correctly notes (line 1399) that `ShouldUpdate` "exists as an extension point, but no code calls it." This is accurate but could be more prominent. Developers reading §8.3 might assume `ShouldUpdate` is functional.

### 6. `OnPropertyChanged` Ignores View-typed Values

`StateManager.OnPropertyChanged` (StateManager.cs:386-387) has an early return: `if (value?.GetType() == typeof(View)) return;`. This prevents views from being treated as state values. The document doesn't mention this edge case.

---

## Code Snippet Accuracy

| Section | Lines | Verdict | Notes |
|---------|-------|---------|-------|
| §2.1 INotifyPropertyRead | 66-70 | ✅ Exact | |
| §2.1 CallPropertyRead/Changed | 77-89 | ✅ Exact | |
| §2.2 BindingObject | 100-133 | ✅ Exact | |
| §2.3 State\<T\> | 144-194 | ✅ Exact | |
| §2.4 Binding base | 229-249 | ✅ Exact | |
| §2.4 Binding\<T\> | 253-266 | ✅ Exact | |
| §2.5 BindingState | 301-369 | ✅ Exact | |
| §3.1 CheckForStateAttributes | 392-419 | ✅ Exact | |
| §3.2 RegisterChild | 446-464 | ✅ Exact | |
| §3.2 ResolvePropertyName | 472-484 | ✅ Exact | |
| §3.3 CheckForBody | 496-508 | ✅ Exact | |
| §4.2 ProcessGetFunc | 566-576 | ✅ Exact | |
| §5.2 UpdateValue | 643-682 | ✅ Exact | |
| §5.3 BindingPropertyChanged | 696-720 | ✅ Exact | |
| §5.4 OnPropertyChanged multi-view | 730-807 | ✅ Exact | |
| §6.1 Binding\<T\>(T) operator | 831-898 | ✅ Exact | |
| §6.1 BindToProperty IsValue branch | 903-944 | ✅ Exact | |
| §7.1 StateManager batching | 1074-1115 | ✅ Exact | |
| §7.2 BindingValueChanged batching | 1150-1163 | ✅ Exact | |
| §7.3 View-level batching | 1174-1212 | ✅ Accurate | |
| §8.1 Component | 1228-1265 | ✅ Exact | |
| §8.2 Component\<TState\> | 1277-1323 | ✅ Exact | |
| §8.3 Component\<TState,TProps\> | 1355-1392 | ✅ Exact | |
| §9.3 SetGlobalEnvironment | 1494-1504 | ✅ Exact | |
| §9.4 SetEnvironmentFields | 1516-1527 | ❌ Wrong | `SetEnvironmentField(f, key)` should be `State.AddGlobalProperty(...)` |
| §10.2 TransferHotReloadStateTo | 1568-1598 | ❌ Fabricated | Does not match actual source; see Error 1 |
| §10.3 IComponentWithState | 1610-1616 | ❌ Wrong access | `public`, not internal |
| §11.2 Dispose | 1657-1673 | ❌ Wrong | Wrong modifier, wrong guard, wrong calls; see Error 5 |
| §11.3 Weak references | 1738-1765 | ✅ Exact | |
| §11.4 ArrayPool | 1776-1797 | ✅ Exact | |
| §11.5 Single-view fast path | 1817-1841 | ✅ Exact | |
| §12.1 ThreadStatic fields | 1852-1855 | ✅ Exact | |
| §12.3 OnPropertyRead | 1917-1923 | ✅ Exact | |
| §13.1 AutoNotifyGenerator | 1940-1964 | ❌ Fabricated | Claims it processes `[Body]`; it processes `[AutoNotify]` fields |

---

## Final Verdict

### Is this document accurate enough to serve as the definitive reference?

**Not yet.** The document is ~85% accurate and impressively thorough in its coverage of the core state management system. The verified sections (§1-§8, most of §9, §11.3-§12) are excellent and closely match the source. However, there are **6 factual errors**, including two fabricated code blocks and a fundamentally wrong description of the source generator.

### Required Edits Before This Can Be Trusted

**Must fix (blocking):**

1. **§10.2** — Replace fabricated `TransferHotReloadStateTo` code with actual implementation. Move handler/environment transfer claims to `SetHotReloadReplacement` description.
2. **§13.1** — Completely rewrite. `AutoNotifyGenerator` processes `[AutoNotify]` fields, NOT `[Body]` methods. Remove fabricated generated output.
3. **§3.3, line 511** — Remove false claim about source generator processing `[Body]` methods.
4. **§11.2** — Replace incorrect `Dispose` code block with actual implementation or faithful simplification.
5. **§10.3** — Fix `IComponentWithState` from "internal" to "public".
6. **§9.4** — Fix `SetEnvironmentFields` code to show `State.AddGlobalProperty(...)` instead of nonexistent `SetEnvironmentField(f, key)`.

**Should fix (recommended):**

7. Add note about `PopulateFromEnvironment` having a second replaced-view fallback check.
8. Mention `OnPropertyChanged`'s early-return for View-typed values.
9. Note that `EndProperty()` deduplicates properties and uses thread-static buffer reuse.
10. Make `ShouldUpdate` dead-code status more prominent in §8.3.

Once items 1-6 are addressed, this document can serve as the definitive reference.
