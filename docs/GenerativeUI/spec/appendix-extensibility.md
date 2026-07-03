# Appendix: Extensibility — Styles, Components & Views

> **Status:** Draft (v0.1) — for iteration. See the [Open Questions](#open-questions).
> Parent: [`overview.md`](./overview.md). Related: [UI-DSL](./appendix-ui-dsl.md).

The built-in DSL covers generic primitives (labels, buttons, entries, cards, …). Real apps need
more: **brand styling**, **bespoke composite controls** (a watermarking product-image presenter),
and **whole pre-built views** that must be used verbatim (a checkout/payment surface, a monthly
orders report). This appendix defines how an app **extends** the generative vocabulary and how the
model **discovers and uses** those extensions.

The guiding rule: the DSL is **closed but extensible**. Closed for reliability and validation;
extensible so each app can register its own vocabulary at startup. The **library hardcodes none of
it** — all app-specific styles/controls/views live in the app and are registered through the
generic mechanism here. The model only *selects* registered names and supplies *declared inputs*;
it never authors styling or code.

## 1. The three tiers (plus renderers)

| Tier | What it is | How it appears in the DSL | Data it binds | The model's job |
|---|---|---|---|---|
| **Style** | A named visual variant mapped to a XAML resource. | `style` token(s) on any node. | — | Pick a token and apply it. |
| **Component** (custom control) | An app composite control (frame, layers, watermark, custom input). | A node `type` with a `props` object. | A single value or a small prop set; one-way or two-way. | Compose it into the generated tree. |
| **View** (full custom view) | A whole app-owned surface/screen. | The `present_view` tool, or a `View` node. | A declared input set; **self-loads** bulk data via DI. | Gather declared inputs, then hand off. |
| **Renderer** (policy) | A rule mapping a content/data type → a preferred or **required** component/view. | Implicit — enforced by the host. | — | Told which renderer is mandatory/preferred. |

The escalating idea: **Style** tweaks an existing control; **Component** replaces one node with a
bespoke control; **View** replaces the whole surface with an app-owned screen; **Renderer** makes
a component/view mandatory for a kind of content.

## 2. Registration model

Everything is registered at startup through the `AddGenerativeUi` options, on a fluent UI registry:

```csharp
builder.Services.AddGenerativeUi(options =>
{
    options.BaseAddress = new Uri("http://localhost:5225");
    options.JsonSerializerContext = GardenJsonContext.Default;

    options.Ui.AddStyle(/* … */);
    options.Ui.AddComponent(/* … */);
    options.Ui.AddView(/* … */);
    options.Ui.AddRenderer(/* … */);
});
```

Descriptors carry **full descriptions and usage rules** that are surfaced to the model **verbatim
and never clipped** — the same principle applied to API descriptions (see the
[OpenAPI appendix §3](./appendix-openapi-processor.md#3-reduction-openapireducer)). These texts are
how the model knows *when* and *why* to use each extension.

### 3.1 Styles

A style maps a **model-facing name** to a XAML resource (`Style`, `Color`, thickness, …) and
constrains where it may be used.

```csharp
options.Ui.AddStyle(new UiStyle
{
    Name = "primary",                       // token the model emits
    Description = "Emphasized call-to-action. Use for the single main action in a view.",
    AppliesTo = [UiNodeKind.Button],        // validation + guidance
    ResourceKey = "PrimaryButtonStyle",     // key in the app's XAML resources
});

options.Ui.AddStyle(new UiStyle
{
    Name = "danger",
    Description = "Destructive action (delete, remove, clear). Signals irreversible intent.",
    AppliesTo = [UiNodeKind.Button],
    ResourceKey = "DangerButtonStyle",
});

options.Ui.AddStyle(new UiStyle
{
    Name = "Brand",
    Description = "Brand accent color for emphasis text.",
    AppliesTo = [UiNodeKind.Label, UiNodeKind.Badge],
    ResourceKey = "BrandAccentColor",       // a Color resource
});
```

- The library **pre-registers a base set** (`Title`/`Body`/… , `primary`/`secondary`/`danger`,
  badge tones). Apps add to it or override by name.
- `Name` defaults to `ResourceKey` when omitted, so exposing an existing XAML style is one line.
- In the DSL: `"style": "primary"` or a list `"style": ["Brand", "large"]` (composes a `Style` +
  MAUI `StyleClass`es).
- `AppliesTo` is advisory to the model and enforced on inflation (a text style on a button falls
  back gracefully).

### 3.2 Components (custom controls)

A component is an app composite control exposed as a **node type** with a small typed prop schema
and a **factory** that builds the real MAUI view.

```csharp
options.Ui.AddComponent(new UiComponent
{
    Name = "ProductImage",
    Description = "Product image presenter: brand frame, rounded corners, and an automatic " +
                  "licensing watermark. Use for ANY product image so watermarking is applied.",
    Props =
    [
        new UiProp("source",  UiPropType.String, Required: true,  Bindable: true,
            Description: "Image URL or resource key for the product."),
        new UiProp("caption", UiPropType.String, Required: false, Bindable: true,
            Description: "Optional caption shown beneath the image."),
        new UiProp("size",    UiPropType.Number, Required: false, Bindable: false,
            Description: "Edge length in device-independent units. Default 96."),
    ],
    AcceptsChildren = false,
    Factory = ctx => new ProductImageView
    {
        // ctx exposes resolved prop values, binding hooks, and DI services:
        //   ctx.GetString("source"), ctx.Bind("caption", …), ctx.Services.GetService<…>()
    },
});
```

- **Props** are a closed, typed schema: `name`, `type` (`string`/`number`/`bool`/`enum`),
  `Required`, `Bindable` (one-way from `data`), `Editable` (two-way into `FormState`), and a full
  `Description`. Enums declare their allowed values.
- **Factory** — `Func<UiComponentContext, View>` — is **app-authored C#** compiled into the app.
  It runs on the UI thread during inflation and receives a context exposing resolved props, binding
  hooks, and DI services. The model never provides code.
- **In the DSL**, a component is an ordinary node whose `props` object supplies values (literals,
  `{ "bind": "path" }` for one-way, `{ "key": "formKey" }` for two-way editable props):

```jsonc
{ "type": "ProductImage",
  "props": { "source": { "bind": "product.imageUrl" }, "caption": "Heirloom Tomato", "size": 120 } }
```

- **Editable component example** (a custom star-rating that writes back):

```csharp
new UiComponent
{
    Name = "StarRating",
    Description = "Interactive 1–5 star rating control.",
    Props = [ new UiProp("value", UiPropType.Number, Required: true, Editable: true,
                  Description: "Selected rating, two-way bound to a form key.") ],
    Factory = ctx => new StarRatingView(),
};
// DSL: { "type": "StarRating", "props": { "value": { "key": "rating" } } }
```

- Registration **validates** names don't shadow built-ins (or, if overriding is allowed, requires
  an explicit `Override = true`).

### 3.3 Views (full custom views)

A view is a **whole app-owned surface** the model hands off to. Unlike a component, the model does
**not** compose its internals; it selects the view and supplies declared inputs. Views **self-load
bulk data** through their own VM/services (the same API/HttpClient), so the model needn't pass
large payloads.

```csharp
options.Ui.AddView(new UiView
{
    Name = "CheckoutView",
    Description = "The official checkout and payment surface. Shows the full cart, totals, " +
                  "shipping, and the bank/payment UI.",
    Presentation = UiPresentation.FullCanvas,     // FullCanvas | Region | Overlay | Persistent
    DataContract = "Cart",                         // a model it consumes; may be null if self-loading
    Inputs = [],                                   // declared params the model must supply (none here)
    Usage = new UiUsage
    {
        MustUseWhen  = "The user is checking out or paying. NEVER compose a custom checkout UI.",
        DoNotUseWhen = "The cart is empty — tell the user to add items first.",
    },
    Resolve = ctx => ctx.Services.GetRequiredService<CheckoutView>(),  // real ContentView + VM
});

options.Ui.AddView(new UiView
{
    Name = "MonthlyOrdersReport",
    Description = "Full-screen monthly orders report: a filterable, printable PDF-style view.",
    Presentation = UiPresentation.FullCanvas,
    Inputs =
    [
        new UiProp("month",     UiPropType.String, Required: true,
            Description: "Report month in YYYY-MM."),
        new UiProp("verbosity", UiPropType.Enum,   Required: false,
            Description: "Detail level.", EnumValues: ["summary", "detailed"]),
    ],
    Usage = new UiUsage { MustUseWhen = "The user asks for an orders report or monthly summary." },
    Resolve = ctx => ctx.Services.GetRequiredService<MonthlyOrdersReportView>(),
});
```

- **`Presentation`** controls hosting:
  - `FullCanvas` — takes over the whole canvas (checkout, report).
  - `Region` — occupies a named region alongside generated UI.
  - `Overlay` — modal over the current surface.
  - `Persistent` — always present in a scenario (e.g. a running-total bar) until dismissed.
- **`Usage`** rules (`MustUseWhen`/`DoNotUseWhen`) are given to the model verbatim so mandatory
  views (checkout) are always used and forbidden cases are avoided. The host can additionally
  **enforce** must-use (see §3.4).
- **`Inputs`** use the same typed schema as component props; the model supplies only these.
- **`Resolve`** — `Func<UiViewContext, View>` — returns a registered `ContentView`/page (with its
  own VM) from DI. The view owns its state and data loading.
- Invoked via the **`present_view`** tool (canvas root), or embedded as a `View` node inside a
  larger generated layout.

### 3.4 Renderers (type → component/view policy)

Renderers bind a **semantic content type** or **data model** to a preferred or required
component/view. This is how "product images must be watermarked" and "orders should use the receipt
view" become guarantees rather than hopes.

```csharp
options.Ui.AddRenderer(new UiRenderer
{
    ContentType = "product-image",
    Component = "ProductImage",
    Policy = UiRenderPolicy.Mandatory,
    Description = "All product imagery must use the watermarking presenter for licensing.",
});

options.Ui.AddRenderer(new UiRenderer
{
    DataModel = "Order",
    View = "OrderReceiptView",
    Policy = UiRenderPolicy.Suggested,
    Description = "Prefer the branded receipt view when displaying a single order.",
});
```

- **`Mandatory`** — enforced by the inflator's policy post-pass: if the model emits a plain
  primitive for that content type, it is **substituted** with the required component/view, and the
  model is told the rule exists so it uses it directly. Policy can't be bypassed by prompt.
- **`Suggested`** — surfaced to the model as a strong hint; the model may still choose otherwise.
- Keyed by either a `ContentType` string the app tags data with, or a `DataModel` name from the
  shared models.

## 4. How the model discovers extensions

Consistent with the OpenAPI tools. The **UI capability catalog** — every registered style,
component, view, and renderer, each with its **full description and usage rules** — is exposed two
ways (mix as needed):

- **Seeded into the system prompt** at session start. The catalog is app-authored, small, stable,
  and always relevant, so seeding it (unlike a potentially huge API) is cheap and improves first-try
  correctness.
- **Client-UI discovery tools**, mirroring the API side:
  - `list_ui_capabilities()` → styles, components, views, renderers (names + descriptions + where
    each applies).
  - `describe_component(name)` → full prop schema.
  - `describe_view(name)` → full input schema + usage rules + presentation.

All catalog text is passed **verbatim and in full** (no clipping), for the same reason API
descriptions are: it's authored intent the model needs to act correctly.

## 5. Dynamic schema & validation

- The **valid node-type set** = built-ins + registered components + `View`; the **valid style set**
  = base + registered styles; the **valid view set** = registered views. All known at startup.
- The library can emit a **per-app `render_ui` JSON Schema** (enums populated from the registry)
  for structured output/validation — see [UI-DSL appendix §11](./appendix-ui-dsl.md#11-draft-json-schema-sketch).
- **Inflation order:** built-in → registered component/view → unknown ⇒ graceful placeholder
  (never throw). **Mandatory renderers** apply as a post-pass. Component `props` and view `inputs`
  are validated against their declared schemas before the factory/resolver runs.

## 6. Binding & state

- **Component props:** literal, one-way `bind` (into `data`), or two-way `key` (into `FormState`)
  for editable controls — reusing the DSL binding model
  ([UI-DSL §5](./appendix-ui-dsl.md#5-binding-model)).
- **Views:** receive resolved `Inputs` plus an optional `DataContract` instance; they own their VM
  and load their own bulk data via services. They can read/write through the app's registered
  `HttpClient` (same auth), so the model passes parameters, not datasets.
- All factory/resolver work marshals to the UI thread during inflation/hosting.

## 7. Security & trust

- **No model-authored code or markup.** Factories and resolvers are compiled app C#. The model only
  chooses registered names and supplies inputs validated against declared schemas.
- **Policy is host-enforced.** Mandatory renderers and must-use views are applied by the host, so
  a prompt cannot skip watermarking or the official checkout view.
- **Views self-load via app services** (app auth/HttpClient), never from model-supplied URLs.
- **Input validation** happens before any factory runs; invalid props degrade to placeholders.

## 8. Versioning

- The core grammar is versioned by the DSL `schemaVersion`
  ([UI-DSL §9](./appendix-ui-dsl.md#9-versioning)).
- The **extension catalog** is owned and versioned by the app; adding styles/components/views is
  additive and surfaced through discovery, so the model always sees the current catalog.

## 9. Worked examples

### 9.1 Register once (app startup)

```csharp
options.Ui.AddStyle(new UiStyle { Name = "hero", Description = "Oversized primary CTA.",
    AppliesTo = [UiNodeKind.Button], ResourceKey = "HeroButtonStyle" });
options.Ui.AddComponent(/* ProductImage, as §3.2 */);
options.Ui.AddView(/* CheckoutView, as §3.3 */);
options.Ui.AddRenderer(/* product-image → ProductImage (Mandatory), as §3.4 */);
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

```jsonc
// present_view — report with declared inputs
{ "view": "MonthlyOrdersReport", "inputs": { "month": "2026-06", "verbosity": "detailed" } }
```

## Open questions

1. **Node namespace:** uniform `type` set (built-ins + components + `View`) vs. explicit wrappers
   (`type:"Component"`/`type:"View"`) to prevent collisions. (Lean: uniform + collision validation.)
2. **Prop value shape:** inline `{ "bind": … }` / `{ "key": … }` per prop vs. a separate `bindings`
   map per node. Which yields fewer model errors?
3. **Mandatory renderer enforcement:** silently substitute vs. reject-and-instruct the model to use
   the required type. (Lean: substitute + tell the model the rule.)
4. **Catalog delivery:** how much to seed into the system prompt vs. lazy `describe_*` — size vs.
   round-trips, especially with many components/views.
5. **Component composition:** may components accept children/slots (e.g. a `Panel` wrapper) in the
   MVP, or are they leaves only?
6. **Multiple surfaces:** how do `Persistent`/`Region`/`Overlay` views coexist with the generative
   canvas? Who owns layout and z-order?
7. **Overriding built-ins:** allow apps to replace a built-in style/component by name (`Override`)
   or keep built-ins immutable?
8. **View data contracts:** how does a view declare "I self-load X" vs. "the model must give me Y"
   so the model knows what to gather?
9. **Renderer keys:** how is a `ContentType` tag attached to data (server-provided, client-inferred
   from model name, or model-asserted)?
10. **Theming:** how do registered resources participate in light/dark and app theming?
11. **Factory lifetime/DI:** are components transient per render, and how do they get scoped
    services safely off the chat/tool threads?
