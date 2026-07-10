# JetNews parity backlog (M2)

Gold standard: `~/work/compose-samples/JetNews` (Kotlin). Gold screenshots:
`sample/Shared/JetNews/gold/` (self-captured 2026-07-09, Pixel 9 Pro API 36 —
compact home/scrolled/drawer/interests×3/article×2; 700dp rail; 1260dp
rail+two-pane "Select a post"). Method per `docs/sample-workflow-checklist.md`.
Glance widget + deeplinks OUT of scope per plan.

App shape: 2 top-level routes (Home, Interests) behind a **ModalNavigationDrawer**
(`JetnewsApp.kt:62`, gesturesEnabled only when NOT expanded :74); expanded adds
**AppNavRail** (`:77-81`) and Home becomes **list-detail** (`navigation/
ListDetailScene.kt` — "Select a post" placeholder pane). Article opens
full-screen compact / detail-pane expanded. All data static in-memory
(`data/posts` fakes); post images are bundled drawables.

## 1. Screens

### Home (`home/HomeScreens.kt`, `home/PostCards.kt`, `home/PostCardTop.kt`)
- App bar: drawer icon (terminal-prompt brand icon), centered lowercase
  "jetnews" wordmark image, search icon. Compact only; expanded home shows a
  **Search posts** OutlinedTextField at the top of the list pane instead.
- Sections: "Top stories for you" (PostCardTop: full-width image 16:9ish,
  headline, author, date+read-time) · simple rows (PostCardSimple: 40dp
  thumbnail, title, author-readtime, bookmark toggle icon) · "Popular on
  Jetnews" **horizontal card carousel** (PostCardPopular: elevated cards w/
  image top) · history rows with "BASED ON YOUR HISTORY" overline + overflow
  menu. Dividers between rows.
- Interactions: row tap → article; bookmark toggle (state in viewmodel);
  pull-to-refresh (fake refresh); snackbar on refresh error (JetnewsSnackbarHost).

### Article (`post/PostScreen.kt`, `post/PostContent.kt` — THE typography lift)
- Top bar: back arrow + "Published in: <publication>" w/ 36dp logo image.
- Content LazyColumn (`PostContent.kt:99-110`): header image, title h4,
  subtitle, author+avatar row, then **rich paragraphs** (`:159-197`):
  ParagraphType Title/Subhead/Text/Header/CodeBlock/Quote/Bullet — styled via
  `getTextAndParagraphStyle()`; **AnnotatedString runs** (bold/italic/code/link
  markups from `model/Paragraph`); CodeBlock = surface-tinted background block;
  Quote = styled italic; Bullet = custom ParagraphStyle w/ bullet glyph.
  → Comet: FormattedText runs exist; needs per-paragraph style mapping,
  code-block background, bullet rendering. Line-height/serif type (Domine
  headers + Montserrat body — `theme/Type.kt`, verify).
- Bottom action bar (compact): thumbs-up, bookmark, share, text-settings icons.
- Expanded: article renders in the detail pane, top bar hidden (list-detail).

### Interests (`interests/InterestsScreen.kt`)
- Tabs: Topics / People / Publications (M3 TabRow, primary indicator).
- Topics = sectioned list (section header + rows: 56dp image placeholder,
  name, **SelectTopicButton** = round add/check toggle w/ border). People /
  Publications = flat lists, same row shape.
- Selection state per row (checked ⇒ filled check icon), persists in VM.

### Drawer (`ui/AppDrawer.kt`)
- Brand row (icon + wordmark), NavigationDrawerItems Home/Interests with
  selected pill — same shape as Reply's modal drawer (reuse NavigationSuite
  drawer variant OR standalone Drawer; JetNews has NO bottom bar — chrome =
  drawer (compact) / rail (expanded), so NavigationSuite needs a
  **drawer-primary compact variant** or this app drives Drawer directly).

## 2. Adaptive (`JetnewsApp.kt:55-81`, `navigation/ListDetailScene.kt`)
- `isExpandedScreen = widthSizeClass == Expanded` (≥840dp): rail + list-detail
  home ("Select a post" empty pane), drawer gestures disabled.
