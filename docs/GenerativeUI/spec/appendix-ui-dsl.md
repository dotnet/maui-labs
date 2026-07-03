# Appendix: UI-DSL & Inflator

> **Status:** Draft (v0.1) — for iteration. See the [Open Questions](#open-questions).
> Parent: [`overview.md`](./overview.md).

This appendix defines the **UI description language** the model emits and the **inflator** that
turns it into MAUI controls. The design bias is **reliability over expressiveness**: a small,
closed vocabulary the model can use predictably, with graceful degradation when it doesn't.

## 1. Design principles

1. **Closed vocabulary.** A fixed set of node `type`s. Unknown types render a visible error
   placeholder, never crash.
2. **Flat, JSON-native.** Plain JSON objects/arrays; no expressions, no code, no styles beyond a
   fixed token set. Easy for a model to emit and for us to validate.
3. **Declarative + data-bound.** Nodes describe *what*, not *how*. Editable nodes bind two-way to
   `FormState`; display nodes may bind one-way to `data`.
4. **Deterministic inflation.** One document → one deterministic view tree. No layout ambiguity.
5. **Forgiving.** Missing optional props use sane defaults; extra props are ignored.

## 2. `render_ui` payload

`render_ui` takes one document:

```jsonc
{
  "schemaVersion": 1,          // required; DSL version the doc targets
  "ui": { /* root UiNode */ }, // required; the view tree
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
  "type": "Label",     // required; one of the closed vocabulary
  "id": "title",       // optional; for targeting/debugging
  "bind": "product.name", // optional; one-way path into `data`
  "style": "Title",    // optional; a fixed style token
  "children": [ ... ], // optional; for container nodes
  // ...type-specific props
}
```

Common fields: `type`, `id`, `bind`, `style`, `children`. Type-specific props are listed per
node below.

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

## 5. Binding model

Three sources of values:

1. **Literal props** — e.g. `"text": "Products"`. Always available.
2. **One-way `bind`** — `"bind": "product.price"` resolves a dotted path into the `data` object
   supplied to `render_ui`. Used by display nodes (`Label`, `Image`, `Badge`).
3. **Two-way `Field`/`Entry`** — bound to `FormState[key]`. The bound `Entry` reflects
   `set_field(key,value)` immediately, and its edits are read by `get_state()`.

### `FormState`

- Backed by an `INotifyPropertyChanged` key/value store (`IDictionary<string, object?>` with
  change notifications), so MAUI two-way bindings work without a statically typed VM.
- Seeded from the `form` object in the `render_ui` payload.
- `set_field(key, value)` updates it on the UI thread → the on-screen control updates.
- `get_state()` serializes it back to a JSON object for the model to send to `write_api`.

### `data` resolution

- Dotted paths (`a.b.c`); array indexing (`items.0.name`) is **out of scope** for the MVP (use
  pre-expanded `List` rows instead).
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

A **fixed** token set (no arbitrary colors/sizes from the model) keeps output on-brand and
predictable. Tokens map to `StaticResource`s in the app theme.

- Text styles: `Title`, `Subtitle`, `Body`, `Caption`, `Mono`.
- Button styles: `primary`, `secondary`, `danger`.
- Badge tones: `neutral`, `positive`, `warning`, `danger`.
- Spacing/padding: small integers interpreted as device-independent units.

Unknown tokens fall back to `Body`/`secondary`/`neutral`.

## 8. Validation & error handling

- **Parse errors** (malformed JSON): render an error card with the raw text (truncated) and log;
  return a tool error so the model can retry.
- **Unknown `type`**: render a labeled placeholder ("Unsupported: <type>") in place of that node;
  continue inflating siblings.
- **Missing required props** (e.g. `Field` without `key`): render a placeholder for that node.
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

### 10.4 Confirm delete

`show_confirm` produces an overlay; conceptually equivalent DSL:

```jsonc
{
  "schemaVersion": 1,
  "ui": {
    "type": "Card",
    "children": [
      { "type": "Label", "text": "Delete Heirloom Tomato Seeds?", "style": "Subtitle" },
      { "type": "Label", "text": "This cannot be undone.", "style": "Caption" },
      { "type": "Stack", "orientation": "horizontal", "spacing": 8, "children": [
        { "type": "Button", "text": "Cancel", "style": "secondary", "intent": "cancel" },
        { "type": "Button", "text": "Delete", "style": "danger",    "intent": "confirm" }
      ]}
    ]
  }
}
```

## 11. Draft JSON Schema (sketch)

A machine-checkable schema will live alongside this doc (e.g. `schemas/ui-dsl.schema.json`) once
the vocabulary settles. Sketch of the top level:

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
        "type": { "enum": ["Stack","Card","Scroll","Separator","Spacer","Label","Image","Badge","Icon","Button","Field","Entry","List"] },
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
   data for read-only views (simpler, more tokens)?
4. **Node set:** is the §4 catalog the right MVP set? Do we need `Grid`, `Toggle`, `Picker`,
   `Slider`, tabs, tables now or later?
5. **Styling:** are the fixed tokens sufficient, or does the model need limited color/size
   control? How do we keep it on-brand?
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
