# Appendix: State & Binding Model (generic view model + JSON Patch)

> **Status:** Implemented (v0.2). Supersedes the two-root draft.
> Parent: [`overview.md`](./overview.md). Related: [UI-DSL](./appendix-ui-dsl.md),
> [Extensibility](./appendix-extensibility.md),
> [Protocol Alignment](./appendix-protocol-alignment.md).

## 1. The problem

MAUI data binding expects a `BindingContext` object with **properties** and **change
notification**. But in Generative UI there are **no hand-authored view models**: the model
produces *data* of arbitrary shape (a product, a cart, a form the user is filling in) and the shape
isn't known at compile time. We can't write a `ProductViewModel` for every shape the model might
invent.

So we need a **generic, observable state graph** the inflator can bind any DSL document to — one
built at runtime from the JSON the model supplies or from REST responses, and **mutated in place**
as things change. The DSL's `bind`/`key` paths compile to bindings into this graph. This is the
client-side "in-memory model" the app is otherwise missing.

## 2. One persistent, observable state graph

> **Design change (v0.2).** Earlier drafts split state into two roots — a `data` tree rebuilt each
> render and a persistent editable `form` tree. **We unified them into a single persistent,
> observable `StateRoot`.** Display nodes bind to it one-way, editable `Field`/`Entry` nodes bind
> two-way, and **all changes flow through the same tree**. This is what makes the canvas *stateful*
> (see §7): the model mutates the graph and the bound UI updates with **no re-inflation**. It also
> resolves prior open questions Q3 (`data` mutability) and Q6 (two roots vs. one) in favour of a
> single, always-observable root that can be patched in place.

A small tree of observable nodes — the runtime substitute for a typed VM.

```csharp
// One node: a scalar leaf, an object (via the indexer), or a list (via Children).
public sealed class UiObject : INotifyPropertyChanged
{
    public string? Name { get; init; }

    // Scalar value — two-way bindable; raises PropertyChanged on set.
    public object? Value { get; set; }

    // Object member access: root["cart"]["total"]. Auto-vivifies a stable empty child so a
    // missing bind path resolves to an empty leaf instead of throwing.
    public UiObject this[string key] { get; }

    // Array / list members: bound as ItemsSource for itemsBind lists.
    public UiObjectCollection Children { get; }

    // Membership (patcher/tools use these; no auto-vivify).
    public bool HasMember(string key);
    public bool RemoveMember(string key);   // raises the indexer change for bindings
    public IEnumerable<KeyValuePair<string, UiObject>> Members { get; }

    // Typed convenience accessors used by converters/inflator.
    public string?  AsString();
    public double?  AsNumber();
    public bool?    AsBool();
}

public sealed class UiObjectCollection : ObservableCollection<UiObject>
{
    public UiObject? Get(string key);   // by Name, for keyed access
}
```

- **One graph, owned by `CanvasState`.** `CanvasState.StateRoot` is a single `UiObject` that lives
  for the life of the surface; a new chat / `clear_ui` replaces it. There is no separate `form`
  root — editable fields are just two-way-bound leaves of the same graph.
- **Observable throughout.** Setting a `Value` raises `PropertyChanged`; adding/removing a
  `UiObjectCollection` item raises collection-changed; `RemoveMember` raises the indexer change. So
  `set_field(...)`, `apply_patch(...)`, user typing, and model-driven updates all flow to the screen
  with **no re-inflation**.
- This tree — not a per-shape VM — is what the inflator assigns as the binding source.

## 3. Why not `System.Dynamic.DynamicObject`

The DLR (`DynamicObject`, `ExpandoObject`, `dynamic`) is **unreliable under the iOS interpreter and
NativeAOT** and is reflection-heavy. MAUI's binding engine, by contrast, supports **indexer
bindings** (`[key]`) and `INotifyPropertyChanged` first-class and AOT-friendly. So the binding
*substrate* is explicit **indexer + change notification**, which is deterministic and portable. A
`dynamic`/`DynamicObject` convenience façade could be layered on later for ergonomics, but it is not
what the UI binds to.

## 4. Binding paths (DSL → MAUI)

The inflator compiles DSL paths into indexer bindings against the `StateRoot`:

| DSL | Compiles to | Direction |
|---|---|---|
| `"bind": "cart.total"` | `Binding` on path `[cart][total].Value`, source = `StateRoot` | one-way |
| `"key": "quantity"` (a `Field`/editable prop) | `Binding` on `[quantity].Value`, source = `StateRoot`, `TwoWay` | two-way |
| `"bind": "product.imageUrl"` on a control prop | same, into the control's bindable target property | one-way |

- **Dot-paths → indexer chains + `.Value`.** `a.b.c` becomes `[a][b][c].Value`.
- **Missing paths auto-vivify** an empty placeholder `UiObject` (null `Value`) rather than throwing;
  displayed as empty and logged.
- **Collections (`itemsBind`).** A `List` with `"itemsBind": "cart.items"` binds a repeating layout
  to `StateRoot["cart"]["items"].Children`; each row's `BindingContext` is the item `UiObject`, and
  the template's inner `bind`s resolve **relative to the row** (`"bind": "name"`). This is
  implemented (see [UI-DSL §4.4](./appendix-ui-dsl.md)) — it is what lets add/remove/quantity
  changes reflect live without re-rendering.

## 5. Populating the graph

- **From `render_ui`.** The document's optional `data`/`form` objects are **merged** into the
  persistent `StateRoot` (not rebuilt), so in-progress edits survive a re-render.
