# Appendix: UI-DSL & Inflator

> **Status:** Implemented (v0.2). See the [Open Questions](#open-questions).
> Parent: [`overview.md`](./overview.md). Related:
> [State & Binding Model](./appendix-binding-model.md),
> [Protocol Alignment](./appendix-protocol-alignment.md).

This appendix defines the **UI description language** the model emits and the **inflator** that
turns it into MAUI controls. The design bias is **reliability over expressiveness**: a small,
**closed-but-extensible** vocabulary the model can use predictably, with graceful degradation when
it doesn't. The base vocabulary ships in the library; apps **register** their own styles, custom
controls, and full screens on top of it — see the
[Extensibility appendix](./appendix-extensibility.md).

> **Convergence note.** This DSL was designed independently but is close in shape to Google's
> **A2UI** declarative generative-UI language (component catalog + data-bound `List` templates +
> JSON-pointer binding). We keep our own MAUI-native DSL for now and treat A2UI as a compatibility
> north-star. See the [Protocol Alignment appendix](./appendix-protocol-alignment.md).

## 1. Design principles

1. **Closed but extensible vocabulary.** A fixed set of built-in node `type`s, plus app-registered
   controls/screens known at startup. The *effective* vocabulary (built-ins + registrations) is
   validated; unknown types render a visible error placeholder, never crash.
2. **Flat, JSON-native.** Plain JSON objects/arrays; no expressions, no code. Styling is limited to
   **named tokens** (never raw colors/XAML from the model). Easy for a model to emit and validate.
3. **Declarative + data-bound.** Nodes describe *what*, not *how*. Editable nodes bind two-way, and
   display nodes may bind one-way, to the single persistent **`StateRoot`**
   (see [State & Binding Model](./appendix-binding-model.md)).
4. **Stateful, not repainted.** The canvas binds to persistent state; data changes flow through
   bindings (`apply_patch`), so `render_ui` is for *structure*, called again only when the **kind**
   of view changes.
5. **Deterministic inflation.** One document → one deterministic UI tree. No layout ambiguity.
6. **Forgiving.** Missing optional props use sane defaults; extra props are ignored.
7. **App owns the look, not the model.** Brand styling, bespoke controls, and full custom screens are
   app-authored C#; the model only *selects* registered names and supplies declared inputs.

## 2. `render_ui` payload

`render_ui` describes **structure**; it takes one document. Its optional `data`/`form` objects are
**merged into the persistent `StateRoot`** (not a throwaway per-render context), so bindings stay
live and in-progress edits survive. For **data changes**, use `apply_patch`/`set_field`/`set_state`
instead of re-calling `render_ui` (see [State & Binding Model](./appendix-binding-model.md)).

```jsonc
{
  "schemaVersion": 1,          // required; DSL version the doc targets
  "ui": { /* root UiNode */ }, // required; the UI structure
  "data": { /* object */ },    // optional; merged into StateRoot for one-way `bind` paths
  "form": {                    // optional; merged into StateRoot to seed editable fields
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
- `data` — a JSON object merged into `StateRoot`; one-way `bind` paths resolve against it.
- `form` — editable seed values merged into `StateRoot`; `Field`/`Entry` nodes bind two-way by key.
- `meta` — non-visual hints (title, future append/replace, etc.).

> **Resolved:** a **single `render_ui`** tool supports both display and editable fields (via `Field`
> nodes + `form`); there is no separate `render_form`.

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
| `Field` | label + `Entry`/`Editor`/`Switch` (by `kind`) | `key` (`StateRoot` leaf), `label`, `kind` (`text`\|`number`\|`multiline`\|`bool`), `placeholder` |
| `Entry` | bare `Entry` | `key`, `placeholder`, `kind` |

### 4.4 Collections

| `type` | Inflates to | Key props |
|---|---|---|
| `List` (bound) | repeating layout bound to a state collection | `itemsBind` (dotted path to a `StateRoot` collection) + **one** template child |
| `List` (static) | `VerticalStackLayout` of the given rows | `children` (pre-expanded row nodes) |

> **Two modes (both implemented).**
> - **Bound list — preferred for changeable data.** `"itemsBind": "cart.items"` binds a repeating
>   layout to `StateRoot["cart"]["items"].Children`; the single template child is repeated per item,
>   with each row's `BindingContext` set to the item, so inner `bind`s resolve **relative to the
>   row** (`"bind": "name"`, `"bind": "price"`). Add/remove/quantity changes made via `apply_patch`
>   then reflect **without re-rendering**. See the [Binding Model appendix](./appendix-binding-model.md).
> - **Static list — for one-off snapshots.** The model pre-expands one child node per item with
>   literal values. Simple and reliable, but does **not** update on data change. Use it only when the
>   list won't change during the turn.

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

Three sources of values, all resolving against the single persistent **`StateRoot`** (see the
[State & Binding Model appendix](./appendix-binding-model.md)):

1. **Literal props** — e.g. `"text": "Products"`. Always available.
2. **One-way `bind`** — `"bind": "cart.total"` resolves a dotted path into the state graph. Used by
   display nodes (`Label`, `Image`, `Badge`).
3. **Two-way `Field`/`Entry`** — `"key": "quantity"` two-way-binds a leaf of the same graph. The
   bound control reflects `set_field`/`apply_patch` immediately, and its edits are read by
   `get_state()`.

> **Literal vs. bind — the reliability rule.** Use a **literal `text`** for values that won't change
> during the turn (a one-off detail snapshot) — it's the most robust and the model gets it right
> every time. Use **`bind`** for anything that *can* change (cart lines, totals, quantities,
> anything you'll `apply_patch`) so the update reflects live without a re-render. Changeable lists
> should use a bound `List` with `itemsBind` (§4.4). Binding a leaf that is never patched is
> harmless; the danger is the reverse — literal text that later needs to change forces a full
> re-render.

The graph is a **generic observable tree** (there are no hand-authored view models — the model
produces data of arbitrary shape). The inflator uses it as the binding source and compiles
`bind`/`key` paths into indexer bindings against it. The full design — the
`UiObject`/`UiObjectCollection` tree, JSON Patch mutation, why not `System.Dynamic`, path
compilation, coercion, and persistence across re-inflation — is in the
[State & Binding Model appendix](./appendix-binding-model.md).

### Editable fields

- `Field`/`Entry` with a `key` two-way-bind a leaf of the `StateRoot`, so MAUI two-way bindings work
  without a statically typed VM.
- Seeded from the `form` object in the `render_ui` payload (merged into the graph).
- `set_field(key, value)` (a single-leaf replace) or `apply_patch` updates it on the UI thread → the
  on-screen control updates.
- `get_state(path?)` serializes the graph (or a subtree) to JSON for the model to send to
  `write_api`.

### Path resolution

- Dotted paths (`a.b.c`) compile to indexer chains (`[a][b][c].Value`). Per-item paths inside a
  bound `List` template resolve **relative to the row** (`"bind": "name"`); positional array
  indexing in a top-level `bind` (`items.0.name`) is **out of scope** — use a bound `List` instead.
- Missing paths resolve to empty (display) and are logged.

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

Styling is limited to **named tokens** — the model never emits raw colors, sizes, or XAML. There
are two kinds of token, resolved in this order:

1. **Base tokens (library built-ins).** `Title`/`Subtitle`/`Body`/`Caption`/`Mono` (on `Label`) and
   `primary`/`secondary`/`danger` (on `Button`) have **built-in visual treatment applied in code**
   by the inflator's `StyleApplier`. They work with **zero app setup** — no `ResourceDictionary`
   required — so the base look is consistent everywhere out of the box.
2. **App-registered tokens.** Apps register additional tokens (or override a base name) that map to
   a **`StaticResource`** (a `Style`, `Color`, thickness, …) in the app theme — e.g. a `Brand`
   accent for labels, a `hero` button treatment. Each registration carries a **name** (the token),
   a full **description** (where it's meant to be used), an **`appliesTo`** list of control types,
   and an **optional resource key** (defaults to the name). See the
   [Extensibility appendix §3.1](./appendix-extensibility.md#31-styles).

> **Implementation note (v0.2).** Base tokens are code-baked rather than resource-driven so the
> library is usable without the app shipping any XAML. Making base tokens themeable resources is a
> possible future change (see [Open Questions](#open-questions)).

- **`appliesTo` constrains where a token can go**, and is **enforced by the inflator**: a MAUI
  `Style` is `TargetType`-specific, so a `danger` button style must not land on a `Picker` or
  `Entry`. A token applied outside its `appliesTo` is dropped (the node keeps its default look) and
  logged. A node matches if its control **is that type or derives from it**.
- **Badge tone** is currently a **`tone` prop** on `Badge` (`neutral`/`positive`/`warning`/`danger`)
  with built-in colours — a convenience, *not* a registered style token. This is a small
  inconsistency with the "everything visual is a token" principle; unifying tones into the style
  catalog is a candidate future change.
- `style` accepts a **single token or a list** — `"style": "primary"` or `"style": ["Brand",
  "large"]`. List composition maps app tokens to a `Style` plus (future) MAUI `StyleClass`es;
  `StyleClass` composition is **not yet implemented**.
- The registered token catalog (names + descriptions + `appliesTo`) is given to the model (seeded
  and/or via `list_ui_capabilities`), so it knows a `danger` button style exists and picks it for
  destructive actions.

Unknown or misapplied tokens fall back to a sensible default (`Body`/`secondary`/`neutral`) and
are logged — never an error.

Spacing/padding remain small integers interpreted as device-independent units (not a style token) —
see the spacing scale in §7.1.

## 7.1 Layout & visual-design guidance (for the agent)

The vocabulary above says *what the model can emit*; this section says *how to compose it well*. The
model authors no styling, but it does choose structure — and without guidance it produces
inconsistent, ad-hoc layouts. This guidance is **generic and reusable**, so it belongs in the
**library** (seeded into the system prompt the same way the capability catalog is), with apps
layering only *brand* specifics on top. It should **not** live only in a sample app's prompt.

> **New capability (planned): a library-contributed "UI authoring guide."** The library exposes a
> block of design doctrine that `AddGenerativeUi` seeds into the prompt, so every consuming app gets
> consistent, on-brand output for free. Today this guidance lives in the Garden sample's prompt;
> promoting it into the library is a tracked follow-up.

Doctrine the guide encodes:

- **Visual hierarchy.** Item/section names → `Title`; prominent values (price, totals) → `Subtitle`
  or a `Badge`; supporting text → `Body`; metadata (category, SKU, timestamps) → `Caption`.
- **Layout patterns.** A list of things → a **bound `List`** (`itemsBind`) of `Card`s, one field per
  meaningful attribute. A single item → a `Card` of labelled fields. A form → a vertical stack of
  `Field`s with exactly **one** `primary` Save button.
- **Spacing scale.** Use a small, consistent set of device-independent units — **4 / 8 / 12 / 16** —
  for `spacing`/`padding` rather than arbitrary numbers, so rhythm stays even.
- **Action semantics.** Exactly one `primary` call-to-action per view; `danger` for destructive
  actions (delete/remove/clear); `secondary` for everything else.
- **Bind vs. literal.** Bind changeable data (§5); use literal text only for static one-offs.
- **Restraint.** Prefer fewer, well-spaced elements over dense output; let `Caption`/muted tones
  carry secondary detail so the primary content stands out.

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

Several earlier questions are now **resolved** (marked ✅); the rest remain open.

1. ✅ **One tool or two?** Single `render_ui` with `Field`/`form`; no `render_form`.
2. ✅ **Collections:** both **pre-expanded static rows** and **bound `itemsBind` templates** are
   implemented (§4.4). Bound is preferred for changeable data. Remaining sub-question: item
   keying/diffing for large lists (see [Binding Model open questions](./appendix-binding-model.md#open-questions)).
3. ✅ **Data binding for display:** both — literal for static one-offs, `bind` for changeable data
   (the reliability rule in §5).
4. ✅ **Partial updates:** done via `apply_patch` against the persistent state graph (no whole-canvas
   repaint for data changes). Structural partial updates (patching the UI tree by node `id`) remain
   future — see A2UI's adjacency-list model in the
   [Protocol Alignment appendix](./appendix-protocol-alignment.md).
5. **Node set:** is the §4 catalog complete enough? Candidates from A2UI parity: `Modal`, `Tabs`,
   `Slider`, `ChoicePicker`/`Picker`, `DateTimeInput`, `CheckBox`.
6. **Styling — base tokens as resources.** Base tokens are code-baked (work with zero app setup).
   Do we make them themeable `StaticResource`s, at the cost of requiring the app to ship a
   `ResourceDictionary`? Also: fold `Badge` tone into the style catalog, and implement `StyleClass`
   composition.
7. **UI authoring guide placement.** Promote the generic layout/visual-design doctrine (§7.1) from
   the sample prompt into a **library-seeded** block, so all apps get consistent output.
8. **Intents:** synthetic chat turns vs. structured tool-result events. How do we avoid loops /
   duplicate submissions?
9. **Images:** allow remote URLs (`Image.source`)? Security/perf implications; do we need an
   allowlist?
10. **Accessibility:** how do we carry semantic/automation ids and accessibility text through the
    DSL?
11. **Determinism vs. richness:** how strict should validation be — reject-and-retry on any unknown,
    or best-effort render? (Lean: best-effort with placeholders.)
