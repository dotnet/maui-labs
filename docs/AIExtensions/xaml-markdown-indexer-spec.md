# XAML → Markdown UI Indexer — Specification

> **Status:** Experimental. This document is the authoritative, implementation‑independent
> specification for the compile‑time XAML → Markdown UI indexer. It describes **what** the
> system produces and **the exact rules** it follows, not how any particular implementation is
> written. Someone reading this document should be able to predict the Markdown produced for any
> XAML page, and — in principle — reimplement the system from scratch and reproduce identical
> output.

---

## Table of contents

1. [Overview](#1-overview)
2. [Design philosophy](#2-design-philosophy)
3. [Scope and non‑goals](#3-scope-and-non-goals)
4. [Processing pipeline](#4-processing-pipeline)
5. [Input selection and page identity](#5-input-selection-and-page-identity)
6. [Output document format](#6-output-document-format)
7. [Element classification](#7-element-classification)
8. [Text, content, and value resolution](#8-text-content-and-value-resolution)
9. [Accessibility semantics](#9-accessibility-semantics)
10. [Visibility and conditional elements](#10-visibility-and-conditional-elements)
11. [Control reference](#11-control-reference)
12. [Containers, nesting, and promotion](#12-containers-nesting-and-promotion)
13. [Collections and data templates](#13-collections-and-data-templates)
14. [Bindable layouts](#14-bindable-layouts)
15. [User controls and cross‑file resolution](#15-user-controls-and-cross-file-resolution)
16. [Shell, navigation, and routing](#16-shell-navigation-and-routing)
17. [Generated artifacts and runtime contract](#17-generated-artifacts-and-runtime-contract)
18. [Determinism and formatting guarantees](#18-determinism-and-formatting-guarantees)
19. [Error handling and resilience](#19-error-handling-and-resilience)
20. [Line‑format grammar](#20-line-format-grammar)
21. [Worked end‑to‑end example](#21-worked-end-to-end-example)
22. [Extensibility and future work](#22-extensibility-and-future-work)
23. [Appendix A — Element classification tables](#appendix-a--element-classification-tables)
24. [Appendix B — Annotation and condition grammar](#appendix-b--annotation-and-condition-grammar)
25. [Glossary](#glossary)

---

## 1. Overview

The XAML → Markdown UI indexer analyzes a .NET MAUI application's XAML at **build time** and
produces a structured, deterministic, AI‑friendly **Markdown** description of every page, view,
template, and navigation path. It does this **without running the app** and **without any runtime
visual‑tree inspection**.

The output makes the entire UI discoverable to AI agents and tooling, enabling:

- **AI‑driven navigation** — an agent can find which screen contains a feature and describe how to
  reach it.
- **AI‑assisted help and onboarding** — step‑by‑step walkthroughs grounded in the real UI.
- **RAG over the UI** — the Markdown corpus is stable and can be embedded/retrieved.
- **Automated documentation** of screens and flows.
- **Accessibility review** — the output reflects what a screen reader would announce.
- **Test scaffolding** — a machine‑readable map of controls, bindings, and actions.

The system runs entirely at compile time. For every XAML page it emits a Markdown string embedded
in generated code, plus a per‑assembly aggregate index that enumerates all pages. The app decides
how to consume the index (e.g. exposing search/lookup tools to an AI agent); **producing** the
index is the only responsibility of this system.

---

## 2. Design philosophy

**The AI consumes the UI non‑visually — the way a screen reader does.** It has no access to the
rendered pixels, only to the semantic content this document describes: what a screen reader would
announce, **not** visual layout. This principle drives every rule in this document:

- **Include** things a user can perceive or act on: text, labels, headings, input fields and their
  hints, buttons and the actions they trigger, images (by their source/description), list content,
  navigation targets, and the conditions under which elements appear.
- **Exclude** things that are purely visual or structural: grid rows/columns, spacing, colors,
  brushes, shadows, styles, and the layout containers themselves.
- **Prefer accessibility metadata.** When a developer provides an accessibility description, hint,
  or heading level, that information takes priority over raw control text.
- **Represent every reachable state.** All tabs, all templates, and all conditionally‑visible
  elements are represented, even though only some are visible at any given moment at runtime.
- **Be deterministic.** Identical input always yields byte‑identical output. The corpus must be
  stable enough to diff and to embed.

---

## 3. Scope and non‑goals

### In scope

- Parsing all XAML documents that define a page or view (identified by a class declaration).
- Walking each document's element tree and extracting semantic content.
- Following references from one XAML document to another (user controls, hosted pages).
- Emitting one Markdown representation per page plus a per‑assembly aggregate index.
- Extracting Shell navigation structure, routes, and the app's home/entry screen.

### Non‑goals

The system must **not**:

- Generate or modify any UI code or XAML.
- Depend on the app running, or on any runtime visual tree.
- Infer business logic, dynamic data, or values only known at runtime.
- Resolve styling, theming, colors, or exact geometry.
- Produce embeddings (that is a downstream, optional step).
- Perform searching or ranking (that is the consuming app's responsibility).

---

## 4. Processing pipeline

Processing occurs in ordered stages. Conceptually:

1. **Discover inputs.** Select every XAML document available to the build (see
   [§5](#5-input-selection-and-page-identity)).
2. **Parse each document independently** into a semantic element tree, discarding non‑semantic
   content and normalizing controls, bindings, accessibility metadata, and conditions.
   Documents that cannot be parsed, or that do not declare a class, are dropped silently
   (see [§19](#19-error-handling-and-resilience)).
3. **Resolve cross‑file references.** Replace references to user controls/views with the inlined
   semantic content of the referenced document (see [§15](#15-user-controls-and-cross-file-resolution)).
4. **Resolve Shell navigation.** Map each Shell route onto the page it hosts and identify the
   app's home/entry screen (see [§16](#16-shell-navigation-and-routing)).
5. **Emit artifacts.** Render each page's element tree to Markdown and embed it in a per‑page
   generated type; emit one per‑assembly aggregate index enumerating all pages
   (see [§17](#17-generated-artifacts-and-runtime-contract)).

Stages 3 and 4 run **after** every document is parsed, because they require knowledge of all
pages at once. Stage 2 is per‑document and order‑independent.

---

## 5. Input selection and page identity

### 5.1 Which files are indexed

Every file with a `.xaml` extension that participates in the build is a candidate. No other file
types are considered.

### 5.2 Page identity requirement

A XAML document is indexed **only if** its root element declares a class name (the standard XAML
`x:Class` attribute). Documents without a class — for example resource dictionaries, color
palettes, or shared style files — are **not** indexed and produce no output.

From the class name the system derives:

- **Page name** — the simple (unqualified) class name, e.g. `ProductDetailPage`. This is the page's
  identity throughout the output.
- **Namespace** — everything before the final `.` in the class name; empty if the class has no
  namespace.
- **Root type** — the local name of the root element (e.g. `ContentPage`, `ContentView`, `Shell`).
  This selects Shell‑specific handling (see [§16](#16-shell-navigation-and-routing)).

### 5.3 File path normalization

Each page records a **relative file path** derived from its source location:

- Directory separators are normalized to forward slashes (`/`).
- The path is trimmed to start at the first occurrence of a recognized project folder marker:
  `Pages/`, `Views/`, or `Resources/` (case‑insensitive). For example
  `…/MyApp/Pages/ProductDetailPage.xaml` becomes `Pages/ProductDetailPage.xaml`.
- If no marker is present, the file name alone is used (e.g. `AppShell.xaml`).

This produces a stable, machine‑independent path that does not leak absolute build directories.

---

## 6. Output document format

Each page renders to a single Markdown document with this structure:

```
# {PageName}

Route: {route}            ← present only if the page has a Shell route
File: {relative/path.xaml} ← present only if a file path is known

{body — one line per semantic element, nested by indentation}
```

Rules:

- **Title.** The first line is a level‑1 Markdown heading containing the page name: `# {PageName}`.
- **Header block.** A single blank line follows the title. Then, if the page has a Shell route, a
  `Route: {route}` line; then, if a file path is known, a `File: {path}` line. A single blank line
  separates the header block from the body.
- **Body.** Each semantic element is one Markdown list item beginning with `- `. Nesting is
  expressed by **two spaces of indentation per level**.
- **Whitespace normalization.** The document has no leading or trailing blank lines. There is
  exactly one blank line between the header block and the first body line. Individual body lines
  are never blank.
- **Ordering.** Body elements appear in **document order** (the order they occur in the XAML),
  after the transformations described in later sections (skipping, flattening, promotion,
  inlining). Ordering is stable and deterministic.
- **Encoding.** Text is preserved verbatim, including Unicode, emoji, right‑to‑left scripts, and
  embedded quotation marks. Text is never translated, transliterated, or paraphrased. XML entity
  references in the source (e.g. `&quot;`, `&amp;`, `&lt;`) are decoded to their characters.

A page with a header but no semantic body content renders as just the title and header lines (no
body).

---

## 7. Element classification

Every element encountered while walking a document falls into exactly one of four classes. The
class determines whether the element appears in the output and how its children are treated.

| Class | Appears in output? | Children | Meaning |
|-------|-------------------|----------|---------|
| **Semantic** | Yes — one line | Handled per control | A control a user perceives or acts on (Label, Button, Entry, CollectionView, …). |
| **Structural** | No (unless promoted) | Walked and flattened to the parent's level | A layout/visual container (Grid, StackLayout, Border, ScrollView, …). |
| **Ignored** | No | Dropped entirely | Non‑visual/resource/styling content (ResourceDictionary, Style, Setter, brushes, row/column definitions, …). |
| **User‑control reference** | Yes — a labeled group | Inlined from the referenced document | A reference to another XAML view/control by namespace‑prefixed type. |

The complete membership of the Semantic, Structural, and Ignored sets is listed in
[Appendix A](#appendix-a--element-classification-tables).

**Unknown elements** (types in none of the three sets):

- If the element uses a **custom namespace prefix** (a prefix that does not resolve to the MAUI or
  Microsoft schema namespaces) and is not a known structural type, it is treated as a
  **user‑control reference** and becomes a candidate for cross‑file resolution
  (see [§15](#15-user-controls-and-cross-file-resolution)).
- Otherwise it is treated like a **structural** container: it is skipped, its children are walked,
  and the promotion rules of [§12](#12-containers-nesting-and-promotion) apply.

**Property elements** (elements whose local name contains a dot, e.g. `Grid.RowDefinitions`,
`CollectionView.ItemTemplate`, `ContentPage.Content`) are never rendered as controls. They are
either recognized as a meaningful slot (content, templates, empty view — handled by the owning
control) or ignored. In particular, these property‑element groups are ignored wholesale:
resources, resource dictionaries, row/column definitions, triggers, behaviors, gesture
recognizers, effects, menu‑bar items, toolbar items, styles, visual‑state groups, and the various
template slots (which are consumed by their owning collection, not walked as page content).
Generic content‑bearing property elements (such as a page's content, a scroll view's content, or a
border's content) are transparent: their children are walked as if written inline.

---

## 8. Text, content, and value resolution

Most controls display either a **literal string** or a **data binding**. The system resolves both.

### 8.1 Literal vs binding

A value is a **binding** if, after trimming, it is a markup extension of the form `{Binding …}` or
`{TemplateBinding …}`. Otherwise it is treated as a literal string.

### 8.2 Binding path display

Bindings are rendered as their **path** wrapped in braces: `{Path}`. Only the path is shown; other
binding facets are parsed but **not** rendered:

- `{Binding UserName}` → `{UserName}`
- `{Binding Path=UserName}` → `{UserName}` (an explicit `Path=` is honored)
- `{Binding}` (no arguments) → `{.}` (self/current binding context)
- `{Binding Price, StringFormat='{0:C}'}` → `{Price}` (string format is dropped from display)
- `{Binding IsReady, Converter={StaticResource Inv}}` → `{IsReady}` (converter dropped from
  display, but see [§10](#10-visibility-and-conditional-elements) — converters influence conditions)
- `{TemplateBinding Value}` → `{Value}`

When multiple comma‑separated binding parameters are present, the **first unnamed parameter** is
the path; named parameters `Path=`, `Mode=`, `Converter=`, `StringFormat=`, and `Source=` are
recognized. Nested braces inside a binding (e.g. a `Converter={StaticResource …}`) are handled
without breaking on their internal commas.

### 8.3 Escaped and non‑binding markup

- The XAML escape prefix `{}` (used to start a literal string with a brace) means the value is
  **not** a markup extension. Such values are shown literally, e.g. `{}{not a binding}` renders as
  the literal text `{}{not a binding}`.
- A token that merely starts with `{` but is not a valid `{Binding …}`/`{TemplateBinding …}`
  expression is treated as a literal string. For example `{BindingSource}` (no space after
  `Binding`) is **not** a binding and renders literally as `{BindingSource}`.
- Other markup extensions used where text is expected (e.g. `{StaticResource …}`) are shown as
  their raw text.

### 8.4 Commands

A control's command is rendered as an arrow annotation `→ {name}` appended after the display text:

- `Command="{Binding AddToCartCommand}"` → `→ AddToCartCommand` (the binding path)
- `Command="SomeLiteral"` → `→ SomeLiteral` (raw value when not a binding)

A command **parameter**, if present, is parsed but **not** rendered.

---

## 9. Accessibility semantics

The system reads the standard accessibility attached properties and prioritizes them. Three are
recognized (in XAML they appear as `SemanticProperties.Description`, `SemanticProperties.Hint`, and
`SemanticProperties.HeadingLevel`, whether written directly or via a namespace‑qualified form):

### 9.1 Description — overrides display text

A **non‑empty** description replaces whatever text/binding the control would otherwise display.
The description is shown as the control's quoted display value.

- `<Button Text="◄" SemanticProperties.Description="Back" />` → `- Button: "Back"`

The description override applies to every control type, including those with special value formats
(e.g. it overrides a slider's range display).

### 9.2 Decorative — empty Description skips the element

An **empty** description (`SemanticProperties.Description=""`) marks the element as **decorative**.
Decorative elements are omitted entirely from the output. If a decorative marker is placed on a
container, that container and its entire subtree are omitted.

- `<Label Text="🌿" SemanticProperties.Description="" />` → *(nothing)*

### 9.3 Hint — supplemental annotation

A hint is rendered as a bracket annotation `[hint: {text}]` (the hint text is shown unquoted).
Hints combine with other bracket annotations (see [Appendix B](#appendix-b--annotation-and-condition-grammar)).

- `<Button Text="Add to Cart" SemanticProperties.Hint="Adds item to cart" />`
  → `- Button: "Add to Cart" [hint: Adds item to cart]`

### 9.4 Heading level — promotes to a heading

A heading level changes how the element is labeled: instead of its control type, it is rendered as
`Heading (level {N}): "{text}"`.

- Accepted values are `1`–`9`, written either as a bare number (`"2"`) or as `Level{N}`
  (`"Level2"`). Values outside 1–9, the value `None`, and any unrecognized value mean **no heading**
  — the element renders normally as its control type.
- `<Label Text="Reviews" SemanticProperties.HeadingLevel="Level2" />`
  → `- Heading (level 2): "Reviews"`

---

## 10. Visibility and conditional elements

The system represents **whether** an element is conditionally shown, without evaluating the
condition. Conditions are surfaced as bracket annotations.

### 10.1 Sources of conditions

1. **Bound `IsVisible`.** When `IsVisible` is bound to a property, the element carries a visibility
   condition on that property.
   - `IsVisible="{Binding IsAdmin}"` → `[visible when IsAdmin = true]`
2. **Inverted binding.** If the `IsVisible` binding uses a converter whose name implies negation
   (contains `Inverse`, `Not`, or `Negate`, case‑insensitive), the condition is inverted.
   - `IsVisible="{Binding IsReady, Converter={StaticResource InverseBoolConverter}}"`
     → `[visible when IsReady = false]`
3. **Static `IsVisible="False"`.** A literal false means the element is **always hidden**. Such
   elements (and their subtrees, for containers) are **omitted entirely** — they are unreachable by
   a screen reader.
4. **`DataTrigger` on `IsVisible`.** A data trigger that sets `IsVisible` becomes a condition on the
   trigger's bound property and value:
   - Trigger sets `IsVisible="False"` → `[hidden when {Property} = {Value}]`
   - Trigger sets `IsVisible="True"` → `[visible when {Property} = {Value}]`

Only the **first** applicable condition on an element is used (a bound `IsVisible` takes precedence
over triggers).

### 10.2 Condition strings

| Situation | Rendered condition |
|-----------|--------------------|
| Bound `IsVisible` (normal) | `visible when {Property} = true` |
| Bound `IsVisible` with a negating converter | `visible when {Property} = false` |
| `DataTrigger` setting `IsVisible=False` when `{Property} = {Value}` | `hidden when {Property} = {Value}` |
| `DataTrigger` setting `IsVisible=True` when `{Property} = {Value}` | `visible when {Property} = {Value}` |

The `{Value}` for a data trigger is reproduced exactly as written in the XAML (e.g. `True`).

### 10.3 Conditions on containers

When a **structural container** carries a visibility condition and has visible children, the
children are wrapped in a **condition group**:

```
- When [visible when IsLoaded = true]:
  - Label: "Name"
  - Button: "Save"
```

See [§12](#12-containers-nesting-and-promotion).

---

## 11. Control reference

This section is the definitive control‑by‑control catalog. Each control renders as a single list
item. The generic line shape is:

```
- {Label}: {display} → {Command} [{annotations}]
```

where `{Label}` is the control type (or `Heading (level N)` when a heading level is set),
`{display}` is the resolved text/value, `→ {Command}` appears only when the control has a command,
and `[{annotations}]` is the comma‑joined bracket group (placeholder, hint, condition — see
[Appendix B](#appendix-b--annotation-and-condition-grammar)). Any segment that does not apply is
omitted, and there is never a doubled space when the display value is empty.

Unless stated otherwise: a non‑empty `SemanticProperties.Description` overrides `{display}`; a
`Hint` and any visibility condition are appended as annotations; a heading level relabels the line.

### 11.1 Text and headings

| Control | Value shown | Example XAML | Output |
|---------|-------------|--------------|--------|
| **Label** | `Text` (literal or binding) | `<Label Text="Hello World" />` | `- Label: "Hello World"` |
| **Label (bound)** | binding path | `<Label Text="{Binding UserName}" />` | `- Label: "{UserName}"` |
| **Label (heading)** | text, relabeled | `<Label Text="Welcome" SemanticProperties.HeadingLevel="Level1" />` | `- Heading (level 1): "Welcome"` |

### 11.2 Buttons and actions

| Control | Value shown | Example | Output |
|---------|-------------|---------|--------|
| **Button** | `Text`/`Content`; `Command` as `→` | `<Button Text="Save" Command="{Binding SaveCommand}" />` | `- Button: "Save" → SaveCommand` |
| **Button (hint)** | text + hint | `<Button Text="Add to Cart" Command="{Binding AddCommand}" SemanticProperties.Hint="Adds item to cart" />` | `- Button: "Add to Cart" → AddCommand [hint: Adds item to cart]` |
| **ImageButton** | image `Source`; `Command` as `→` | `<ImageButton Source="heart.png" Command="{Binding Like}" />` | `- ImageButton: "heart.png" → Like` |

- A button with no text and no command renders as just `- Button:`.
- **ImageButton** is displayed by its image `Source`.

### 11.3 Text input

Input controls display their **bound value** (if any); their **placeholder** is always shown as a
`[placeholder: "…"]` annotation — placeholders are frequently the only visible label for an input,
so they are surfaced even when the field is also data‑bound. The placeholder is listed **first** in
the annotation group.

| Control | Example | Output |
|---------|---------|--------|
| **Entry** (bound + placeholder) | `<Entry Text="{Binding Email}" Placeholder="Email" />` | `- Entry: "{Email}" [placeholder: "Email"]` |
| **Entry** (placeholder only) | `<Entry Placeholder="Search products" />` | `- Entry: [placeholder: "Search products"]` |
| **Editor** | `<Editor Text="{Binding Comment}" Placeholder="Write your review" />` | `- Editor: "{Comment}" [placeholder: "Write your review"]` |
| **SearchBar** | `<SearchBar Placeholder="Search..." />` | `- SearchBar: [placeholder: "Search..."]` |

### 11.4 Selection and toggles

| Control | Value shown | Example | Output |
|---------|-------------|---------|--------|
| **Switch** | `IsToggled` binding (or literal) | `<Switch IsToggled="{Binding DarkMode}" />` | `- Switch: "{DarkMode}"` |
| **CheckBox** | `IsChecked`/`IsToggled` binding (or literal) | `<CheckBox IsChecked="{Binding Agreed}" />` | `- CheckBox: "{Agreed}"` |
| **RadioButton** | `Content`/`Text` | `<RadioButton Content="Option A" />` | `- RadioButton: "Option A"` |
| **Picker** | `Title` + `SelectedItem` binding as `→` | `<Picker Title="Select size" SelectedItem="{Binding Size}" />` | `- Picker: "Select size" → "{Size}"` |
| **DatePicker** | `Date` binding | `<DatePicker Date="{Binding Delivery}" />` | `- DatePicker: "{Delivery}"` |
| **TimePicker** | `Time` binding | `<TimePicker Time="{Binding SelectedTime}" />` | `- TimePicker: "{SelectedTime}"` |

- **Picker** with no title omits the title text. Because the remaining value begins with the
  arrow, the line renders with two spaces after the colon: `- Picker:  → "{Selected}"`.

### 11.5 Ranges and progress

| Control | Value shown | Defaults | Example | Output |
|---------|-------------|----------|---------|--------|
| **Slider** | `{min}–{max}`; `Value` binding as `→` | min `0`, max `1` | `<Slider Minimum="1" Maximum="5" Value="{Binding Rating}" />` | `- Slider: 1–5 → "{Rating}"` |
| **Slider** (defaults) | range only | `0–1` | `<Slider />` | `- Slider: 0–1` |
| **Stepper** | `{min}–{max}`; `Value` binding as `→` | min `0`, max `100` | `<Stepper Minimum="0" Maximum="10" Value="{Binding Qty}" />` | `- Stepper: 0–10 → "{Qty}"` |
| **ActivityIndicator** | `IsRunning` binding | — | `<ActivityIndicator IsRunning="{Binding IsBusy}" />` | `- ActivityIndicator: "{IsBusy}"` |
| **ProgressBar** | `Progress` binding | — | `<ProgressBar Progress="{Binding Download}" />` | `- ProgressBar: "{Download}"` |

- The range separator is an en‑dash (`–`, U+2013).
- A `SemanticProperties.Description` overrides the entire range/value display for sliders and
  steppers.

### 11.6 Media and other

| Control | Value shown | Example | Output |
|---------|-------------|---------|--------|
| **Image** | `Source` | `<Image Source="logo.png" />` | `- Image: "logo.png"` |
| **WebView** | type only (presence marker) | `<WebView … />` | `- WebView:` |

### 11.7 Collections

`CollectionView`, `ListView`, and `CarouselView` are semantic containers with their own rendering
(items source + template slots). See [§13](#13-collections-and-data-templates).

---

## 12. Containers, nesting, and promotion

Layout/visual containers are **structural**: by default they are not rendered, and their children
are walked and emitted at the **parent's** indentation level (flattening the visual hierarchy).

```xml
<Grid>
  <VerticalStackLayout>
    <Border>
      <ScrollView>
        <Label Text="Deep inside" />
      </ScrollView>
    </Border>
  </VerticalStackLayout>
</Grid>
```

renders as a single line:

```
- Label: "Deep inside"
```

Three rules modify this default:

### 12.1 Decorative containers are dropped

A container explicitly marked decorative (empty `SemanticProperties.Description`) is omitted
together with its entire subtree (see [§9.2](#92-decorative--empty-description-skips-the-element)).

### 12.2 Promoted containers

If a container carries a **non‑empty** `SemanticProperties.Description`, it is **promoted** to a
visible element: it renders as `- {ContainerType}: "{Description}"`, and its actionable children
are still walked and nested beneath it.

```xml
<Border SemanticProperties.Description="Product card">
  <Button Text="Buy" Command="{Binding BuyCommand}" />
</Border>
```

```
- Border: "Product card"
  - Button: "Buy" → BuyCommand
```

### 12.3 Conditional containers (condition groups)

If a structural container has a **visibility condition** (see [§10](#10-visibility-and-conditional-elements))
and has visible children, its children are wrapped in a **condition group** labeled with the
condition:

```xml
<StackLayout IsVisible="{Binding IsLoaded}">
  <Label Text="Name" />
  <Button Text="Save" />
</StackLayout>
```

```
- When [visible when IsLoaded = true]:
  - Label: "Name"
  - Button: "Save"
```

A statically hidden container (`IsVisible="False"`) is dropped with its subtree.

---

## 13. Collections and data templates

`CollectionView`, `ListView`, and `CarouselView` render as a labeled container followed by their
template slots. The container line is:

```
- {CollectionType}: "{ItemsSource}" [{annotations}]
```

- **Items source.** The `ItemsSource` binding path is shown quoted in braces, e.g. `"{Products}"`.
  If `ItemsSource` is a literal (not a binding), its raw value is shown.
- **Grouped.** If `IsGrouped="True"`, a `grouped` annotation is added: `[grouped]`. A grouped
  collection that also has a visibility condition combines them: `[grouped, visible when X = true]`.

### 13.1 Template slots

Each template is rendered as a nested, labeled sub‑list. The elements inside each template are
walked with the **same rules** as page content (so nested layouts are flattened, headings apply,
etc.). Slots and their labels:

| Slot | Label |
|------|-------|
| Item template | `- Each item:` |
| Header template | `- Header:` |
| Footer template | `- Footer:` |
| Group header template | `- Group header (each group):` |
| Group footer template | `- Group footer (each group):` |
| Empty view | `- Empty view:` |

Slots that are absent are omitted. The relative order of rendered slots is: header, group header,
each item, group footer, footer, empty view. (Only the slots present appear.)

A `DataTemplate` may be provided either through the explicit template property element or as a
direct child; both are recognized, and the template's root content is unwrapped (the
`DataTemplate` wrapper itself is not rendered).

### 13.2 Examples

Simple item template:

```xml
<CollectionView ItemsSource="{Binding Items}">
  <CollectionView.ItemTemplate>
    <DataTemplate><Label Text="{Binding Name}" /></DataTemplate>
  </CollectionView.ItemTemplate>
</CollectionView>
```

```
- CollectionView: "{Items}"
  - Each item:
    - Label: "{Name}"
```

Grouped with a group header (note the heading level inside the template):

```
- CollectionView: "{Groups}" [grouped]
  - Group header (each group):
    - Heading (level 2): "{CategoryName}"
  - Each item:
    - Label: "{Name}"
```

Header, items, and footer:

```
- CollectionView: "{Items}"
  - Header:
    - Label: "Start"
  - Each item:
    - Label: "{Name}"
  - Footer:
    - Label: "End"
```

With an empty view:

```
- CollectionView: "{Items}"
  - Each item:
    - Label: "{Name}"
  - Empty view:
    - Label: "No items found"
```

---

## 14. Bindable layouts

Any structural layout can act as a repeater via the **bindable‑layout** attached property (an
`ItemsSource` attached to a layout, with an attached item template). Such a layout is rendered as a
labeled repeater rather than being flattened:

```
- {LayoutType} with items from "{ItemsSource}"[ {condition}]:
  - Each item:
    - …template content…
```

Example:

```xml
<VerticalStackLayout BindableLayout.ItemsSource="{Binding Reviews}">
  <BindableLayout.ItemTemplate>
    <DataTemplate><Label Text="{Binding Comment}" /></DataTemplate>
  </BindableLayout.ItemTemplate>
</VerticalStackLayout>
```

```
- VerticalStackLayout with items from "{Reviews}":
  - Each item:
    - Label: "{Comment}"
```

A visibility condition on the layout is appended to the container line (before the colon):

```
- VerticalStackLayout with items from "{Items}" [visible when HasItems = true]:
  - Each item:
    - Label: "{Name}"
```

The bindable‑layout `ItemsSource` may be written as an attribute or as a child property element;
both are recognized.

---

## 15. User controls and cross‑file resolution

A page may embed a reusable view/control defined in another XAML document, referenced by a
namespace‑prefixed type (e.g. `<views:CartView />`). These references are **inlined**: the
referenced document is parsed and its semantic content is nested beneath a labeled group.

### 15.1 Rendering

A user‑control reference renders as:

```
- [{TypeName}]:[ {condition}]
  - …inlined content of the referenced view…
```

The square brackets around the type name distinguish an inlined user control from a built‑in
control. A visibility condition on the reference is shown as an annotation on the group line.

Example — a page referencing `MyWidget`:

```
- Label: "Header"
- [MyWidget]:
  - Button: "Click me" → ClickCommand
```

### 15.2 Nested and shared controls

- **Nested references resolve recursively** — a control that itself references another control
  inlines the whole chain:

  ```
  - [Outer]:
    - Label: "Outer"
    - [Inner]:
      - Label: "Inner"
  ```

- **Shared controls are inlined at every use site.** If two pages both reference the same view,
  each page's output contains the fully inlined content. Resolution is cached so a shared control
  is parsed once, and each inlined copy is independent (no cross‑page mutation).

### 15.3 Resolution matching and ambiguity

- A reference is matched to a document by **simple (unqualified) class name**.
- If **two or more** indexed documents share the same simple class name, that name is
  **ambiguous** and references to it are treated as unresolved (see below).
- A reference is matched regardless of the namespace prefix used at the reference site; matching is
  by type name.

### 15.4 Cycles

Self‑references and reference cycles (A → B → A) are detected and broken. A control never inlines
itself, and an in‑progress control is not re‑entered. Resolution always terminates.

### 15.5 Unresolved references

If a reference cannot be resolved (the target document is not indexed, or the name is ambiguous),
the reference is **kept as an empty placeholder** rather than dropped:

```
- Label: "Before"
- [MissingWidget]:
- Label: "After"
```

Any accessibility metadata or condition on the unresolved reference is preserved on the placeholder
line. This ensures third‑party controls and not‑yet‑indexed views still appear in the map. This
behavior directly satisfies the resilience requirement: unknown/unresolved types produce warnings‑
in‑spirit (a placeholder), not failures.

---

## 16. Shell, navigation, and routing

When a document's root type is `Shell`, it is parsed for **navigation structure** instead of page
content. The Shell parse recognizes `TabBar`, `Tab`, `FlyoutItem`, and `ShellContent`.

### 16.1 Navigation elements

- **`ShellContent`** renders as `- ShellContent: "{Title or Route}"[ → {HostedPage}] [route: {route}]`:
  - The display text is the `Title` if present, otherwise the `Route`, otherwise empty.
  - If the `ShellContent` names a hosted page via its content template (see §16.3), the hosted
    page's simple class name is shown after an arrow: `→ {HostedPage}`.
  - The `Route`, if present, is shown as a `[route: {route}]` annotation.
- **`TabBar`** and **`FlyoutItem`** are transparent grouping containers: their `ShellContent`/`Tab`
  children are surfaced directly (the `TabBar`/`FlyoutItem` itself is not rendered as a line).
- **`Tab`** renders as `- Tab: "{Title}" [route: {route}]` with its child `ShellContent`s nested
  beneath it.

Examples:

```
- ShellContent: "Home" [route: home]
- ShellContent: "Settings" [route: settings]
```

```
- Tab: "Browse" [route: browse]
  - ShellContent: "Catalog" [route: catalog]
  - ShellContent: "Search" [route: search]
```

### 16.2 Route mapping onto pages

After all documents are parsed, each `ShellContent`'s route is **copied onto the page it hosts**.
That page then reports that route in its own header (`Route: {route}`) and is discoverable by
route. A page that is only reachable through code‑based route registration (not declared in the
Shell) has no route in its header.

### 16.3 Resolving the hosted page

A `ShellContent`'s hosted page is resolved from its content template, in either form:

- Inline markup extension: `ContentTemplate="{DataTemplate pages:MainPage}"` → `MainPage`.
- Property‑element form: a nested content‑template → data‑template → the page element, whose local
  type name is used.

Any namespace prefix on the type is dropped; the **simple class name** is used, which matches the
hosted page's page name.

### 16.4 Home / entry screen

The **first** `ShellContent` (in document order, including those nested under a `Tab`) that hosts a
page identifies the app's **home/entry screen** — the screen shown when the app launches and where
every user starts. This fact is:

- recorded on the aggregate index as the entry page name (see [§17](#17-generated-artifacts-and-runtime-contract)), and
- marked in the Shell document's Markdown on that `ShellContent` line with a trailing marker:
  `(HOME — the screen the app opens to; users start here)`.

Identifying the home screen as an explicit, told fact (rather than something a consumer must infer)
is important: it lets an agent start a navigation walkthrough from the correct screen without
guessing.

---

## 17. Generated artifacts and runtime contract

The system emits code artifacts that expose the Markdown to the app. The **names and shapes below
are the public contract** that consumers depend on.

### 17.1 Per‑page artifact

For every indexed page, a generated type carries that page's Markdown:

- The type is named `{PageName}_UiIndex` (with any characters invalid in an identifier replaced by
  `_`), declared in the page's namespace, as a `public static partial class`.
- It exposes:
  - `public const string Markdown` — the page's full semantic Markdown (as described throughout
    this document), embedded verbatim.
  - `public const string PageName` — the page's simple class name.

### 17.2 Aggregate artifact (per assembly)

Exactly one aggregate index is emitted per assembly:

- The type is named `{AssemblyName}UiIndex` (identifier‑sanitized) and derives from the runtime base
  type `UiPageIndex`. It is placed in a namespace matching the assembly name when that is a valid
  namespace; otherwise it is emitted without a namespace.
- It exposes:
  - `public static {Type} Default { get; }` — a ready‑to‑use singleton.
  - `Pages` — the list of every indexed page, as `UiPageEntry` records, **ordered alphabetically by
    page name**. Each entry carries the page's name, route (or none), file path (or none), and its
    Markdown.
  - `EntryPageName` — the home/entry screen's page name, emitted **only** when a home screen was
    resolved from a Shell (see [§16.4](#164-home--entry-screen)).
- The page list is a plain static array — **no reflection, no module initializers** — so it is
  trimming‑ and AOT‑safe.

### 17.3 Runtime consumption types

Consumers interact with two runtime types (provided by the runtime library, not generated):

- **`UiPageEntry`** — an indexed page: `Name`, `Route` (nullable), `FilePath` (nullable),
  `Markdown`.
- **`UiPageIndex`** — the base of every aggregate:
  - `Pages` — all indexed pages.
  - `FindByName(name)` — look up a page by class name (case‑insensitive).
  - `FindByRoute(route)` — look up a page by Shell route (case‑insensitive).
  - `EntryPageName` — the home/entry page name (or none).
  - `Home` — the home/entry `UiPageEntry` (or none).

Multiple assemblies each get their own aggregate; there is no global registry by design. An app
that spans assemblies merges each assembly's `Pages` itself.

### 17.4 Embedding Markdown safely

Because the Markdown may contain arbitrary characters (including sequences of double quotes), the
embedded literal uses a delimiter guaranteed not to collide with the content. The embedded text is
byte‑for‑byte the Markdown defined by this specification; escaping is purely a code‑emission
concern and does not alter the Markdown.

---

## 18. Determinism and formatting guarantees

- **Deterministic.** The same set of input documents always produces byte‑identical output.
- **Stable ordering.** Within a page, elements follow document order after transformation. In the
  aggregate, pages are ordered alphabetically by page name.
- **Indentation.** Exactly two spaces per nesting level; list items always begin with `- `.
- **Header.** `# {PageName}`, one blank line, optional `Route:` line, optional `File:` line, one
  blank line, then the body. No leading/trailing blank lines; no blank lines within the body.
- **Verbatim text.** On‑screen text, labels, hints, and placeholders are reproduced exactly,
  including Unicode, emoji, RTL text, and quotes. Nothing is translated or paraphrased.
- **No doubled spaces (scoped).** When a control has **no display value** but does have
  annotations or a command, the trailing space after the type label is dropped so the line does not
  contain a doubled space (e.g. `- Entry: [placeholder: "…"]`, not `- Entry:  [placeholder: "…"]`).
  This rule is scoped to an *empty* display value. A non‑empty display value that itself begins with
  a space is reproduced as‑is — notably a title‑less `Picker` renders `- Picker:  → "{Selected}"`
  with two spaces, because its value string starts with the arrow.

---

## 19. Error handling and resilience

The system favors partial, useful output over failure. Specifically:

- **Unparseable XAML** (not well‑formed XML) produces **no output for that document** and does not
  stop indexing of other documents.
- **A document with no root element** produces no output.
- **A document with no class declaration** is skipped (not an error).
- **Empty or whitespace‑only content** produces no output.
- **Unknown control types** are handled gracefully — either walked as containers or preserved as
  user‑control placeholders — never dropped as errors.
- **Unresolved user‑control references** are kept as empty placeholders (see [§15.5](#155-unresolved-references)).
- **Reference cycles** are detected and broken; resolution always terminates.
- **Missing referenced files** simply leave a placeholder.

In all cases the goal is: index everything that can be indexed, and represent the rest as best as
possible, rather than aborting.

---

## 20. Line‑format grammar

An informal grammar of a rendered body line (whitespace‑significant; `INDENT` is two spaces per
nesting level):

```
line          = INDENT "- " item
item          = control | container-group | shell-item
control       = label [ " " display ] [ " → " command ] [ " " brackets ]
label         = type-name ":" | "Heading (level " digit ")" ":"
display       = "\"" text-or-binding "\""   ; may be empty (then the leading space is omitted)
              | range                        ; sliders/steppers, e.g. 0–1
range         = number "–" number [ " → " "\"{" path "}\"" ]
command       = name                          ; binding path or literal
brackets      = "[" annotation *( ", " annotation ) "]"
annotation    = "placeholder: \"" text "\""
              | "hint: " text
              | "visible when " prop " = " value
              | "hidden when " prop " = " value
              | "grouped"
container-group = "When " brackets ":"        ; conditional structural container
              | user-control                  ; "[TypeName]:"  (+ optional condition annotation)
              | collection                     ; "{Type}: \"{source}\"" (+ [grouped, …])
              | bindable-layout                ; "{Type} with items from \"{source}\"" (+ cond) ":"
shell-item    = "ShellContent: \"" text "\"" [ " → " page ] [ " [route: " route "]" ] [ home-marker ]
              | "Tab: \"" title "\"" [ " [route: " route "]" ]
home-marker   = "  (HOME — the screen the app opens to; users start here)"
```

Bracket annotations, when combined on one control, always appear in this order: **placeholder,
hint, condition** (see [Appendix B](#appendix-b--annotation-and-condition-grammar)).

---

## 21. Worked end‑to‑end example

Given these documents:

`Pages/ProductDetailPage.xaml`:

```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:views="clr-namespace:MyApp.Views"
             x:Class="MyApp.Pages.ProductDetailPage">
  <Grid>
    <Button Text="◄" SemanticProperties.Description="Back"
            SemanticProperties.Hint="Returns to catalog" />
    <Label Text="{Binding Name}" SemanticProperties.HeadingLevel="Level1" />
    <Label Text="{Binding PriceLabel}" />
    <Button Text="Add to Cart" Command="{Binding AddToCartCommand}"
            SemanticProperties.Hint="Adds this product to your cart" />
    <Button Text="Write Review" Command="{Binding WriteReviewCommand}" />
    <VerticalStackLayout BindableLayout.ItemsSource="{Binding Reviews}">
      <BindableLayout.ItemTemplate>
        <DataTemplate>
          <Label Text="{Binding Stars}" />
          <Label Text="{Binding Comment}" IsVisible="{Binding HasComment}" />
        </DataTemplate>
      </BindableLayout.ItemTemplate>
    </VerticalStackLayout>
  </Grid>
</ContentPage>
```

`AppShell.xaml`:

```xml
<Shell xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
       xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
       xmlns:pages="clr-namespace:MyApp.Pages"
       x:Class="MyApp.AppShell">
  <TabBar>
    <ShellContent Route="products" Title="Products"
                  ContentTemplate="{DataTemplate pages:ProductDetailPage}" />
  </TabBar>
</Shell>
```

### Output — `ProductDetailPage`

Because the Shell's first `ShellContent` hosts `ProductDetailPage`, its route `products` is mapped
onto the page (and it is the home screen).

```
# ProductDetailPage

Route: products
File: Pages/ProductDetailPage.xaml

- Button: "Back" [hint: Returns to catalog]
- Heading (level 1): "{Name}"
- Label: "{PriceLabel}"
- Button: "Add to Cart" → AddToCartCommand [hint: Adds this product to your cart]
- Button: "Write Review" → WriteReviewCommand
- VerticalStackLayout with items from "{Reviews}":
  - Each item:
    - Label: "{Stars}"
    - Label: "{Comment}" [visible when HasComment = true]
```

### Output — `AppShell`

```
# AppShell

File: AppShell.xaml

- ShellContent: "Products" → ProductDetailPage [route: products]  (HOME — the screen the app opens to; users start here)
```

### Aggregate

The assembly's aggregate index exposes both pages (ordered alphabetically: `AppShell`,
`ProductDetailPage`), each with its name, route, file path, and Markdown, and reports the entry
page name `ProductDetailPage`.

---

## 22. Extensibility and future work

The model is intentionally open to enrichment. Planned/possible additions that fit the existing
shape without breaking it:

- **ViewModel metadata** — associating a page with its view model and surfacing annotated
  properties/commands.
- **AI‑generated descriptions** — supplementing sparse UIs with generated summaries.
- **Localization extraction** — resolving localized string keys to representative text.
- **Deeper accessibility analysis** — flagging missing labels, unreachable actions, etc.
- **RAG embedding generation** — producing embeddings from the stable Markdown corpus.

Any such extension must preserve the guarantees in [§18](#18-determinism-and-formatting-guarantees):
determinism, stable ordering, verbatim text, and accessibility‑first content.

---

## Appendix A — Element classification tables

### A.1 Semantic elements (rendered)

`Label`, `Button`, `ImageButton`, `Entry`, `Editor`, `SearchBar`, `Slider`, `Stepper`, `Switch`,
`CheckBox`, `RadioButton`, `Picker`, `DatePicker`, `TimePicker`, `Image`, `ActivityIndicator`,
`ProgressBar`, `CollectionView`, `ListView`, `CarouselView`, `WebView`.

### A.2 Structural elements (skipped; children walked)

`Grid`, `StackLayout`, `VerticalStackLayout`, `HorizontalStackLayout`, `FlexLayout`,
`AbsoluteLayout`, `ScrollView`, `Border`, `Frame`, `BoxView`, `ContentView`, `ContentPresenter`,
`RefreshView`, `SwipeView`.

### A.3 Ignored elements (dropped entirely)

`ResourceDictionary`, `Style`, `Setter`, visual‑state constructs (`VisualStateManager.VisualStateGroups`,
`VisualStateGroupList`, `VisualStateGroup`, `VisualState`), brushes (`Brush`, `SolidColorBrush`,
`LinearGradientBrush`, `GradientStop`), `Shadow`, layout definitions (`ColumnDefinition`,
`RowDefinition`, `ColumnDefinitionCollection`, `RowDefinitionCollection`), and icon slots
(`FlyoutItem.Icon`, `Tab.Icon`).

### A.4 Ignored property‑element groups

Property elements whose local name ends with any of: `.Resources`, `.ResourceDictionary`,
`.RowDefinitions`, `.ColumnDefinitions`, `.Triggers`, `.Behaviors`, `.GestureRecognizers`,
`.Effects`, `.MenuBarItems`, `.ToolbarItems`, `.Styles`, `.VisualStateManager.VisualStateGroups`,
and the collection template slots (`.ItemTemplate`, `.HeaderTemplate`, `.FooterTemplate`,
`.GroupHeaderTemplate`, `.GroupFooterTemplate`) — the template slots are consumed by their owning
collection rather than walked as page content.

Other content‑bearing property elements (e.g. a page's, scroll view's, or border's content) are
transparent: their children are walked inline.

---

## Appendix B — Annotation and condition grammar

### B.1 Line assembly

A control line is assembled as:

```
"- " + label + [ " " + display ] + [ " → " + command ] + [ " " + brackets ]
```

- When `display` is empty, no space is inserted after the `label:` colon (no doubled space).
- `brackets` is emitted only when at least one annotation is present.

### B.2 Bracket annotation order

Within `[ … ]`, annotations are comma‑separated in this fixed order:

1. `placeholder: "{text}"` — input controls only.
2. `hint: {text}` — from `SemanticProperties.Hint`.
3. condition — one of the visibility strings below.

For collections, the container annotation group is `[grouped]`, optionally combined with a
condition as `[grouped, {condition}]`.

### B.3 Condition strings

| Condition | Rendered |
|-----------|----------|
| Bound `IsVisible` | `visible when {Property} = true` |
| Bound `IsVisible` + negating converter | `visible when {Property} = false` |
| `DataTrigger` hides (sets `IsVisible=False`) | `hidden when {Property} = {Value}` |
| `DataTrigger` shows (sets `IsVisible=True`) | `visible when {Property} = {Value}` |

Static `IsVisible="False"` is not a condition — the element is omitted entirely.

---

## Glossary

- **Semantic element** — a control that carries meaning for a user; it is rendered as a line.
- **Structural element** — a layout/visual container; skipped, with children flattened to the
  parent level unless promoted or conditional.
- **Promotion** — rendering an otherwise‑skipped container because it has an accessibility
  description, keeping its children nested.
- **Condition group** — a `- When [ … ]:` wrapper around the children of a conditionally‑visible
  container.
- **User‑control reference** — a use of another XAML view/control by type; rendered as `[TypeName]:`
  with the referenced content inlined.
- **Aggregate index** — the per‑assembly type that enumerates every indexed page and exposes lookup
  by name/route and the home screen.
- **Home / entry screen** — the screen the app opens to, taken from the first Shell content that
  hosts a page; surfaced as an explicit fact so navigation walkthroughs can start there.