- **From REST responses.** A typed model (deserialized via the app's `JsonSerializerContext`) or a
  raw `JsonElement` is walked into `UiObject`s by the same builder (`UiObjectBuilder`), so bindings
  work whether the model passed values inline or the tool pulled them from an API result.
- **`set_field(key, value)`** sets a leaf's `Value` on the UI thread → `PropertyChanged` → the bound
  control updates. It is a convenience for a single-leaf `replace`.
- **`get_state(path?)`** serializes the whole graph (or a JSON-Pointer subtree) back to JSON — for
  gathering form values to send to `write_api`, and so the model can **read before it patches**.
- **`set_state(json, path?)`** replaces the graph (or a subtree) with a snapshot.

## 6. Mutation via JSON Patch (RFC 6902)

Rather than a bespoke `mutate(path, action, value)` tool, state changes use the **JSON Patch**
standard (RFC 6902) with **JSON Pointer** paths (RFC 6901), applied **in place** to the observable
tree by `UiStatePatcher`. Because the tree is observable, a patch updates bound UI **without
re-inflation**.

```jsonc
// apply_patch
[
  { "op": "remove",  "path": "/cart/items/2" },
  { "op": "replace", "path": "/cart/items/0/quantity", "value": 3 },
  { "op": "add",     "path": "/cart/items/-", "value": { "sku": "pears", "name": "Pears", "price": 2.99, "quantity": 1 } }
]
```

- **Supported ops:** `add`, `remove`, `replace`, `move`, `copy`, `test`. Scalars set
  `UiObject.Value`; array ops mutate `Children`; object ops add/remove members — all raising the
  change notifications that flow to the canvas.
- **`-` and numeric indices** address array append/insert/remove on a `Children` collection; keys
  address object members.
- **Read-then-patch discipline.** The model calls `get_state` first so its paths match the real
  shape — the same safety pattern as `read_api` before `write_api`. A failed op returns an error so
  the model can re-read and retry; it never throws into the UI.
- **Array addressing caveat.** Positional indices (`/items/2`) shift under edits. Keyed items (by a
  stable id such as `sku`) are safer; a future refinement may prefer pointer-to-keyed-object over
  raw indices (see [Open Questions](#open-questions)).

### AG-UI compatibility (shape only, no dependency)

This is deliberately shaped like **AG-UI's shared-state model**: a full **snapshot**
(`set_state` ≈ `STATE_SNAPSHOT`) plus incremental **deltas** as JSON Patch (`apply_patch` ≈
`STATE_DELTA`, which is RFC 6902 in AG-UI too). We take **no AG-UI dependency** — the packages are
render-agnostic and not on this repo's feeds — but staying shape-compatible keeps the door open to
interop. See the [Protocol Alignment appendix](./appendix-protocol-alignment.md).

## 7. Change, re-inflation & persistence

- Because the tree is observable, **most updates need no re-inflation** — values change in place and
  `itemsBind` lists add/remove rows automatically.
- **Re-render only when the *kind* of view changes** (a product list → a cart → a form). `render_ui`
  is for structure; `apply_patch`/`set_field`/`set_state` are for data. When a new document is
  rendered, it re-inflates against the **same persistent `StateRoot`**, so bindings re-attach and
  in-progress values survive.
- The `StateRoot` is owned by `CanvasState` for the life of the surface; a new chat / `clear_ui`
  resets it.

## 8. Controls & screens

- **Controls** receive their prop values through the *same* graph: one-way `bind` and two-way `key`
  resolve exactly as above onto the control's bindable target properties. A control may host its
  own internal, real VM, but its **inputs arrive via generic-graph bindings** — it never needs a
  bespoke context from the model.
- **Screens** are self-contained: they bring their **own real VM and DI services** and self-load
  bulk data, so they generally don't use the generic graph at all. See
  [Extensibility §3.3](./appendix-extensibility.md#33-screens-full-custom-screens).

## 9. Type coercion

Leaves store `object?`. Editable `Field`s and control props declare a kind
(`string`/`number`/`bool`/multiline), so the inflator attaches a value converter where needed (e.g.
a `bool` switch). Coercion lives at the **edges** (converters + the typed `AsString/AsNumber/AsBool`
accessors), keeping the tree itself untyped and simple.

## Open questions

1. **Path syntax.** We compile dot-paths to `[a][b].Value` indexer bindings (portable, verbose).
   Revisit a custom `BindingBase` that walks a `UiObject` path directly if the indexer chains become
   a bottleneck.
2. **Typed vs stringly leaves.** We store lightly-typed `Value`s (string/number/bool) and coerce at
   the edges. Do we need richer typing (dates, decimals) in the tree, or keep coercion at converters
   and `get_state`?
3. **Collections keying / diffing.** `itemsBind` binds to a `UiObjectCollection`. For large or
   frequently-patched lists, how do we key/diff items for stable selection and efficient updates
   (vs. positional indices, which shift)? Leaning toward a stable-id convention (`sku`, `id`).
4. **Eager vs lazy.** We materialize the tree from API payloads up front. For very large/nested
   responses, do we lazily create `UiObject`s on first bind?
5. **Patch conflict / resync.** If an `apply_patch` fails mid-sequence (bad path), we return an
   error and the model re-reads. Do we need a transactional apply (all-or-nothing) or an automatic
   `STATE_SNAPSHOT`-style resync like AG-UI's, for robustness under streaming?
