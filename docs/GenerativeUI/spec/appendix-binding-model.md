# Appendix: Dynamic Binding Model (generic view model)

> **Status:** Draft (v0.1) — for iteration. See the [Open Questions](#open-questions).
> Parent: [`overview.md`](./overview.md). Related: [UI-DSL](./appendix-ui-dsl.md),
> [Extensibility](./appendix-extensibility.md).

## 1. The problem

MAUI data binding expects a `BindingContext` object with **properties** and **change
notification**. But in Generative UI there are **no hand-authored view models**: the model
produces *data* of arbitrary shape (a product, a cart, a form the user is filling in) and the shape
isn't known at compile time. We can't write a `ProductViewModel` for every shape the model might
invent.

So we need a **generic, observable data context** the inflator can bind any DSL document to — one
built at runtime from the JSON the model supplies (`data`/`form`) or from REST responses. The DSL's
`bind`/`key` paths compile to bindings into this generic tree. This is the client-side "in-memory
model" the app is otherwise missing.

## 2. The generic bindable tree

A small tree of observable nodes — the runtime substitute for a typed VM. (Conceptually the
`DynamicObject` / `DynamicObjectCollection` shape from the design notes, renamed to avoid clashing
with `System.Dynamic.DynamicObject`.)

```csharp
// One node: a scalar leaf, an object (via the indexer), or a list (via Children).
public sealed class UiObject : INotifyPropertyChanged   // or : BindableObject
{
    public string? Name { get; init; }

    // Scalar value — two-way bindable; raises PropertyChanged on set.
    public object? Value { get; set; }

    // Object member access: root["product"]["name"].
    public UiObject this[string key] { get; }

    // Array / list members: bound as CollectionView.ItemsSource.
    public UiObjectCollection Children { get; }

    // Typed convenience accessors used by converters/inflator.
    public string?  AsString();
    public double?  AsNumber();
    public bool?    AsBool();
}

public sealed class UiObjectCollection : ObservableCollection<UiObject>
{
    public UiObject Get(string key);   // by Name, for keyed access
}
```

- **Two regions, one mechanism.** The DSL keeps its `data` (one-way, read-only) vs `form` (two-way,
  editable) split, but **both are backed by `UiObject` trees**. `data` is (re)built from API/model
  JSON each render; `form` persists user/model edits across renders.
- **Observable throughout.** Setting a `Value` raises `PropertyChanged`; adding/removing a
  `UiObjectCollection` item raises collection-changed. So `set_field(...)`, user typing, and
  model-driven updates all flow to the screen with no re-inflation.
- This tree — not a per-shape VM — is what the inflator assigns as `BindingContext`.

## 3. Why not `System.Dynamic.DynamicObject`

The DLR (`DynamicObject`, `ExpandoObject`, `dynamic`) is **unreliable under the iOS interpreter and
NativeAOT** and is reflection-heavy. MAUI's binding engine, by contrast, supports **indexer
bindings** (`[key]`) and `INotifyPropertyChanged` first-class and AOT-friendly. So the binding
*substrate* is explicit **indexer + change notification**, which is deterministic and portable. A
`dynamic`/`DynamicObject` convenience façade could be layered on later for ergonomics, but it is not
what the UI binds to.

## 4. Binding paths (DSL → MAUI)

The inflator compiles DSL paths into indexer bindings against the root `UiObject`:

| DSL | Compiles to | Direction |
|---|---|---|
| `"bind": "product.name"` | `Binding` on path `[product][name].Value` against `data` root | one-way |
| `"key": "quantity"` (a `Field`/editable prop) | `Binding` on `[quantity].Value` against `form` root, `TwoWay` | two-way |
| `"bind": "product.imageUrl"` on a component prop | same, into the component's bindable target property | one-way |

- **Dot-paths → indexer chains + `.Value`.** `a.b.c` becomes `[a][b][c].Value`.
- **Missing paths auto-vivify** an empty placeholder `UiObject` (null `Value`) rather than throwing;
  displayed as empty and logged.
- **Collections (future `itemsBind`).** `"itemsBind": "products"` → `ItemsSource =
  root["products"].Children`; each row is a `UiObject` and the item template's inner `bind`s resolve
  against the row. (MVP still pre-expands rows for reliability — see
  [UI-DSL §4](./appendix-ui-dsl.md).)

## 5. Populating the tree

- **From `render_ui`.** The `data` JSON object is walked into a `UiObject` tree; the `form` object
  seeds editable leaves.
- **From REST responses.** A typed model (deserialized via the app's `JsonSerializerContext`) or a
  raw `JsonElement` is walked into `UiObject`s by the same builder, so display bindings work whether
  the model passed `data` inline or the inflator pulled it from an API result.
- **`set_field(key, value)`** sets a `form` leaf's `Value` on the UI thread → `PropertyChanged` →
  the bound `Entry` updates.
- **`get_state()`** serializes the `form` subtree back to a JSON object for `write_api`.

## 6. Type coercion

Leaves store `object?`. Editable `Field`s and component props declare a `UiPropType`
(`string`/`number`/`bool`/`enum`/date), so the inflator attaches a value converter: `Entry` text
round-trips to a typed JSON value in `get_state()`, and numeric/bool `data` renders correctly.
Coercion lives at the **edges** (converters), keeping the tree itself untyped and simple.

## 7. Components & views

- **Components** receive their prop values through the *same* tree: one-way `bind` and two-way `key`
  resolve exactly as above onto the component's bindable target properties. A component may host its
  own internal, real VM, but its **inputs arrive via generic-tree bindings** — it never needs a
  bespoke context from the model.
- **Views** are self-contained: they bring their **own real VM and DI services** and self-load bulk
  data, so they generally don't use the generic tree at all (they may accept a `DataContract`
  instance built from it). See [Extensibility §3.3](./appendix-extensibility.md#33-views-full-custom-views).

## 8. Change, re-inflation & persistence

- Because the tree is observable, **most updates need no re-inflation** — values change in place.
- On a **catalog or render-context change** (see
  [Extensibility](./appendix-extensibility.md#22-context-conditional-resolution)), the current DSL
  document is **re-inflated against the same persistent tree**, so bindings re-attach and **in-progress
  form values survive** the re-render.
- The `form` tree is owned by `CanvasState` for the life of the surface; a new chat/`clear_ui`
  resets it.

## Open questions

1. **Path syntax.** Compile dot-paths to `[a][b].Value` indexer bindings (portable, verbose) vs. a
   custom `IValueConverter`/`BindingBase` that walks a path against `UiObject` directly (cleaner,
   more code)?
2. **Typed vs stringly leaves.** Store typed `Value`s (number/bool/date) in the tree, or keep
   everything string and coerce only at converters/`get_state`?
3. **`data` mutability.** Rebuild `data` immutably on each render (only `form` observable), or make
   both observable so partial API updates can patch the tree in place?
4. **Eager vs lazy.** Materialize the whole tree from a large API payload up front, or lazily
   create `UiObject`s on first bind for big/nested responses?
5. **Collections.** When `itemsBind` lands, how do we key/diff `UiObjectCollection` items for
   virtualization and stable selection on large lists?
6. **Two-region vs single root.** Keep separate `data` and `form` roots, or a single root with
   reserved subtrees? (Current lean: two roots, matching the DSL split.)
