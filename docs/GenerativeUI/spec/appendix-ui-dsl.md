# Appendix: UI-DSL & Inflator

> **Status:** Draft (v0.1) — for iteration. See the [Open Questions](#open-questions).
> Parent: [`overview.md`](./overview.md).

This appendix defines the **UI description language** the model emits and the **inflator** that
turns it into MAUI controls. The design bias is **reliability over expressiveness**: a small,
**closed-but-extensible** vocabulary the model can use predictably, with graceful degradation when
it doesn't. The base vocabulary ships in the library; apps **register** their own styles, custom
controls, and full screens on top of it — see the
[Extensibility appendix](./appendix-extensibility.md).

## 1. Design principles

1. **Closed but extensible vocabulary.** A fixed set of built-in node `type`s, plus app-registered
   controls/screens known at startup. The *effective* vocabulary (built-ins + registrations) is
   validated; unknown types render a visible error placeholder, never crash.
2. **Flat, JSON-native.** Plain JSON objects/arrays; no expressions, no code. Styling is limited to
   **named tokens from a registered catalog** (never raw colors/XAML from the model). Easy for a
   model to emit and for us to validate.
3. **Declarative + data-bound.** Nodes describe *what*, not *how*. Editable nodes bind two-way to
   `FormState`; display nodes may bind one-way to `data`.
4. **Deterministic inflation.** One document → one deterministic UI tree. No layout ambiguity.
5. **Forgiving.** Missing optional props use sane defaults; extra props are ignored.
6. **App owns the look, not the model.** Brand styling, bespoke controls, and full custom screens are
   app-authored C#; the model only *selects* registered names and supplies declared inputs.

## 2. `render_ui` payload

`render_ui` takes one document:

```jsonc
{
  "schemaVersion": 1,          // required; DSL version the doc targets
  "ui": { /* root UiNode */ }, // required; the UI tree
  "data": { /* object */ },    // optional; values for one-way `bind` paths
  "form": {                    // optional; seeds FormState for editable fields
    "quantity": "1",
    "name": "Pears"
  },
  "meta": {                    // optional; hints, non-visual
    "title": "Add product",
    "replace": true            // replace canvas (default) vs. append (future)
  }
}
```

- `ui` — the root node (see §3).
- `data` — a JSON object; one-way `bind` paths resolve against it.
- `form` — initial `FormState` values; `Field` nodes bind two-way to these keys.
- `meta` — non-visual hints (title, future append/replace, etc.).

> **Open question:** do we separate `render_ui` (display) and `render_form` (editable), or keep a
> single `render_ui` that supports both via `Field` nodes + `form`? Current lean: **single tool**.

## 3. Node model

Every node is:

```jsonc
{
  "type": "Label",     // required; a built-in or app-registered type
  "id": "title",       // optional; for targeting/debugging
  "bind": "product.name", // optional; one-way path into `data`
  "style": "Title",    // optional; a registered style token, or a list e.g. ["Brand","large"]
  "children": [ ... ], // optional; for container nodes
  // ...type-specific props
}
```

Common fields: `type`, `id`, `bind`, `style`, `children`. Type-specific props are listed per
node below. `type` may be a **built-in** (this appendix) or an **app-registered control/screen**
(see the [Extensibility appendix](./appendix-extensibility.md)); the model sees one uniform set.

## 4. Node catalog (MVP)

### 4.1 Layout

| `type` | Inflates to | Key props |
|---|---|---|
| `Stack` | `VerticalStackLayout` / `HorizontalStackLayout` | `orientation` (`vertical`\|`horizontal`, default vertical), `spacing` (number), `padding` |
| `Card` | `Border` (rounded, subtle shadow) | `padding`, `children` |
| `Scroll` | `ScrollView` | single child |
| `Separator` | thin `BoxView`/line | `orientation` |
| `Spacer` | flexible gap | `size` |

### 4.2 Content

| `type` | Inflates to | Key props |
|---|---|---|
| `Label` | `Label` | `text` or `bind`, `style`, `wrap` |
| `Image` | `Image` / emoji `Label` | `source` (url) or `emoji`, `size` |
| `Badge` | pill `Border`+`Label` | `text` or `bind`, `tone` (`neutral`\|`positive`\|`warning`\|`danger`) |
| `Icon` | glyph `Label` (Fluent font) | `glyph`, `size` |

### 4.3 Interactive

| `type` | Inflates to | Key props |
|---|---|---|
| `Button` | `Button` | `text`, `intent` (see §6), `style` (`primary`\|`secondary`\|`danger`), `payload` |
| `Field` | label + `Entry`/`Editor`/`Switch` (by `kind`) | `key` (FormState key), `label`, `kind` (`text`\|`number`\|`multiline`\|`bool`), `placeholder` |
| `Entry` | bare `Entry` | `key`, `placeholder`, `kind` |

### 4.4 Collections

| `type` | Inflates to | Key props |
|---|---|---|
| `List` | `VerticalStackLayout` of the given rows | `children` (pre-expanded row nodes) |

> For the MVP a `List`'s rows are **pre-expanded** by the model (it emits one child node per
> item). This trades token cost for reliability and removes runtime item-templating. A future
> `List` may take `itemsBind` + `itemTemplate`. See [Open Questions](#open-questions).

### 4.5 Registered types (controls & screens)

Beyond the built-ins above, an app can register its own node types. These appear to the model as
ordinary `type`s with their own prop schema, and the inflator resolves them via the registry:

| `type` shape | Inflates to | Notes |
|---|---|---|
| a registered **control** name (e.g. `ProductImage`) | the app's composite control | Binds a single value or a small prop set; may be editable. `props` object carries values. |
| `Screen` | a registered **full screen** hosted inline | `screen` names the registered screen; `inputs` supplies its declared params. Larger, app-owned surface. |

Full screens are more often presented as the whole canvas via the `present_screen` tool than embedded
as a node. Registration, prop/input lists, DI creation, and discovery are specified in the
[Extensibility appendix](./appendix-extensibility.md). Examples appear in §10.6–10.7.

## 5. Binding model

Three sources of values:

1. **Literal props** — e.g. `"text": "Products"`. Always available.
2. **One-way `bind`** — `"bind": "product.price"` resolves a dotted path into the `data` object
   supplied to `render_ui`. Used by display nodes (`Label`, `Image`, `Badge`).
3. **Two-way `Field`/`Entry`** — bound to the editable `form` state. The bound `Entry` reflects
   `set_field(key,value)` immediately, and its edits are read by `get_state()`.

Both `data` and `form` are backed by a **generic observable tree** (there are no hand-authored view
models — the model produces data of arbitrary shape). The inflator sets that tree as the
`BindingContext` and compiles `bind`/`key` paths into indexer bindings against it. The full design —
the `UiObject`/`UiObjectCollection` tree, why not `System.Dynamic`, path compilation, coercion, and
persistence across re-inflation — is in the
[Dynamic Binding Model appendix](./appendix-binding-model.md).

### `form` (editable state)

- Backed by an observable `UiObject` tree (a leaf per key), so MAUI two-way bindings work without a
  statically typed VM.
- Seeded from the `form` object in the `render_ui` payload.
- `set_field(key, value)` updates it on the UI thread → the on-screen control updates.
- `get_state()` serializes it back to a JSON object for the model to send to `write_api`.

### `data` resolution

- Dotted paths (`a.b.c`) compile to indexer chains (`[a][b][c].Value`); array indexing
  (`items.0.name`) is **out of scope** for the MVP (use pre-expanded `List` rows instead).
- Missing paths resolve to empty string (display) and are logged.

## 6. Intents (control → loop)

Interactive controls raise **intents** back into the chat loop rather than calling tools
directly. An `intent` is a string name plus an optional `payload`.

Reserved intents:

| Intent | Raised by | Effect |
|---|---|---|
| `submit` | a form's submit `Button` | Posts a synthetic user turn: "The user submitted the form" + `get_state()` values, so the model calls the right `write_api`. |
| `confirm` | `show_confirm` confirm button | Signals approval so the model proceeds. |
| `cancel` | `show_confirm` cancel button | Signals rejection. |
| `action:<name>` | any `Button` | Posts "The user tapped <name>" (+ `payload`) so the model decides what to do. |

The bridge is an `IChatBridge` the library raises and the app's chat VM implements. This keeps
the loop **AI-driven**: buttons feed the model, which then explores/renders/calls as needed.

> **Open question:** synthetic chat turns vs. direct tool re-entry vs. a structured event the
> model receives as a tool result. Synthetic turns are simplest and most transparent for the MVP.

## 7. Styles

Styling is limited to **named tokens from a registered catalog** — the model never emits raw
colors, sizes, or XAML. Each token maps to a `StaticResource` (a `Style`, `Color`, thickness, …)
in the app theme, so output stays on-brand and predictable.

- The library pre-registers a **base set**: text styles `Title`/`Subtitle`/`Body`/`Caption`/`Mono`,
  button styles `primary`/`secondary`/`danger`, badge tones `neutral`/`positive`/`warning`/`danger`.
- Apps **register additional styles** (or override the base) via the registry — e.g. a `Brand`
  accent for labels, a `hero` button treatment, a multi-line vs single-line entry variant. Each
  registered style carries a **name** (the token the model uses), a full **description** (which
  says where it's meant to be used), an **`appliesTo`** list of the control types it's valid on,
  and an **optional resource key** (defaults to the name) it maps to. See the
  [Extensibility appendix §3.1](./appendix-extensibility.md#31-styles).
- **`appliesTo` constrains where a token can go.** A MAUI `Style` is `TargetType`-specific, so a
  `danger` button style must not land on a `Picker` or `Entry`. The list is both told to the model
  and **enforced by the inflator**: a token applied to a control outside its `appliesTo` is dropped
  (the node keeps its default look) and logged. A node matches if its control **is that type or
  derives from it**.
- `style` accepts a **single token or a list** — `"style": "primary"` or
  `"style": ["Brand", "large"]` — so styles can compose (mapped to a `Style` plus MAUI
  `StyleClass`es under the hood).
- The registered style catalog (names + descriptions + `appliesTo`) is given to the model (seeded
  and/or via `list_ui_capabilities`), so it knows a `danger` button style exists and picks it for
  destructive actions.

Unknown or misapplied tokens fall back to a sensible default (`Body`/`secondary`/`neutral`) and
are logged — never an error.

Spacing/padding remain small integers interpreted as device-independent units (not a style token).

## 8. Validation & error handling

- **Type resolution order:** built-in → registered control/screen → unknown. The valid set is
  known at startup (built-ins + registry), so validation is exact.
- **Parse errors** (malformed JSON): render an error card with the raw text (truncated) and log;
  return a tool error so the model can retry.
- **Unknown `type`**: render a labeled placeholder ("Unsupported: <type>") in place of that node;
  continue inflating siblings.
- **Missing/invalid props** (e.g. `Field` without `key`, or a control prop failing its declared
  list): render a placeholder for that node and log.
- **Depth/size caps**: cap node count and tree depth; beyond the cap, truncate with a notice.

The inflator **never throws** into the UI; it degrades to placeholders + logs.

## 9. Versioning

- `schemaVersion` is required in every `render_ui` document.
- The inflator supports the current version and rejects unknown majors with a friendly error.
- Additive node types/props bump the minor understanding; breaking changes bump the major.

## 10. Worked examples

### 10.1 Product list

```jsonc
{
  "schemaVersion": 1,
  "ui": {
    "type": "Stack", "spacing": 12,
    "children": [
      { "type": "Label", "text": "Products", "style": "Title" },
      { "type": "List", "children": [
        { "type": "Card", "children": [
          { "type": "Stack", "orientation": "horizontal", "spacing": 8, "children": [
            { "type": "Icon", "glyph": "🍅" },
            { "type": "Label", "text": "Heirloom Tomato Seeds" },
            { "type": "Badge", "text": "$3.49", "tone": "neutral" }
          ]}
        ]}
        /* ...one Card per product... */
      ]}
    ]
  }
}
```

### 10.2 Product detail

```jsonc
{
  "schemaVersion": 1,
  "data": { "product": { "name": "Sweet Basil Seeds", "price": "$2.49", "category": "Seeds" } },
  "ui": {
    "type": "Card",
    "children": [
      { "type": "Stack", "spacing": 6, "children": [
        { "type": "Label", "bind": "product.name", "style": "Title" },
        { "type": "Label", "bind": "product.category", "style": "Caption" },
        { "type": "Label", "bind": "product.price", "style": "Subtitle" }
      ]}
    ]
  }
}
```

### 10.3 Add-product form (bound + partially filled)

```jsonc
{
  "schemaVersion": 1,
  "form": { "name": "Pears", "category": "", "price": "", "quantity": "1" },
  "ui": {
    "type": "Stack", "spacing": 12,
    "children": [
      { "type": "Label", "text": "Add product", "style": "Title" },
      { "type": "Field", "key": "name",     "label": "Name",     "kind": "text" },
      { "type": "Field", "key": "category", "label": "Category", "kind": "text" },
      { "type": "Field", "key": "price",    "label": "Price",    "kind": "number" },
      { "type": "Field", "key": "quantity", "label": "Quantity", "kind": "number" },
      { "type": "Button", "text": "Save", "style": "primary", "intent": "submit" }
    ]
  }
}
```

Flow: user says "set the quantity to 3" → model calls `set_field("quantity","3")` → the Quantity
`Entry` shows `3`. User says "save for me" → model calls `get_state()` → `write_api("POST",
"/products", body)`. Or the user taps **Save** → `submit` intent → model does the same.

### 10.5 Registered style on a built-in (styled button)

The app registered a `hero` button style. The model just references the token:

```jsonc
{ "type": "Button", "text": "Start a bundle", "style": ["primary", "hero"], "intent": "action:bundle" }
```

### 10.6 Registered control node (watermarked product image)

`ProductImage` is an app-registered composite control (frame + auto-watermark) that binds
`source` (+ optional `caption`). Its props may be literals or `{ "bind": ... }`:

```jsonc
{
  "type": "Card",
  "children": [
    { "type": "ProductImage",
      "props": {
        "source":  { "bind": "product.imageUrl" },
        "caption": { "bind": "product.name" },
        "size": 120
      }
    },
    { "type": "Label", "bind": "product.price", "style": "Subtitle" }
  ]
}
```

The model chooses `ProductImage` here because its **description** says to use it for any product
image (so the watermark is applied) — see
[Extensibility §3.2](./appendix-extensibility.md#32-controls-custom-controls).

### 10.7 Full screen handoff (checkout)

Checkout must use the official, app-owned screen — the model does **not** compose a checkout UI. It
supplies only declared inputs (here, none — the screen self-loads the cart) and hands off, usually
via the `present_screen` tool:

```jsonc
// present_screen
{ "screen": "CheckoutScreen", "inputs": {} }
```

Embedded-in-a-layout form (a `Screen` node) is also allowed:

```jsonc
{ "type": "Screen", "screen": "CheckoutScreen", "inputs": {} }
```

## 11. Draft JSON Schema (sketch)

The schema is **generated per app at startup**: the `type` enum = built-ins **+** registered
control/screen names; the `style` enum = registered style tokens. This lets us hand the model a
schema matching exactly what *this* app supports (useful for structured output). A machine-checkable
base schema will live alongside this doc (e.g. `schemas/ui-dsl.schema.json`); the runtime augments
its enums from the registry. Sketch of the top level:

```jsonc
{
  "$id": "https://maui-labs/generative-ui/ui-dsl.schema.json",
  "type": "object",
  "required": ["schemaVersion", "ui"],
  "properties": {
    "schemaVersion": { "const": 1 },
    "ui": { "$ref": "#/$defs/node" },
    "data": { "type": "object" },
    "form": { "type": "object", "additionalProperties": { "type": ["string","number","boolean"] } },
    "meta": { "type": "object" }
  },
  "$defs": {
    "node": {
      "type": "object",
      "required": ["type"],
      "properties": {
        // built-ins + registered control/screen names, injected at startup:
        "type": { "enum": ["Stack","Card","Scroll","Separator","Spacer","Label","Image","Badge","Icon","Button","Field","Entry","List","Screen","/* …registered… */"] },
        "style": { "oneOf": [ { "type": "string" }, { "type": "array", "items": { "type": "string" } } ] },
        "props": { "type": "object" },
        "children": { "type": "array", "items": { "$ref": "#/$defs/node" } }
      }
    }
  }
}
```

## Open questions

1. **One tool or two?** `render_ui` (with `Field`/`form`) only, or split `render_ui` +
   `render_form`? (Lean: one.)
2. **Collections:** pre-expanded rows (MVP) vs. `itemsBind` + `itemTemplate`. When do we need the
   latter (large lists, live updates)?
3. **Data binding for display:** always bind display nodes to `data`, or allow literal-inlined
   data for read-only displays (simpler, more tokens)?
4. **Node set:** is the §4 catalog the right MVP set? Do we need `Grid`, `Toggle`, `Picker`,
   `Slider`, tabs, tables now or later?
5. **Styling:** the token set is now registry-driven (built-ins + app styles). Is the `style`
   string-or-list shape right, and do we need any model-controlled sizing, or is app-registered
   enough? See the [Extensibility appendix](./appendix-extensibility.md#open-questions).
6. **Intents:** synthetic chat turns vs. structured tool-result events. How do we avoid loops /
   duplicate submissions?
7. **Images:** allow remote URLs (`Image.source`)? Security/perf implications; do we need an
   allowlist?
8. **Partial updates:** MVP replaces the whole canvas. Do we need targeted updates (update node
   by `id`) for responsiveness (e.g., updating one cart line)?
9. **Accessibility:** how do we carry semantic/automation ids and accessibility text through the
   DSL?
10. **Determinism vs. richness:** how strict should validation be — reject-and-retry on any
    unknown, or best-effort render? (Lean: best-effort with placeholders.)