- Compact/medium: modal drawer, single pane. (No bottom bar anywhere.)
- Comet: ListDetail primitive ✅ (Reply), rail ✅ (suite) — but chrome
  composition differs from Reply (drawer-first, no bar): likely drive
  Drawer + rail directly rather than NavigationSuite. DECIDE at build time.

## 3. Capability classification (initial — verify each at build)
| Need | Class |
|---|---|
| ModalNavigationDrawer + items | WORKING (Reply suite drawer / Drawer control) |
| NavRail | WORKING (suite rail) — needs standalone-rail composition w/ labels |
| ListDetail two-pane + placeholder pane | WORKING (Reply) + "Select a post" empty state |
| LazyColumn lists + dividers | WORKING |
| Horizontal card carousel (LazyRow) | **verify** — ListView is vertical; needs LazyRow node or HStack-in-Scroll |
| Elevated Card w/ image | WORKING (Reply cards) + Image top slot |
| TabRow (3 tabs, indicator) | **bound-unwired?** — facade TabRow? verify; Jetchat had none |
| Rich text: styled paragraph runs | FormattedText runs (WORKING base) + paragraph-type styles |
| CodeBlock background / Quote / Bullet | sample-level composition over FormattedText |
| Bookmark/select toggle icon buttons | WORKING (icon + tap) |
| Pull-to-refresh | **verify** — facade PullRefresh? else defer w/ note |
| Snackbar | **bound-unwired?** — facade SnackbarHost? verify |
| OutlinedTextField (expanded search) | WORKING (TextField borderless variant? verify outlined) |
| Serif display font (Domine) + Montserrat | bundle fonts, register (Jetchat pattern) |
| Post images (drawables) | copy to both probes (Reply avatar pattern) |

## 4. Known carryovers relevant here
- Android long-text bubble overflow (jetchat backlog) — the ARTICLE body is
  paragraph-heavy; fix the Compose text measure/render width mismatch EARLY if
  it reproduces on article paragraphs.
- Registry/agent: existence-based asserts — smoke asserts should tap through
  real visible flows (system back on Android for article close if no
  NavigationView).
- iOS gate: hand-composed chrome checklist (same-scale side-by-side pixel
  measurement) applies to the drawer/rail/tabs this sample hand-composes.

## 5. Next steps (in checklist order)
1. Deep source survey → fill file:line cites + values (this doc is the skeleton).
2. Framework gaps first, host-test-first: TabRow? LazyRow? Snackbar?
   pull-to-refresh? (verify facade bindings before building controls).
3. Screens: Home → Article (typography) → Interests → drawer/rail chrome.
4. jetnews.android.sh as screens land; then iOS gate; then RESULTS row.

## Status 2026-07-09 — compact-width core LANDED on Android (near-gold)

Commits `bd797be8`…`31e64d6a`: data/theme/icons port, Home, Article,
Interests, drawer-first chrome, jetnews.android.sh (16/16).

Framework capabilities added en route:
- `ListView.Horizontal` → real Compose `LazyRow` (popular carousel).
- `Text.LineBreak(Heading|Paragraph)` → real Compose `LineBreak` presets via
  the Text/AnnotatedText `style` slot + matching StaticLayout measurement
  (balanced+phrase / high-quality). iOS: no SwiftUI wrap-strategy API —
  documented greedy deviation.
- `TabBar` → real M3 `PrimaryTabRow`+`Tab` (Interests switcher).
- `DrawerOpened` event — gesture-open now reflects into the open signal
  (desync previously swallowed the next programmatic close).
- Surface-container tones derived from CorePalette.Neutral (mapper predates
  the M3 container roles).

