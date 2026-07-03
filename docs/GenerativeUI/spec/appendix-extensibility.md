# Appendix: Extensibility — Styles, Components & Views

> **Status:** Draft (v0.1) — for iteration. See the [Open Questions](#open-questions).
> Parent: [`overview.md`](./overview.md). Related: [UI-DSL](./appendix-ui-dsl.md),
> [Binding Model](./appendix-binding-model.md).

The built-in DSL covers generic primitives (labels, buttons, entries, cards, …). Real apps need
more: **brand styling**, **bespoke composite controls** (a watermarking product-image presenter),
and **whole pre-built views** that must be used as-is (a checkout/payment surface, a monthly orders
report). This appendix defines how an app **extends** the generative vocabulary and how the model
**discovers and uses** those extensions.

The guiding rule: the DSL is **closed but extensible**. Closed for reliability and validation;
extensible so each app can register its own vocabulary. The **library hardcodes none of it** — all
app-specific styles/controls/views live in the app. The model only *selects* registered names and
supplies *declared inputs*; it never authors styling or code.

> **MVP simplicity.** Each registration is just a **name/alias + a description** (plus, for
> components/views, a small prop/input list). There is **no policy engine, no "renderer" concept,
> and no structured "applies-to / must-use / do-not-use" fields** — all such guidance lives in the
> freeform **description**. The **entire catalog is sent to the model** (seeded into the system
> prompt), so it reads the descriptions and decides when to use what. Richer enforcement can come
> later; see [Open Questions](#open-questions).

## 1. The three tiers

| Tier | What it is | How it appears in the DSL | Identified by | The model's job |
|---|---|---|---|---|
| **Style** | A named visual variant mapped to a XAML resource. | `style` token(s) on any node. | name / key | Pick a token per its description. |
| **Component** (custom control) | An app composite control (frame, layers, watermark, custom input). | A node `type` with a `props` object. | type name + alias | Compose it into the generated tree. |
| **View** (full custom view) | A whole app-owned surface/screen. | The `present_view` tool, or a `View` node. | type name + alias | Gather declared inputs, then hand off. |

The escalating idea: **Style** tweaks an existing control; **Component** replaces one node with a
bespoke control; **View** replaces the whole surface with an app-owned screen.

## 2. The registry

A single small service — `GenerativeUiRegistry` — lives in DI. It is a plain **mutable collection**
of registrations, populated two interchangeable ways:

- **During builder setup**, via `AddGenerativeUi(options => options.Ui.Add…)`.
- **Any time after startup**, by resolving `GenerativeUiRegistry` from DI and calling
  `Add…`/`Remove…` (e.g. after sign-in, when new capabilities become available).

```csharp
// startup
builder.Services.AddGenerativeUi(options =>
{
    options.BaseAddress = new Uri("http://localhost:5225");
    options.JsonSerializerContext = GardenJsonContext.Default;

    options.Ui.AddStyle("danger",
        "Destructive actions — delete, remove, clear. Signals irreversible intent.",
        resourceKey: "DangerButtonStyle");

    options.Ui.AddComponent<ProductImageView>("ProductImage",
        "Product image with brand frame and automatic licensing watermark. " +
        "Use for any product image so watermarking is applied.");

    options.Ui.AddView<CheckoutView>("CheckoutView",
        "Official checkout + payment surface. Use when the user checks out or pays; " +
        "never build a custom checkout. Do not use when the cart is empty.");
});
```

```csharp
// later — from anywhere the registry is injected
public sealed class SessionUi(GenerativeUiRegistry ui)
{
    public void OnManagerSignIn()
    {
        ui.AddView<AdminOrdersView>("AdminOrdersView",
            "All-orders admin view. Only when signed in as a manager.");
        ui.AddComponent<BulkPriceEditor>("BulkPriceEditor",
            "Edit many product prices at once. Manager only.");
    }

    public void OnSignOut()
    {
        ui.Remove("AdminOrdersView");
        ui.Remove("BulkPriceEditor");
    }
}
```

- **Add/remove any time.** The registry is just a mutable list; whatever is registered when the
  system prompt (and per-app schema) is built is what the model sees that turn. The MVP does **not**
  version the catalog or push change events — the next prompt simply reflects the current registry.
- **Descriptions are freeform and never clipped** — the same principle applied to API descriptions
  (see the [OpenAPI appendix §3](./appendix-openapi-processor.md#3-reduction-openapireducer)). They
  carry the *when / when-not* guidance in prose, replacing structured rule fields.
- **Send-all.** The whole catalog (names, aliases, descriptions, prop/input lists) is small and
  app-authored, so it is **seeded to the model** wholesale; no lazy discovery is required (optional
  `describe_*` tools remain available — §4).

### 2.1 Theming & device context

Dark/light, screen size, orientation, and accessibility are handled by the **XAML resource a style
maps to** (`AppThemeBinding`, `VisualStateManager`, `OnIdiom`, adaptive triggers) — **not** by the
registry or the model. The model picks `danger` once; the app's XAML resolves the right look per
context. No special mechanism is needed for the MVP, and the model never reasons about theme or
screen size.

### 2.2 Registration sources

The MVP is **imperative** — `AddStyle` / `AddComponent<TView>` / `AddView<TView>`. Later, a source
generator (or reflection over `[UiComponent]`-style attributes and `ResourceDictionary` entries) can
register directly from XAML; every path produces the same registry entries, so discovery and
validation don't care how something was registered.

## 3. The tiers in detail

Descriptors carry **full descriptions** that are surfaced to the model **verbatim and never
clipped** — this is how the model knows *when* and *why* to use each extension.

### 3.1 Styles

A style maps a **model-facing name** to a XAML resource (`Style`, `Color`, thickness, …).

```csharp
options.Ui.AddStyle("primary",
    "Emphasized call-to-action — the single main action in a view.",
    resourceKey: "PrimaryButtonStyle");

options.Ui.AddStyle("danger",
    "Destructive action (delete, remove, clear). Signals irreversible intent.",
    resourceKey: "DangerButtonStyle");

options.Ui.AddStyle("Brand",
    "Brand accent color for emphasis text and badges.",
    resourceKey: "BrandAccentColor");     // a Color resource
```

- The library **pre-registers a base set** (`Title`/`Body`/… , `primary`/`secondary`/`danger`,
  badge tones). Apps add to it or override by name.
- `resourceKey` defaults to the `name` when omitted, so exposing an existing XAML style is one line.
- In the DSL: `"style": "primary"` or a list `"style": ["Brand", "large"]` (composes a `Style` +
  MAUI `StyleClass`es).
- Unknown or misapplied tokens fall back to a sensible default and are logged — never an error. The
  **description** tells the model where a token is meant to be used (e.g. "for buttons").

### 3.2 Components (custom controls)

A component is an app composite control exposed as a **node type**. It is identified by its **view
type** (used to create it via DI) and a short **alias** the model can emit, plus a **description**
and a small **prop** list.

```csharp
options.Ui.AddComponent<ProductImageView>("ProductImage",
    "Product image presenter: brand frame, rounded corners, and an automatic licensing " +
    "watermark. Use for ANY product image so watermarking is applied.",
    props:
    [
        new UiProp("source",  "Image URL or resource key for the product."),
        new UiProp("caption", "Optional caption shown beneath the image."),
    ]);

options.Ui.AddComponent<StarRatingView>("StarRating",
    "Interactive 1–5 star rating control.",
    props: [ new UiProp("value", "Selected rating.", Editable: true) ]);
```

- **Props** are a light list: each has a `name`, a `Description`, and an optional `Editable` flag
  (two-way into the form) and `Type` (for coercion, default string). No required/enum ceremony in
  the MVP.
- **Creation** is via **DI** (`ActivatorUtilities.CreateInstance<TView>`), so components can take
  constructor services. The model never provides code.
- **In the DSL**, a component is an ordinary node whose `props` object supplies values (literals,
  `{ "bind": "path" }` for one-way, `{ "key": "formKey" }` for two-way editable props):

```jsonc
{ "type": "ProductImage",
  "props": { "source": { "bind": "product.imageUrl" }, "caption": "Heirloom Tomato" } }
```

```jsonc
{ "type": "StarRating", "props": { "value": { "key": "rating" } } }
```

- The model may reference a component by its **alias** (`ProductImage`) or its **full type name** —
  both resolve to the same registration.
- Registration **validates** aliases don't shadow built-ins (or require an explicit override).

### 3.3 Views (full custom views)

A view is a **whole app-owned surface** the model hands off to. Unlike a component, the model does
**not** compose its internals; it selects the view and supplies declared inputs. Views **self-load
bulk data** through their own VM/services (the same API/HttpClient), so the model needn't pass large
payloads. A view is identified by its **type** (created via DI) and an **alias**, with a
**description** and optional **inputs**.

```csharp
options.Ui.AddView<CheckoutView>("CheckoutView",
    "The official checkout and payment surface: full cart, totals, shipping, and payment UI. " +
    "Use when the user is checking out or paying. Never compose a custom checkout UI. " +
    "Do not use when the cart is empty — ask the user to add items first.");

options.Ui.AddView<MonthlyOrdersReportView>("MonthlyOrdersReport",
    "Full-screen monthly orders report: filterable and printable. " +
    "Use when the user asks for an orders report or monthly summary.",
    inputs:
    [
        new UiProp("month",     "Report month in YYYY-MM."),
        new UiProp("verbosity", "Detail level: summary or detailed."),
    ]);
```

- **Presented** via the **`present_view`** tool, which takes over the canvas, or embedded as a
  `View` node inside a larger generated layout. Full-canvas is the MVP default; region/overlay/
  persistent hosting is future.
- **Inputs** use the same light schema as component props; the model supplies only these.
- The **description** carries all usage guidance (must-use / do-not-use) in prose.

```jsonc
{ "view": "CheckoutView", "inputs": {} }
```

```jsonc
{ "view": "MonthlyOrdersReport", "inputs": { "month": "2026-06", "verbosity": "detailed" } }
```

## 4. How the model discovers extensions

The **UI capability catalog** — every registered style, component, and view, each with its **full
description** — is **seeded into the system prompt** at session start ("send-all"). The catalog is
app-authored, small, and stable, so seeding it wholesale is cheap and improves first-try
correctness.

Optional discovery tools mirror the API side for larger catalogs or refreshes:

- `list_ui_capabilities()` → styles, components, views (names + aliases + descriptions).
- `describe_component(name)` → prop list.
- `describe_view(name)` → input list + description.

All catalog text is passed **verbatim and in full** (no clipping) — it's authored intent the model
needs to act correctly.

## 5. Schema & validation

- The **valid node-type set** = built-ins + registered components + `View`; the **valid style set**
  = base + registered styles; the **valid view set** = registered views — read from the registry's
  **current** state.
- The library can emit a **per-app `render_ui` JSON Schema** (enums populated from the registry) for
  structured output/validation — see
  [UI-DSL appendix §11](./appendix-ui-dsl.md#11-draft-json-schema-sketch).
- **Inflation order:** built-in → registered component/view → unknown ⇒ graceful placeholder
  (never throw). Component `props` and view `inputs` are validated against their declared lists
  before the view is created.

## 6. Binding & state

- **Component props:** literal, one-way `bind` (into `data`), or two-way `key` (into the form) for
  editable controls — reusing the DSL binding model
  ([UI-DSL §5](./appendix-ui-dsl.md#5-binding-model)), backed by the generic observable tree in the
  [Binding Model appendix](./appendix-binding-model.md). Since the model authors no view models,
  component inputs arrive through that generic tree.
- **Views:** receive resolved `inputs`; they own their VM and load their own bulk data via services
  (same app auth/HttpClient), so the model passes parameters, not datasets.
- All view creation marshals to the UI thread during inflation/hosting.

## 7. Security & trust

- **No model-authored code or markup.** Components and views are compiled app C# created via DI. The
  model only chooses registered names and supplies inputs validated against declared lists.
- **Views self-load via app services** (app auth/HttpClient), never from model-supplied URLs.
- **Input validation** happens before any view is created; invalid props degrade to placeholders.

## 8. Versioning

- The core grammar is versioned by the DSL `schemaVersion`
  ([UI-DSL §9](./appendix-ui-dsl.md#9-versioning)).
- The **extension catalog** is owned by the app and is **mutable**; whatever is registered when a
  turn's prompt/schema is built is what the model sees. No separate catalog version in the MVP.

## 9. Worked examples

### 9.1 Register (startup + runtime)

```csharp
// startup
options.Ui.AddStyle("hero", "Oversized primary CTA.", resourceKey: "HeroButtonStyle");
options.Ui.AddComponent<ProductImageView>("ProductImage", "…", props: [ /* … */ ]);
options.Ui.AddView<CheckoutView>("CheckoutView", "…");

// later (e.g. after manager sign-in), via the injected GenerativeUiRegistry
ui.AddView<AdminOrdersView>("AdminOrdersView", "All-orders admin view. Manager only.");
// …and on sign-out
ui.Remove("AdminOrdersView");
```

### 9.2 Model uses a registered component

```jsonc
{ "type": "ProductImage",
  "props": { "source": { "bind": "product.imageUrl" }, "caption": { "bind": "product.name" } } }
```

### 9.3 Model hands off to a full view

```jsonc
// present_view — checkout takes over the canvas; the view loads the cart itself
{ "view": "CheckoutView", "inputs": {} }
```

## Open questions

1. **Alias vs. full type name:** allow both (prefer the short alias for the model), or require one?
   How do we avoid alias collisions across registrations?
2. **Prop shape:** is `name + description` (+ optional `Editable`/`Type`) enough, or do we need
   `required`/`enum` for reliable validation and coercion?
3. **Catalog delivery:** send-all is fine for small catalogs — at what size do we switch to lazy
   `describe_*`? Do we ever need both in one session?
4. **Component composition:** may components accept children/slots (e.g. a `Panel` wrapper) in the
   MVP, or are they leaves only?
5. **Overriding built-ins:** allow apps to replace a built-in style/component by name, or keep
   built-ins immutable?
6. **View data contracts:** how does a view express "I self-load X" vs. "the model must give me Y"
   so the model knows what to gather? (MVP: prose in the description + declared `inputs`.)
7. **View hosting:** MVP presents views full-canvas. When do we need region/overlay/persistent
   hosting, and who owns layout/z-order then?
8. **Runtime changes mid-render:** if a component/view is removed while a surface using it is on
   screen, do we leave the rendered instance, show a placeholder, or re-inflate?
9. **Enforcement (post-MVP):** prose descriptions ("always watermark", "never build a custom
   checkout") rely on the model complying. If we later need a *guarantee* (e.g. licensing), do we
   reintroduce a light host-enforced policy for a small set of cases?
