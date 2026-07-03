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

Extensions are registered on a mutable, observable **UI registry**. Registration can happen at
**app startup** (the common case) *and* **at any point during a session** — the catalog is a live
thing, not a fixed startup manifest (see [§2.1](#21-static-vs-dynamic-registration)). Startup
registration goes through `AddGenerativeUi`:

```csharp
builder.Services.AddGenerativeUi(options =>
{
    options.BaseAddress = new Uri("http://localhost:5225");
    options.JsonSerializerContext = GardenJsonContext.Default;

    options.Ui.AddStyle(/* … */);
    options.Ui.AddComponent(/* … */);          // or AddComponent<TView>(…)
    options.Ui.AddView(/* … */);               // or AddView<TView>(…)
    options.Ui.AddRenderer(/* … */);
});
```

The same `IUiRegistry` is resolvable from DI (`IUiRegistry`) so code can add/remove later:

```csharp
public sealed class SessionUi(IUiRegistry ui)
{
    public IDisposable OnAdminSignIn() =>            // register a bundle, dispose on sign-out
        ui.Bundle(reg =>
        {
            reg.AddView<AdminOrdersView>(/* … */);
            reg.AddComponent<BulkPriceEditor>(/* … */);
            reg.AddStyle(new UiStyle { Name = "admin-danger", /* … */ });
        });
}
```

Descriptors carry **full descriptions and usage rules** that are surfaced to the model **verbatim
and never clipped** — the same principle applied to API descriptions (see the
[OpenAPI appendix §3](./appendix-openapi-processor.md#3-reduction-openapireducer)). These texts are
how the model knows *when* and *why* to use each extension.

### 2.1 Static vs dynamic registration

The registry is **mutable and observable**, and everything registered can be **added or removed at
any time** — startup, after login, on a permission change, or mid-conversation:

- **Symmetric add/remove.** Every `Add…` has a matching `Remove…(name)` **and returns an
  `IDisposable`** whose `Dispose()` unregisters it. `Bundle(…)` groups several registrations into
  one handle so a whole feature set (e.g. "admin tools") can be added on sign-in and removed on
  sign-out atomically.
- **Permission-driven catalogs.** After authentication the app registers the components/views/styles
  the user is now entitled to (e.g. `AdminOrdersView`, a `BulkPriceEditor`, a destructive
  `admin-danger` style); on sign-out it disposes the bundle and they vanish from the model's
  vocabulary. The model can only ever use what is currently registered.
- **Observable + versioned.** The registry raises a `CatalogChanged` event and exposes a monotonic
  `Version`/etag. On change the host (a) **regenerates the per-app JSON Schema** used to validate
  `render_ui` output (§5), and (b) **re-informs the model** — either a short system note
  ("UI capabilities changed") or by having it re-call `list_ui_capabilities()` — so the seeded
  snapshot never goes stale.
- **Thread-safe.** Add/remove may be called from login handlers, tool threads, or the UI thread;
  mutations are synchronized and cheap.
- **Applies to all four tiers** — styles, components, views, and renderers are all dynamic. A
  mandatory renderer can be introduced or lifted at runtime (e.g. watermarking turns on only for
  unlicensed sessions).

### 2.2 Context-conditional resolution

Separate from *membership* (what is registered) is *resolution* (how a registered name renders).
These are different kinds of "dynamic":

| | Changes | Model-visible? | Driven by |
|---|---|---|---|
| **Membership** | which styles/components/views exist | **Yes** — catalog + schema change | login, permissions, feature flags |
| **Resolution** | how a *registered* name looks/behaves | **No** — same token, different result | theme, size, orientation, a11y, mode |

**The rule: context variation must never expand the model's vocabulary.** The model emits
`style:"danger"` (or a `ProductImage` component) once; the **host** resolves the right presentation
for light/dark, phone/desktop, large-text/high-contrast, or logged-in/out. The model does not reason
about theme or screen size.

**Prefer native MAUI** to express this — most of it is "just XAML" and updates automatically:

- `AppThemeBinding` for **light/dark**.
- `VisualStateManager` for control **states**.
- `OnIdiom` / `OnPlatform` and adaptive/size triggers for **device size & orientation**.
- Swappable **merged `ResourceDictionary`s** for **modes** (e.g. a "signed-in" theme) and
  **accessibility** (a high-contrast / large-text dictionary).

A style token therefore points at a XAML `Style` that *itself* varies by context; the token stays
constant. For cases native XAML can't express, a registration may supply a **resolver delegate**
that receives a `UiRenderContext` and returns the resource key / factory / view:

```csharp
public sealed record UiRenderContext(
    AppTheme Theme,                 // Light / Dark / Unspecified
    UiSizeClass Size,               // Compact / Medium / Expanded
    DisplayOrientation Orientation,
    UiAccessibility A11y,           // LargeText, HighContrast, ReduceMotion, BoldText…
    string? Mode,                   // app-defined, e.g. "signed-in" / "guest"
    IReadOnlyCollection<string> Permissions);

options.Ui.AddStyle(new UiStyle
{
    Name = "hero",
    AppliesTo = [UiNodeKind.Button],
    // static key…
    ResourceKey = "HeroButtonStyle",
    // …or context-resolved key (used when native XAML can't express the rule):
    ResolveResourceKey = ctx => ctx.A11y.HasFlag(UiAccessibility.HighContrast)
        ? "HeroButtonStyle.HighContrast"
        : "HeroButtonStyle",
});
```

- `UiComponentContext`/`UiViewContext` also expose `RenderContext`, so factories/resolvers can adapt.
- **Re-resolution on change.** When the render context changes, native mechanisms update in place;
  resolver-driven nodes are re-resolved by **re-inflating the current DSL document against the
  persistent binding tree** ([Binding Model §8](./appendix-binding-model.md#8-change-re-inflation--persistence)),
  so in-progress form state survives.

### 2.3 Registration sources

Registrations can come from three places (all feed the same registry):

1. **Imperative API (MVP).** Object descriptors or generic helpers; DI creates the view when it's
   registered, else falls back to `Activator.CreateInstance`:

   ```csharp
   ui.AddStyle(new UiStyle { /* … */ });

   ui.AddComponent<ProductImageView>(new UiComponentInfo   // created via ActivatorUtilities/DI
   {
       Name = "ProductImage",
       Description = "…",
       Props = [ /* … */ ],
   });

   ui.AddView<CheckoutView>(new UiViewInfo { Name = "CheckoutView", /* … */ });
   ```

2. **XAML + reflection (near-term).** Author the control/styles in XAML and mark them for
   registration; a startup **reflection scan** discovers and registers them:

   - **Attached-property / attribute marker** on a `ContentView` — e.g.
     `genui:UiComponent.Name="ProductImage"` (plus a `Description`), or a `[UiComponent(Name,
     Description)]` attribute on the class. `ui.AddComponentsFrom(assembly)` reflects over marked
     types and registers each.
   - **Bulk style registration** from a `ResourceDictionary`: `ui.AddStylesFrom(dictionary,
     predicate?)` walks `Style`/`Color` entries by `x:Key` and registers each as a token (opt-in via
     a marker attached property that also carries the description).

3. **Source generator (future).** A generator parses code/XAML at build time and emits **typed
   registration calls / descriptors** — no runtime reflection, descriptions captured at compile
   time, AOT-clean. The reflection path above is the bridge until then.

All three produce identical registry entries, so discovery, validation, and the dynamic schema don't
care how something was registered.



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
  hooks, the current `RenderContext` ([§2.2](#22-context-conditional-resolution)), and DI services.
  The model never provides code. Instead of a `Factory`, `AddComponent<TView>(UiComponentInfo)`
  lets the library create `TView` from **DI** (`ActivatorUtilities`) — or a XAML/reflection scan
  can register it ([§2.3](#23-registration-sources)).
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
  = base + registered styles; the **valid view set** = registered views — all read from the
  registry's **current** state.
- The library can emit a **per-app `render_ui` JSON Schema** (enums populated from the registry)
  for structured output/validation — see [UI-DSL appendix §11](./appendix-ui-dsl.md#11-draft-json-schema-sketch).
  Because the catalog is dynamic ([§2.1](#21-static-vs-dynamic-registration)), the schema is
  **regenerated whenever `CatalogChanged` fires**, so validation always matches what's currently
  registered.
- **Inflation order:** built-in → registered component/view → unknown ⇒ graceful placeholder
  (never throw). **Mandatory renderers** apply as a post-pass. Component `props` and view `inputs`
  are validated against their declared schemas before the factory/resolver runs.

## 6. Binding & state

- **Component props:** literal, one-way `bind` (into `data`), or two-way `key` (into `form`) for
  editable controls — reusing the DSL binding model
  ([UI-DSL §5](./appendix-ui-dsl.md#5-binding-model)), which is backed by the generic observable
  tree in the [Binding Model appendix](./appendix-binding-model.md). Since the model authors no view
  models, component inputs arrive through that generic tree, not a bespoke context.
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
- The **extension catalog** is owned by the app and is **live**: it carries a monotonic
  `Version`/etag and raises `CatalogChanged` on every add/remove
  ([§2.1](#21-static-vs-dynamic-registration)). Registrations are additive *and* removable and are
  always surfaced through discovery, so the model sees the current catalog — even when it changes
  mid-session (e.g. after sign-in).

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

### 9.4 Dynamic registration on sign-in (runtime)

```csharp
// After the user authenticates as an admin, register a bundle of admin-only vocabulary.
_adminUi = ui.Bundle(reg =>
{
    reg.AddView<AdminOrdersView>(new UiViewInfo { Name = "AdminOrdersView", /* … */ });
    reg.AddComponent<BulkPriceEditor>(new UiComponentInfo { Name = "BulkPriceEditor", /* … */ });
});
// CatalogChanged fires → schema regenerates → model is told new capabilities exist.

// On sign-out, one dispose removes them all; the model can no longer use them.
_adminUi.Dispose();
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
10. **Theming/context resolution:** how much context variation (light/dark, size, orientation, a11y,
    mode) is left to native XAML (`AppThemeBinding`/VSM/merged dictionaries) vs. a `ResolveResourceKey`
    delegate? Where's the line ([§2.2](#22-context-conditional-resolution))?
11. **Factory lifetime/DI:** are components transient per render, and how do they get scoped
    services safely off the chat/tool threads?
12. **Catalog-change signalling:** on `CatalogChanged` mid-session, do we inject a system note, force
    a `list_ui_capabilities()` refresh, or diff-and-notify only what changed? What's cheapest and
    most reliable?
13. **Removal mid-render:** if a component/view is unregistered while a surface using it is on
    screen, do we leave the rendered instance, replace it with a placeholder, or re-inflate?
14. **Registration source of truth:** imperative vs. XAML-attribute vs. source-generated — do we
    support all three at once, and how do we reconcile duplicate/conflicting registrations?
15. **Render-context change cost:** re-inflate the whole current document on every theme/size change,
    or track only context-sensitive nodes and re-resolve those?