Known deviations / follow-ups (compact):
- Wordmark + jetnews-logo + publication-badge VECTORS are stand-ins (styled
  Text / menu glyph / android glyph on #073042). Bundle the gold drawables
  (tinted-Icon resId path exists — the Jetchat footer approach).
- App bar does NOT collapse on scroll (gold: enterAlwaysScrollBehavior).
- Drawer sheet pills are hand-composed to NavigationDrawerItem metrics
  (Drawer takes a free-form sheet); selected highlight is Peek-baked, not
  reactive.
- Interests tab lists are ListViews (LazyColumn) vs gold Column+verticalScroll;
  bullet dot is line-height-centered vs gold's baseline alignBy; history-row
  3-line phrase break differs on one title.
- Article favorites/bookmark bottom-bar actions are inert (gold shows an
  unimplemented-dialog for most; bookmark toggle should wire to favorites).
- Scroll position resets on Article re-open (gold restores per-post state).

Remaining for M2: expanded chrome (AppNavRail + list-detail + search field,
gold medium-700dp/expanded-1260dp), per-sample app id + icons
(`-p:CometSample=jetnews`), iOS gate (SwiftUI twins: horizontal list =
ScrollView(.horizontal)+LazyHStack design noted; TabBar twin; article),
RESULTS.md row, /code-review.

## Status 2026-07-09 (later) — expanded chrome + standalone app + gold vectors LANDED

Commits `ca09ce4b`…`bf775607`:
- Expanded chrome (gold expanded-1260dp): root rebuilt on NavigationSuite
  (JetNews policy: None <840dp + modal drawer w/ REAL NavigationDrawerItems;
  REAL NavigationRail ≥840 with selected-only labels) + ListDetail at ~1/3
  split (compact pushes article full-screen w/ system back; expanded shows
  "Select a post" placeholder). Home list pane variant-aware via the suite's
  new variant signal (app bar ↔ "Search posts" field). Smoke now 20/20 incl.
  the 1260dp resize walk.
- Framework: NavigationSuiteVariant.None + variantFor policy + variantSignal;
  railShowsSelectedLabel (alwaysShowLabel slot exposed on the bridge);
  ListDetail.ListFraction; suite per-flush metrics recheck (resize could
  strand the variant); Icon.IconFillFrame (non-square vectors).
- Standalone com.comet.sample.jetnews (-p:CometSample=jetnews, gold launcher).
- Gold vectors landed: logo/wordmark/badge everywhere (stand-ins gone).
- Drawer-item selected highlight now reactive (real NavigationDrawerItems).

Still open for M2: app-bar collapse-on-scroll; expanded article chrome
(floating toolbar, no top bar — currently shows compact chrome in the pane);
rail items top-aligned vs gold centered; article bookmark action inert;
scroll-position restore; iOS gate (TabBar twin + horizontal list +
None-variant suite twin + article/interests screens); RESULTS.md row;
/code-review.

## /code-review (2026-07-09, 8-angle fan-out on bd797be8~1..HEAD) — outcome

FIXED (commits `cd69df98`, `ee35663b`): stale-article-on-destination-change;
FeedLists / article-shell subscription leaks; SwiftUIListDetailNode ignoring
ListFraction (iOS rendered 50/50); hosted-twin OnOwnerViewChanged never
dispatching (virtual on the base now); suite per-flush env-chain walk; JNI
TextStyle/LineBreakConfig rebuilt per render/measure (cached); compact app-bar
wordmark still the Text stand-in (replacement had silently missed); wrong
"inert in the gold too" search comment; no-op TrimIndent; dead topInset param.

REPORTED, BACKLOGGED (not fixed):
- REAL-widget promotions the facade already bridges: expanded search →
  OutlinedTextField (gold accepts input; submit is toasted), popular card →
  M3 Card, bookmark → IconToggleButton. AGENTS.md-grade fidelity items.
- LineBreakValues reflection hardening (R8/mangling → Release crash risk;
  current Release publish survives). Consider deriving packed values from
  public Strategy/Strictness/WordBreak constants.
- MulticolorAssets hardcoded set → promote to a control-level .Multicolor()
  property (both backends).
- TabBar styling via ctor tokens → migrate to the env styling system.
- iOS suite drawer only wraps the None variant (documented); DrawerOpen set
  during Rail is swallowed/stale (also: reset the signal on variant switch).
- Text_LineBreak has no reset-to-Default path on a reused node (hot reload).
- Android horizontal ListView branch skips header/footer/scroll-state wiring.
- ListFraction unvalidated (0/1/out-of-range).
- TabBar backend hook + ContentSwitcher/NavigationSuite share the
  subscribe-without-unsubscribe idiom on long-lived signals (framework-wide).
- Dedup nits: Tx() ×4, ToArgbUint/C() color conversions ×3, 48dp icon-button
  composition ×6, JetNewsRoot derived signals (ContentSwitcher bool overload).
- Style base swap: LineBreak text uses TextStyle.Default (not LocalTextStyle)
  as its base — benign while all slots are explicit; semantic trap noted.
