// `$default` is a Kotlin compiler convention: every @Composable function
// takes a trailing `int $default` bitmask where bit N == 1 means
// "parameter N was NOT provided; substitute the default". Bit positions
// are positional in the Kotlin source order — there's no runtime API
// to query them.
//
// `ColumnDefault` and `MaterialThemeDefault` are *generated* by
// AndroidX.Compose.SourceGenerators from the generic `[ComposeDefaults<T>]`
// attribute below — the binder exposes those Kt classes, so the
// generator can read parameter names off the longest overload.
//
// The other enums are *also* generated, but from the declarative
// `[ComposeDefaults]` overload. Their Kotlin overloads with the trailing
// $default param are stripped from the binding (mangled JVM names like
// `Text--4IGK_g` from inline classes such as `Color`/`TextUnit`/`Dp`),
// so there is no IMethodSymbol for the generator to introspect — we
// hand it the Kotlin parameter names instead. Names prefixed with `!`
// consume a bit position but don't emit an enum member (e.g. params
// the caller always provides). For extension-receiver functions
// (NavigationBarItem takes a RowScope receiver), the receiver is NOT
// part of the $default bitmask — start the name list at the first
// user-facing parameter.
//
// When dotnet/java-interop#1440 lands and exposes the inline-class
// overloads, the declarative attributes can be replaced with
// `[ComposeDefaults<ButtonKt>("Button", "ButtonDefault")]` etc. and
// this comment can be deleted.

using AndroidX.Compose.Foundation.Lazy;
using AndroidX.Compose.Foundation.Lazy.Grid;
using AndroidX.Compose.Material3;
using AndroidX.Compose;

// Hand-rolled declarative form (instead of generic form) so the
// ComposeFacadeGenerator can see the bit names — generic-form enums
// are emitted by another source generator in the same pass and aren't
// visible cross-generator. Bits and names match what the generic form
// would have produced.
[assembly: ComposeDefaults("ColumnDefault", "modifier", "verticalArrangement", "horizontalAlignment", "!content")]
[assembly: ComposeDefaults("RowDefault", "modifier", "horizontalArrangement", "verticalAlignment", "!content")]
[assembly: ComposeDefaults("BoxDefault", "modifier", "contentAlignment", "propagateMinConstraints", "!content")]

// androidx.compose.foundation.layout.BoxWithConstraintsKt — same shape
// as Box, but the content lambda receives a BoxWithConstraintsScope.
[assembly: ComposeDefaults("BoxWithConstraintsDefault", "modifier", "contentAlignment", "propagateMinConstraints", "!content")]

// androidx.compose.foundation.text.selection.SelectionContainerKt.SelectionContainer —
// `(modifier=Modifier, content)`. Only `modifier` is defaultable;
// `content` is always provided. `DisableSelection(content)` takes no
// optional params, so no `$default` slot and no enum is needed for it.
[assembly: ComposeDefaults("SelectionContainerDefault", "modifier", "!content")]

// androidx.compose.foundation.layout.FlowLayoutKt — the simpler
// FlowRow / FlowColumn overloads (no FlowRowOverflow / FlowColumnOverflow
// slot) lower to 7 user params + content. The trailing `maxItemsInEachRow`
// / `maxLines` (resp. `maxItemsInEachColumn` / `maxLines`) Ints can't be
// auto-masked from a nullable C# slot, so the v1 facade leaves both bits
// set and lets Kotlin substitute Int.MAX_VALUE.
[assembly: ComposeDefaults("FlowRowDefault",
    "modifier", "horizontalArrangement", "verticalArrangement",
    "itemVerticalAlignment", "maxItemsInEachRow", "maxLines", "!content")]
[assembly: ComposeDefaults("FlowColumnDefault",
    "modifier", "verticalArrangement", "horizontalArrangement",
    "itemHorizontalAlignment", "maxItemsInEachColumn", "maxLines", "!content")]
[assembly: ComposeDefaults<DividerKt>("HorizontalDivider", "HorizontalDividerDefault")]
[assembly: ComposeDefaults<DividerKt>("VerticalDivider", "VerticalDividerDefault")]
[assembly: ComposeDefaults<IconKt>("Icon", "IconDefault")]
[assembly: ComposeDefaults<MaterialThemeKt>("MaterialTheme", "MaterialThemeDefault")]

// androidx.compose.animation.AnimatedVisibilityKt.AnimatedVisibility(bool, ...) —
// declarative form (not generic) because AnimatedVisibilityKt has multiple
// overloads (Boolean, MutableTransitionState, Transition<T> extension,
// RowScope/ColumnScope extensions) and the generic form picks the *longest*
// overload, which is not necessarily the (boolean visible, ...) one we call.
// Pinning the parameter list here keeps the bit positions in lockstep with
// the bytecode of the specific overload `AnimatedVisibility.Render` invokes.
[assembly: ComposeDefaults("AnimatedVisibilityDefault",
    "!visible", "modifier", "enter", "exit", "label", "!content")]

// androidx.compose.material3.Shapes — synthetic-default constructor,
// 5 CornerBasedShape slots. Hand-rolled JNI (ShapesBridges.cs) calls
// the ctor; this enum names the bits so MaterialTheme.BuildShapes can
// build the $default mask without magic numbers.
[assembly: ComposeDefaults("ShapesDefault",
    "extraSmall", "small", "medium", "large", "extraLarge")]

// androidx.compose.material3.ColorScheme — synthetic-default
// lightColorScheme / darkColorScheme factories, 48 Color slots in
// Kotlin source order. Hand-rolled JNI (ColorSchemeBridges.cs) calls
// the pair-of-int factory; this enum names the bits so
// MaterialTheme.BuildColorScheme can build the $default mask without
// magic numbers. The slot count (48 > 31) triggers the generator's
// long-backed enum + Split helper (see #178), and BuildColorScheme
// calls .Split() to get the (mask0, mask1) pair Kotlin expects.
[assembly: ComposeDefaults("ColorSchemeDefault",
    "primary",                "onPrimary",
    "primaryContainer",       "onPrimaryContainer",
    "inversePrimary",
    "secondary",              "onSecondary",
    "secondaryContainer",     "onSecondaryContainer",
    "tertiary",               "onTertiary",
    "tertiaryContainer",      "onTertiaryContainer",
    "background",             "onBackground",
    "surface",                "onSurface",
    "surfaceVariant",         "onSurfaceVariant",
    "surfaceTint",
    "inverseSurface",         "inverseOnSurface",
    "error",                  "onError",
    "errorContainer",         "onErrorContainer",
    "outline",                "outlineVariant",
    "scrim",
    "surfaceBright",
    "surfaceContainer",       "surfaceContainerHigh",
    "surfaceContainerHighest","surfaceContainerLow",
    "surfaceContainerLowest",
    "surfaceDim",
    "primaryFixed",           "primaryFixedDim",
    "onPrimaryFixed",         "onPrimaryFixedVariant",
    "secondaryFixed",         "secondaryFixedDim",
    "onSecondaryFixed",       "onSecondaryFixedVariant",
    "tertiaryFixed",          "tertiaryFixedDim",
    "onTertiaryFixed",        "onTertiaryFixedVariant")]

// androidx.compose.material3.Typography — synthetic-default
// constructor, 15 TextStyle slots in Kotlin source order.
// Hand-rolled JNI (TypographyBridges.cs) calls the ctor; this enum
// names the bits so MaterialTheme.BuildTypography can build the
// $default mask without magic numbers.
[assembly: ComposeDefaults("TypographyDefault",
    "displayLarge",  "displayMedium",  "displaySmall",
    "headlineLarge", "headlineMedium", "headlineSmall",
    "titleLarge",    "titleMedium",    "titleSmall",
    "bodyLarge",     "bodyMedium",     "bodySmall",
    "labelLarge",    "labelMedium",    "labelSmall")]

// androidx.compose.foundation.lazy.LazyDslKt — the LazyColumn / LazyRow
// @Composable functions take no inline-class params (modifier/state/etc.
// are all reference types or plain bools), so the binder exposes the
// Kt class and the generic generator path Just Works. 9 optional params
// per overload (modifier through overscrollEffect; content lambda is
// skipped as IFunction1).
[assembly: ComposeDefaults<LazyDslKt>("LazyColumn", "LazyColumnDefault")]
[assembly: ComposeDefaults<LazyDslKt>("LazyRow", "LazyRowDefault")]

// androidx.compose.foundation.lazy.grid.LazyGridDslKt — same story for
// LazyVerticalGrid / LazyHorizontalGrid, but with a required first
// `columns` / `rows` IGridCells param. The facade always supplies that
// param and clears the bit (mirror of how Icon clears
// IconDefault.ImageVector / IconDefault.ContentDescription).
[assembly: ComposeDefaults<LazyGridDslKt>("LazyVerticalGrid", "LazyVerticalGridDefault")]
[assembly: ComposeDefaults<LazyGridDslKt>("LazyHorizontalGrid", "LazyHorizontalGridDefault")]

// androidx.compose.foundation.lazy.staggeredgrid.LazyStaggeredGridDslKt —
// LazyVerticalStaggeredGrid / LazyHorizontalStaggeredGrid take a required
// first `columns` / `rows` IStaggeredGridCells param (Bit 0 is always
// cleared; the facade always provides it). The remaining params are all
// optional; bit positions follow Kotlin source order.
[assembly: ComposeDefaults("LazyVerticalStaggeredGridDefault",
    "columns", "modifier", "state", "contentPadding", "reverseLayout",
    "verticalItemSpacing", "horizontalArrangement", "flingBehavior",
    "userScrollEnabled", "overscrollEffect", "!content")]
[assembly: ComposeDefaults("LazyHorizontalStaggeredGridDefault",
    "rows", "modifier", "state", "contentPadding", "reverseLayout",
    "verticalArrangement", "horizontalItemSpacing", "flingBehavior",
    "userScrollEnabled", "overscrollEffect", "!content")]

// androidx.compose.foundation.pager.PagerKt — HorizontalPager /
// VerticalPager take a required first PagerState (always provided by the
// facade) and a required trailing pageContent IFunction4 (also always
// provided). The 13 optional params in between are all candidates for
// the $default mask.
[assembly: ComposeDefaults("HorizontalPagerDefault",
    "!state", "modifier", "contentPadding", "pageSize",
    "beyondViewportPageCount", "pageSpacing", "verticalAlignment",
    "flingBehavior", "userScrollEnabled", "reverseLayout", "key",
    "pageNestedScrollConnection", "snapPosition", "overscrollEffect",
    "!pageContent")]
[assembly: ComposeDefaults("VerticalPagerDefault",
    "!state", "modifier", "contentPadding", "pageSize",
    "beyondViewportPageCount", "pageSpacing", "horizontalAlignment",
    "flingBehavior", "userScrollEnabled", "reverseLayout", "key",
    "pageNestedScrollConnection", "snapPosition", "overscrollEffect",
    "!pageContent")]

// androidx.compose.foundation.ImageKt.Image (Painter overload): all four
// `Image` Kotlin overloads share the JVM name `Image` and only differ by
// first-param type, so the binder strips them all. 7 user params; bit 0
// (painter) is always provided by the Image facade.
[assembly: ComposeDefaults("ImageDefault",
    "!painter", "contentDescription", "modifier", "alignment",
    "contentScale", "alpha", "colorFilter")]

// androidx.compose.material3.IconKt.Icon-ww6aTOc (Painter overload):
// the Painter and ImageBitmap overloads collide with the bound
// ImageVector overload (same mangled JVM name `Icon-ww6aTOc`) and are
// stripped from the binding. 4 user params; bit 0 (painter) is always
// provided by the Icon facade. ContentDescription/Modifier/Tint are
// optional. A separate enum is required because the matching bridge
// generator needs Kotlin parameter names (the `IconDefault` enum is
// produced via the generic `[ComposeDefaults<IconKt>]` form, which the
// bridge generator doesn't read).
[assembly: ComposeDefaults("IconPainterDefault",
    "!painter", "contentDescription", "modifier", "tint")]

// androidx.compose.material3.ButtonKt.Button: 10 user params,
// bit 0 = onClick, bit 9 = content (both always provided).
[assembly: ComposeDefaults("ButtonDefault",
    "!onClick", "modifier", "enabled", "shape", "colors",
    "elevation", "border", "contentPadding", "interactionSource", "!content")]

// androidx.compose.material3.TextKt.Text--4IGK_g: 17 user params,
// bit 0 = text (always provided).
[assembly: ComposeDefaults("TextDefault",
    "!text", "modifier", "color", "fontSize", "fontStyle",
    "fontWeight", "fontFamily", "letterSpacing", "decoration", "align",
    "lineHeight", "overflow", "softWrap", "maxLines", "minLines",
    "onTextLayout", "style")]

// androidx.compose.material3.TextKt.Text-IbK3jfQ — the AnnotatedString
// overload. Same shape as TextDefault but with the extra `inlineContent`
// Map slot between minLines and onTextLayout, for a total of 18 bits.
// bit 0 = text (always provided); inlineContent, onTextLayout, style
// are never exposed by the AnnotatedText facade so the auto-mask leaves
// their default bits set.
[assembly: ComposeDefaults("AnnotatedTextDefault",
    "!text", "modifier", "color", "fontSize", "fontStyle",
    "fontWeight", "fontFamily", "letterSpacing", "decoration", "align",
    "lineHeight", "overflow", "softWrap", "maxLines", "minLines",
    "inlineContent", "onTextLayout", "style")]

// androidx.compose.material3.IconButtonKt.IconButton: 6 user params,
// bit 0 = onClick, bit 5 = content (both provided).
[assembly: ComposeDefaults("IconButtonDefault",
    "!onClick", "modifier", "enabled", "colors", "interactionSource", "!content")]

// androidx.compose.material3.IconButtonKt.{FilledIconButton,FilledTonalIconButton}:
// 7 user params, bit 0 = onClick, bit 6 = content (both provided).
// Filled and FilledTonal variants share the same Kotlin signature so they
// reuse one enum.
[assembly: ComposeDefaults("FilledIconButtonDefault",
    "!onClick", "modifier", "enabled", "shape", "colors",
    "interactionSource", "!content")]

// androidx.compose.material3.IconButtonKt.OutlinedIconButton: 8 user
// params, bit 0 = onClick, bit 7 = content (both provided).
[assembly: ComposeDefaults("OutlinedIconButtonDefault",
    "!onClick", "modifier", "enabled", "shape", "colors", "border",
    "interactionSource", "!content")]

// androidx.compose.material3.IconButtonKt.IconToggleButton: 7 user params,
// bits 0 (checked), 1 (onCheckedChange), 6 (content) always provided.
[assembly: ComposeDefaults("IconToggleButtonDefault",
    "!checked", "!onCheckedChange", "modifier", "enabled", "colors",
    "interactionSource", "!content")]

// androidx.compose.material3.IconButtonKt.{FilledIconToggleButton,FilledTonalIconToggleButton}:
// 8 user params, bits 0 (checked), 1 (onCheckedChange), 7 (content)
// always provided. Filled and FilledTonal share one enum.
[assembly: ComposeDefaults("FilledIconToggleButtonDefault",
    "!checked", "!onCheckedChange", "modifier", "enabled", "shape",
    "colors", "interactionSource", "!content")]

// androidx.compose.material3.IconButtonKt.OutlinedIconToggleButton:
// 9 user params, bits 0 (checked), 1 (onCheckedChange), 8 (content)
// always provided.
[assembly: ComposeDefaults("OutlinedIconToggleButtonDefault",
    "!checked", "!onCheckedChange", "modifier", "enabled", "shape",
    "colors", "border", "interactionSource", "!content")]

// androidx.compose.material3.FloatingActionButtonKt.FloatingActionButton-X-z6DiA:
// 8 user params, bit 0 = onClick, bit 7 = content (both provided).
[assembly: ComposeDefaults("FloatingActionButtonDefault",
    "!onClick", "modifier", "shape", "containerColor", "contentColor",
    "elevation", "interactionSource", "!content")]

// androidx.compose.material3.FloatingActionButtonKt.SmallFloatingActionButton-X-z6DiA:
// same shape as FloatingActionButton.
[assembly: ComposeDefaults("SmallFloatingActionButtonDefault",
    "!onClick", "modifier", "shape", "containerColor", "contentColor",
    "elevation", "interactionSource", "!content")]

// androidx.compose.material3.FloatingActionButtonKt.LargeFloatingActionButton-X-z6DiA:
// same shape as FloatingActionButton.
[assembly: ComposeDefaults("LargeFloatingActionButtonDefault",
    "!onClick", "modifier", "shape", "containerColor", "contentColor",
    "elevation", "interactionSource", "!content")]

// androidx.compose.material3.FloatingActionButtonKt.ExtendedFloatingActionButton-ElI5-7k:
// 10 user params; text/icon/onClick/expanded always provided by the caller.
[assembly: ComposeDefaults("ExtendedFloatingActionButtonDefault",
    "!text", "!icon", "!onClick", "modifier", "!expanded", "shape",
    "containerColor", "contentColor", "elevation", "interactionSource")]

// androidx.compose.material3.SurfaceKt.Surface-T9BRK9s (non-interactive):
// 8 user params, only bit 7 = content provided.
[assembly: ComposeDefaults("SurfaceDefault",
    "modifier", "shape", "color", "contentColor", "tonalElevation",
    "shadowElevation", "border", "!content")]

// androidx.compose.material3.AndroidAlertDialog_androidKt.AlertDialog-Oix01E0:
// 14 user params, bit 0 = onDismissRequest, bit 1 = confirmButton
// (both always provided). The four slot Function2 params (dismissButton,
// icon, title, text) are toggled per-call by AlertDialog.Render — they
// stay as enum members so callers can OR them in conditionally.
[assembly: ComposeDefaults("AlertDialogDefault",
    "!onDismissRequest", "!confirmButton", "modifier", "dismissButton",
    "icon", "title", "text", "shape", "containerColor", "iconContentColor",
    "titleContentColor", "textContentColor", "tonalElevation", "properties")]

// androidx.compose.material3.ModalBottomSheet_androidKt.ModalBottomSheet-dYc4hso:
// 13 user params; bits 0 (onDismissRequest), 2 (sheetState), 12 (content) always provided.
[assembly: ComposeDefaults("ModalBottomSheetDefault",
    "!onDismissRequest", "modifier", "!sheetState", "sheetMaxWidth", "shape",
    "containerColor", "contentColor", "tonalElevation", "scrimColor", "dragHandle",
    "windowInsets", "properties", "!content")]

// androidx.compose.material3.ScaffoldKt.Scaffold-TvnljyQ:
// 10 user params; bit 9 (content) always provided. Optional slot bits
// 1 (topBar), 2 (bottomBar), 3 (snackbarHost), 4 (floatingActionButton)
// are toggled per-call by Scaffold.Render.
[assembly: ComposeDefaults("ScaffoldDefault",
    "modifier", "topBar", "bottomBar", "snackbarHost", "floatingActionButton",
    "floatingActionButtonPosition", "containerColor", "contentColor",
    "contentWindowInsets", "!content")]

// androidx.compose.material3.BottomSheetScaffoldKt.BottomSheetScaffold-sdMYb0k:
// 17 user params; bits 0 (sheetContent), 2 (scaffoldState), 16 (content) always provided.
[assembly: ComposeDefaults("BottomSheetScaffoldDefault",
    "!sheetContent", "modifier", "!scaffoldState", "sheetPeekHeight", "sheetMaxWidth",
    "sheetShape", "sheetContainerColor", "sheetContentColor", "sheetTonalElevation",
    "sheetShadowElevation", "sheetDragHandle", "sheetSwipeEnabled", "topBar",
    "snackbarHost", "containerColor", "contentColor", "!content")]

// androidx.compose.material3.DatePickerDialog_androidKt.DatePickerDialog-GmEhDVc:
// 9 user params; bits 0 (onDismissRequest), 1 (confirmButton), 8 (content) always provided.
[assembly: ComposeDefaults("DatePickerDialogDefault",
    "!onDismissRequest", "!confirmButton", "modifier", "dismissButton", "shape",
    "tonalElevation", "colors", "properties", "!content")]

// androidx.compose.material3.DatePickerKt.DatePicker:
// 8 user params; bit 0 (state) always provided.
[assembly: ComposeDefaults("DatePickerDefault",
    "!state", "modifier", "dateFormatter", "colors", "title",
    "headline", "showModeToggle", "requestFocus")]

// androidx.compose.material3.DateRangePickerKt.DateRangePicker:
// 8 user params; bit 0 (state) always provided.
[assembly: ComposeDefaults("DateRangePickerDefault",
    "!state", "modifier", "dateFormatter", "colors", "title",
    "headline", "showModeToggle", "requestFocus")]

// androidx.compose.material3.TimePickerKt.TimePicker-mT9BvqQ:
// 4 user params; bit 0 (state) always provided.
[assembly: ComposeDefaults("TimePickerDefault",
    "!state", "modifier", "colors", "layoutType")]

// androidx.compose.material3.TimePickerKt.TimeInput:
// 3 user params; bit 0 (state) always provided.
[assembly: ComposeDefaults("TimeInputDefault",
    "!state", "modifier", "colors")]

// androidx.compose.material3.TimePickerDialogKt.TimePickerDialog-FItCLgY:
// 10 user params; bits 0 (onDismissRequest), 1 (confirmButton),
// 2 (dismissButton), 9 (content) always provided.
[assembly: ComposeDefaults("TimePickerDialogDefault",
    "!onDismissRequest", "!confirmButton", "!dismissButton", "modifier", "properties",
    "title", "modeToggleButton", "shape", "containerColor", "!content")]

// androidx.compose.material3.TooltipKt.TooltipBox (7-param overload):
// 7 user params; bits 0 (positionProvider), 1 (tooltip), 2 (state),
// 6 (content) always provided.
[assembly: ComposeDefaults("TooltipBoxDefault",
    "!positionProvider", "!tooltip", "!state", "modifier", "focusable",
    "enableUserInput", "!content")]

// androidx.compose.material3.TextFieldKt.TextField (String overload) AND
// OutlinedTextFieldKt.OutlinedTextField (String overload): 23 user params,
// bit 0 = value, bit 1 = onValueChange (both provided).
[assembly: ComposeDefaults("TextFieldDefault",
    "!value", "!onValueChange", "modifier", "enabled", "readOnly",
    "textStyle", "label", "placeholder", "leadingIcon", "trailingIcon",
    "prefix", "suffix", "supportingText", "isError", "visualTransformation",
    "keyboardOptions", "keyboardActions", "singleLine", "maxLines", "minLines",
    "interactionSource", "shape", "colors")]

// androidx.compose.material3.SecureTextFieldKt.SecureTextField-XvU6IwQ AND
// OutlinedSecureTextField-XvU6IwQ: 23 user params, bit 0 = state (always
// provided through the SecureTextFieldState wrapper). Two enums (one per
// JVM function) so each [ComposeBridge] can disambiguate via its own
// `Defaults` typeof — the bit layout is identical between filled and
// outlined variants.
[assembly: ComposeDefaults("SecureTextFieldDefault",
    "!state",
    "modifier", "enabled", "textStyle", "labelPosition",
    "label", "placeholder", "leadingIcon", "trailingIcon",
    "prefix", "suffix", "supportingText", "isError",
    "inputTransformation", "textObfuscationMode", "textObfuscationCharacter",
    "keyboardOptions", "onKeyboardAction", "onTextLayout",
    "shape", "colors", "contentPadding", "interactionSource")]

[assembly: ComposeDefaults("OutlinedSecureTextFieldDefault",
    "!state",
    "modifier", "enabled", "textStyle", "labelPosition",
    "label", "placeholder", "leadingIcon", "trailingIcon",
    "prefix", "suffix", "supportingText", "isError",
    "inputTransformation", "textObfuscationMode", "textObfuscationCharacter",
    "keyboardOptions", "onKeyboardAction", "onTextLayout",
    "shape", "colors", "contentPadding", "interactionSource")]

// androidx.compose.material3.CardKt.Card (non-clickable): 6 user params,
// bit 5 = content provided. Also reused by OutlinedCard — it takes the
// same 6 params in the same order.
[assembly: ComposeDefaults("CardDefault",
    "modifier", "shape", "colors", "elevation", "border", "!content")]

// androidx.compose.material3.CardKt.ElevatedCard (non-clickable): 5 user
// params (no border) — bit 4 = content provided.
[assembly: ComposeDefaults("ElevatedCardDefault",
    "modifier", "shape", "colors", "elevation", "!content")]

// androidx.compose.material3.NavigationDrawerKt.{Modal,Dismissible,Permanent}DrawerSheet-afqeVBk:
// all three have identical 7-param signatures (modifier, shape,
// drawerContainerColor, drawerContentColor, tonalElevation, windowInsets,
// content). Bit 6 (content) always provided.
[assembly: ComposeDefaults("DrawerSheetDefault",
    "modifier", "shape", "drawerContainerColor", "drawerContentColor",
    "tonalElevation", "windowInsets", "!content")]

// androidx.compose.material3.NavigationDrawerKt.ModalNavigationDrawer.
// 6 Kotlin user params. The facade ALWAYS supplies drawerContent (Drawer
// slot), drawerState (Phase 4b StateHolder auto-creates a wrapper), and
// content (Content slot); modifier is the only auto-mask-controlled bit.
// gesturesEnabled and scrimColor stay set so Kotlin substitutes its
// defaults (true / DrawerDefaults.scrimColor); the wrapper-passthrough
// body passes dummy values that Kotlin ignores when the bit is set.
[assembly: ComposeDefaults("ModalNavigationDrawerDefault",
    "!drawerContent", "modifier", "!drawerState", "gesturesEnabled",
    "scrimColor", "!content")]

// androidx.compose.material3.NavigationDrawerKt.DismissibleNavigationDrawer.
// 5 Kotlin user params (same as ModalNavigationDrawer minus scrimColor —
// the dismissible drawer has no scrim).
[assembly: ComposeDefaults("DismissibleNavigationDrawerDefault",
    "!drawerContent", "modifier", "!drawerState", "gesturesEnabled",
    "!content")]

// androidx.compose.material3.NavigationDrawerKt.PermanentNavigationDrawer.
// 3 Kotlin user params — no state holder (drawer is always visible), no
// gesturesEnabled (can't be swiped closed), no scrimColor (no scrim).
[assembly: ComposeDefaults("PermanentNavigationDrawerDefault",
    "!drawerContent", "modifier", "!content")]

// androidx.compose.material3.ChipKt.AssistChip: 11 user params,
// bit 0 = onClick, bit 1 = label (both always provided).
// Optional slot bits 4 (LeadingIcon) and 5 (TrailingIcon) are toggled
// per-call by AssistChip.Render.
[assembly: ComposeDefaults("AssistChipDefault",
    "!onClick", "!label", "modifier", "enabled", "leadingIcon", "trailingIcon",
    "shape", "colors", "elevation", "border", "interactionSource")]

// androidx.compose.material3.ChipKt.FilterChip: 12 user params,
// bits 0 = selected, 1 = onClick, 2 = label (all always provided).
[assembly: ComposeDefaults("FilterChipDefault",
    "!selected", "!onClick", "!label", "modifier", "enabled",
    "leadingIcon", "trailingIcon", "shape", "colors", "elevation",
    "border", "interactionSource")]

// androidx.compose.material3.ChipKt.InputChip: 13 user params,
// bits 0 = selected, 1 = onClick, 2 = label (all always provided).
[assembly: ComposeDefaults("InputChipDefault",
    "!selected", "!onClick", "!label", "modifier", "enabled",
    "leadingIcon", "avatar", "trailingIcon", "shape", "colors",
    "elevation", "border", "interactionSource")]

// androidx.compose.material3.ChipKt.SuggestionChip: 10 user params,
// bit 0 = onClick, bit 1 = label (both always provided).
[assembly: ComposeDefaults("SuggestionChipDefault",
    "!onClick", "!label", "modifier", "enabled", "icon",
    "shape", "colors", "elevation", "border", "interactionSource")]

// androidx.compose.material3.NavigationBarKt.NavigationBar-HsRjFd4:
// 6 user params, bit 5 = content provided.
[assembly: ComposeDefaults("NavigationBarDefault",
    "modifier", "containerColor", "contentColor", "tonalElevation",
    "windowInsets", "!content")]

// androidx.compose.material3.NavigationBarKt.NavigationBarItem: 9 user
// params after the RowScope receiver (the receiver is not part of the
// $default bitmask). Bits 0 = selected, 1 = onClick, 2 = icon (all
// always provided). The optional Label slot is toggled by
// NavigationBarItem.Render.
[assembly: ComposeDefaults("NavigationBarItemDefault",
    "!selected", "!onClick", "!icon", "modifier", "enabled", "label",
    "alwaysShowLabel", "colors", "interactionSource")]

// androidx.compose.material3.NavigationDrawerKt.NavigationDrawerItem:
// 9 user params (no extension receiver). Bits 0 = label, 1 = selected,
// 2 = onClick are required and always provided by the caller; the
// optional Icon and Badge slots are toggled per-call by
// NavigationDrawerItem.Render via the auto-mask. shape, colors, and
// interactionSource stay defaulted (Kotlin defaults applied).
[assembly: ComposeDefaults("NavigationDrawerItemDefault",
    "!label", "!selected", "!onClick", "modifier", "icon", "badge",
    "shape", "colors", "interactionSource")]

// androidx.compose.foundation.BackgroundKt.background-bw27NRU$default —
// non-@Composable Modifier extension. Mangled because Color is a
// @JvmInline value class (ULong). Bit 0 = color (always supplied by
// the C# wrapper), bit 1 = shape (Compose substitutes RectangleShape
// when defaulted).
[assembly: ComposeDefaults("ModifierBackgroundDefault", "!color", "shape")]

// androidx.compose.foundation.BackgroundKt.background$default —
// Brush-typed overload. Bit 0 = brush (always supplied), bit 1 = shape
// (Compose substitutes RectangleShape when defaulted), bit 2 = alpha
// (always supplied; the C# wrapper defaults to 1f to match Kotlin).
[assembly: ComposeDefaults("ModifierBackgroundBrushDefault",
    "!brush", "shape", "!alpha")]

// androidx.compose.foundation.BorderKt.border-xT4_qwU$default —
// non-@Composable Modifier extension. Both width (Dp) and color
// (Color) are @JvmInline value classes, hence the mangled name.
// Bits 0/1 = width/color (always supplied), bit 2 = shape.
[assembly: ComposeDefaults("ModifierBorderDefault", "!width", "!color", "shape")]

// androidx.compose.foundation.BorderKt.border-ziNgDLE$default —
// Brush-typed overload. width is a Dp inline-class param (hence the
// mangled name); brush is a regular object reference. Bits 0/1 =
// width/brush (always supplied), bit 2 = shape.
[assembly: ComposeDefaults("ModifierBorderBrushDefault",
    "!width", "!brush", "shape")]

// androidx.compose.foundation.ClickableKt.clickable-XHw0xAI$default —
// non-@Composable Modifier extension. Bit 3 (onClick) is always
// supplied by the C# wrapper; bits 0/1/2 (enabled/onClickLabel/role)
// are left to Kotlin's defaults.
[assembly: ComposeDefaults("ModifierClickableDefault",
    "enabled", "onClickLabel", "role", "!onClick")]

// androidx.compose.foundation.ClickableKt.combinedClickable-cJG_KMw$default —
// non-@Composable Modifier extension (no MutableInteractionSource overload).
// 7 Kotlin params after the receiver. The C# wrapper always supplies
// onClick (bit 6 always cleared); enabled / onClickLabel / role / onLongClickLabel
// are left to Kotlin's defaults; the optional onLongClick / onDoubleClick
// slots are auto-cleared per-call when the caller passes a non-null
// IFunction0 (Kotlin requires nullability here — a null callback DOES
// substitute Kotlin's default of "ignore that gesture").
[assembly: ComposeDefaults("ModifierCombinedClickableDefault",
    "enabled", "onClickLabel", "role", "onLongClickLabel",
    "onLongClick", "onDoubleClick", "!onClick")]

// androidx.compose.foundation.selection.SelectableKt.selectable-XHw0xAI$default —
// 4 Kotlin params after the receiver: selected, enabled, role, onClick.
// C# wrapper always supplies selected + onClick.
[assembly: ComposeDefaults("ModifierSelectableDefault",
    "!selected", "enabled", "role", "!onClick")]

// androidx.compose.foundation.selection.ToggleableKt.toggleable-XHw0xAI$default —
// 4 Kotlin params after the receiver: value, enabled, role, onValueChange.
// C# wrapper always supplies value + onValueChange.
[assembly: ComposeDefaults("ModifierToggleableDefault",
    "!value", "enabled", "role", "!onValueChange")]

// androidx.compose.foundation.FocusableKt.focusable$default — 2 Kotlin
// params after the receiver: enabled (always supplied by C#) and
// interactionSource (left to Kotlin's default).
[assembly: ComposeDefaults("ModifierFocusableDefault",
    "!enabled", "interactionSource")]

// androidx.compose.ui.semantics.SemanticsModifierKt.semantics$default —
// 2 Kotlin params after the receiver: mergeDescendants (always supplied
// by the C# wrapper, the `Semantics(string)` helper hard-codes `false`)
// and properties (the configuration Function1, always supplied).
[assembly: ComposeDefaults("ModifierSemanticsDefault",
    "mergeDescendants", "!properties")]

// androidx.compose.ui.semantics.SemanticsPropertiesKt.onClick$default —
// 2 Kotlin params after the receiver: label (String?, optional — caller
// may omit to get the platform-default label) and action (Function0,
// always supplied by the C# wrapper).
[assembly: ComposeDefaults("SemanticsOnClickDefault",
    "label", "!action")]

// androidx.compose.foundation.gestures.DraggableKt.draggable$default —
// non-@Composable Modifier extension. 8 Kotlin params after the
// receiver: state, orientation, enabled, interactionSource,
// startDragImmediately, onDragStarted, onDragStopped, reverseDirection.
// C# wrapper supplies state/orientation/enabled (bits 0/1/2 cleared);
// the remaining five slots fall through to Kotlin's defaults.
[assembly: ComposeDefaults("ModifierDraggableDefault",
    "!state", "!orientation", "!enabled",
    "interactionSource", "startDragImmediately",
    "onDragStarted", "onDragStopped", "reverseDirection")]

// androidx.compose.foundation.ScrollKt.verticalScroll$default —
// non-@Composable Modifier extension. 4 Kotlin params after the
// receiver: state, enabled, flingBehavior, reverseScrolling. The C#
// wrapper always supplies state/enabled/reverseScrolling (bits 0/1/3
// cleared) and leaves flingBehavior to Kotlin's default (bit 2 set,
// substitutes ScrollableDefaults.flingBehavior()).
[assembly: ComposeDefaults("ModifierVerticalScrollDefault",
    "!state", "!enabled", "flingBehavior", "!reverseScrolling")]

// androidx.compose.foundation.ScrollKt.horizontalScroll$default —
// same shape as ModifierVerticalScrollDefault above.
[assembly: ComposeDefaults("ModifierHorizontalScrollDefault",
    "!state", "!enabled", "flingBehavior", "!reverseScrolling")]

// androidx.compose.foundation.layout.{Row,Column}Scope$DefaultImpls.weight$default —
// non-@Composable scope extension. Shared by both Row and Column
// weight bridges since the helper signatures are identical apart from
// the dispatch-receiver class. The bridge generator treats the first
// IntPtr (rowScope / columnScope) as the JNI receiver (bound to
// args[0], NOT a $default bit). The remaining Kotlin parameters in
// declaration order are: the extension receiver Modifier (bit 0,
// always supplied), weight (bit 1, always supplied), fill (bit 2,
// optional — Kotlin's default is true). Our wrapper always passes
// fill explicitly, so the generator clears bit 2 too and the helper
// uses our value.
[assembly: ComposeDefaults("ModifierWeightDefault",
    "!modifier", "!weight", "fill")]

// androidx.navigation.compose.NavHostKt.NavHost — Kotlin signature
// `NavHost(navController, startDestination, modifier=Modifier,
// route=null, builder)`. Bits 0/1/4 are required params (always
// supplied) so they're suppressed; bits 2 (modifier) and 3 (route)
// are optional and the auto-mask in the bridge generator clears them
// when the caller provides a non-null value.
[assembly: ComposeDefaults("NavHostDefault",
    "!navController", "!startDestination", "modifier", "route", "!builder")]

// androidx.navigation.compose.NavGraphBuilderKt.composable — Kotlin
// extension `NavGraphBuilder.composable(route, arguments=emptyList(),
// deepLinks=emptyList(), content)`. The receiver is NOT in the
// $default bitmask. Bit 0 (route) and bit 3 (content) are required,
// so suppressed; bits 1/2 (arguments/deepLinks) are optional and
// driven by the auto-mask — passing null leaves the bit set so
// Kotlin substitutes its emptyList() default.
[assembly: ComposeDefaults("NavComposableDefault",
    "!route", "arguments", "deepLinks", "!content")]

// androidx.compose.foundation.layout.SizeKt — ranged size constraints.
// Each bit corresponds to one Dp parameter. C# bridges declare each
// param as `Dp?`, so the auto-mask clears the bit only when the user
// supplies a non-null value (otherwise Kotlin substitutes
// `Dp.Unspecified` — no constraint on that side).
[assembly: ComposeDefaults("ModifierWidthInDefault", "min", "max")]
[assembly: ComposeDefaults("ModifierHeightInDefault", "min", "max")]
[assembly: ComposeDefaults("ModifierSizeInDefault",
    "minWidth", "minHeight", "maxWidth", "maxHeight")]
[assembly: ComposeDefaults("ModifierDefaultMinSizeDefault",
    "minWidth", "minHeight")]

// androidx.compose.foundation.layout.SizeKt — wrapContent variants.
// Bit 0 = align (left to Kotlin's default — Center, etc.; the
// param has no C# slot, generator emits IntPtr.Zero into the JNI
// arg and the bit stays set). Bit 1 = unbounded — always supplied
// by the C# wrapper, so the auto-mask must clear the bit; do NOT
// prefix with `!` (that would skip auto-mask clearing and Kotlin
// would always use its `false` default).
[assembly: ComposeDefaults("ModifierWrapContentSizeDefault",
    "align", "unbounded")]
[assembly: ComposeDefaults("ModifierWrapContentWidthDefault",
    "align", "unbounded")]
[assembly: ComposeDefaults("ModifierWrapContentHeightDefault",
    "align", "unbounded")]

// androidx.compose.foundation.layout.AspectRatioKt.aspectRatio$default —
// (Modifier, Float ratio, Boolean matchHeightConstraintsFirst). Bit 0
// = ratio (always supplied), bit 1 = matchHeightConstraintsFirst (cleared
// when the C# caller supplies a value; default false matches Kotlin).
[assembly: ComposeDefaults("ModifierAspectRatioDefault",
    "!ratio", "matchHeightConstraintsFirst")]

// androidx.compose.foundation.layout.OffsetKt.offset / absoluteOffset —
// (Modifier, Dp x, Dp y). Both Dp params are nullable in the C#
// wrapper; null leaves Kotlin's default of 0.dp on that axis.
[assembly: ComposeDefaults("ModifierOffsetDefault", "x", "y")]
[assembly: ComposeDefaults("ModifierAbsoluteOffsetDefault", "x", "y")]

// androidx.compose.ui.draw.ShadowKt.shadow-ziNgDLE$default —
// (Modifier, Dp elevation, Shape shape, Boolean clip). Bit 0 =
// elevation (always supplied), bit 1 = shape (auto-mask honors
// null), bit 2 = clip (left to Kotlin's default of
// `elevation > 0.dp`).
[assembly: ComposeDefaults("ModifierShadowDefault",
    "!elevation", "shape", "clip")]

// androidx.compose.ui.graphics.GraphicsLayerModifierKt.graphicsLayer-sKFY_QE$default —
// 13 user params, all defaultable. Caller passes null to skip,
// non-null to override.
[assembly: ComposeDefaults("ModifierGraphicsLayerDefault",
    "scaleX", "scaleY", "alpha",
    "translationX", "translationY", "shadowElevation",
    "rotationX", "rotationY", "rotationZ",
    "cameraDistance", "transformOrigin", "shape", "clip")]

// androidx.compose.material3.NavigationRailKt.NavigationRail-qi6gXK8:
// 6 user params, bit 5 = content provided.
[assembly: ComposeDefaults("NavigationRailDefault",
    "modifier", "containerColor", "contentColor", "header",
    "windowInsets", "!content")]

// androidx.compose.material3.NavigationRailKt.NavigationRailItem:
// 9 user params; bits 0 = selected, 1 = onClick, 2 = icon
// (all always provided).
[assembly: ComposeDefaults("NavigationRailItemDefault",
    "!selected", "!onClick", "!icon", "modifier", "enabled", "label",
    "alwaysShowLabel", "colors", "interactionSource")]

// androidx.compose.material3.DatePickerKt.rememberDatePickerState-EU0dCGE:
// 5 user params, all defaulted by the wrapper (which exposes none).
[assembly: ComposeDefaults("RememberDatePickerStateDefault",
    "initialSelectedDateMillis", "initialDisplayedMonthMillis", "yearRange",
    "initialDisplayMode", "selectableDates")]

// androidx.compose.material3.DateRangePickerKt.rememberDateRangePickerState-IlFM19s:
// 6 user params, all defaulted by the wrapper (which exposes none).
[assembly: ComposeDefaults("RememberDateRangePickerStateDefault",
    "initialSelectedStartDateMillis", "initialSelectedEndDateMillis",
    "initialDisplayedMonthMillis", "yearRange",
    "initialDisplayMode", "selectableDates")]

// androidx.compose.material3.TimePickerKt.rememberTimePickerState:
// 3 user params, all always provided by the wrapper.
[assembly: ComposeDefaults("RememberTimePickerStateDefault",
    "!initialHour", "!initialMinute", "!is24Hour")]

// androidx.compose.material3.TooltipKt.rememberTooltipState:
// 3 user params; `isPersistent` always provided, the other two
// (`initialIsVisible`, `mutatorMutex`) are defaulted.
[assembly: ComposeDefaults("RememberTooltipStateDefault",
    "initialIsVisible", "!isPersistent", "mutatorMutex")]

// androidx.compose.material3.TooltipDefaults.rememberPlainTooltipPositionProvider-kHDZbjc:
// 1 user param (`spacing`), defaulted by the wrapper.
[assembly: ComposeDefaults("RememberPlainTooltipPositionProviderDefault",
    "spacing")]

// androidx.compose.material3.SegmentedButtonKt.{Single,Multi}ChoiceSegmentedButtonRow-uFdPcIQ:
// 3 user params, bit 2 (content) always provided. Both row variants
// share the same signature so they reuse one enum. The JNI descriptor
// is `(...;Composer;II)V` — two trailing `I` slots, so the row HAS a
// `$default` slot (3 user params → 1 `$changed` group → trailingInts=2
// > expectedChangedSlots=1). The bound binding's parameter names are
// misleading: the binder labels position 4 as `p4` ($changed) and
// position 5 as `_changed` ($default). Call sites pass this enum's
// mask to the `_changed:` named arg, which is positionally `$default`.
[assembly: ComposeDefaults("SegmentedButtonRowDefault",
    "modifier", "space", "!content")]

// androidx.compose.material3.SegmentedButtonKt.SegmentedButton (longer
// 11-param overload with PaddingValues) — SingleChoiceSegmentedButtonRowScope
// receiver. Bits 0 (selected), 1 (onClick), 2 (shape), 10 (label) always
// provided — `shape` has no Kotlin default expression so its $default bit
// is a no-op; the caller must pass a real Shape (resolved via
// SegmentedButtonDefaults.itemShape). Bit 9 (icon) is optional and
// toggled per-call by SegmentedButton.Render.
[assembly: ComposeDefaults("SingleChoiceSegmentedButtonDefault",
    "!selected", "!onClick", "!shape", "modifier", "enabled",
    "colors", "border", "contentPadding", "interactionSource",
    "icon", "!label")]

// androidx.compose.material3.SegmentedButtonKt.SegmentedButton (longer
// 11-param overload with PaddingValues) — MultiChoiceSegmentedButtonRowScope
// receiver. Bits 0 (checked), 1 (onCheckedChange), 2 (shape), 10 (label)
// always provided. Bit 9 (icon) is optional and toggled per-call.
[assembly: ComposeDefaults("MultiChoiceSegmentedButtonDefault",
    "!checked", "!onCheckedChange", "!shape", "modifier", "enabled",
    "colors", "border", "contentPadding", "interactionSource",
    "icon", "!label")]

// androidx.compose.material3.SegmentedButtonDefaults.itemShape: 3 user
// params on the SegmentedButtonDefaults Kotlin `object` instance method.
// `index` and `count` are always provided; `baseShape` is omitted from
// the C# user-facing bridge, so the source generator auto-sets bit 2 to
// have Kotlin substitute SegmentedButtonDefaults.getBaseShape() (the
// theme's default rounded-corner shape).
[assembly: ComposeDefaults("SegmentedButtonItemShapeDefault",
    "!index", "!count", "baseShape")]

// androidx.compose.material3.WideNavigationRailKt.WideNavigationRail.
// 8 user params; bit 7 (content) always provided. The optional `header`
// slot (bit 4) is a member of the enum so a future overload of the
// facade can toggle it; today WideNavigationRail.Render always leaves
// it as a default ($default bit set).
[assembly: ComposeDefaults("WideNavigationRailDefault",
    "modifier", "state", "shape", "colors", "header",
    "windowInsets", "arrangement", "!content")]

// androidx.compose.material3.WideNavigationRailKt.WideNavigationRailItem-pli-t6k:
// 10 user params; bits 0 (selected), 1 (onClick), 2 (icon) always provided.
[assembly: ComposeDefaults("WideNavigationRailItemDefault",
    "!selected", "!onClick", "!icon", "label", "railExpanded",
    "modifier", "enabled", "iconPosition", "colors", "interactionSource")]

// androidx.compose.material3.WideNavigationRailKt.ModalWideNavigationRail-k3FuEkE:
// 12 user params; bits 1 (state) and 11 (content) always provided by the
// facade. The facade also always sets `hideOnCollapse=true`, but we keep
// that bit as an enum member because future overloads may toggle it.
[assembly: ComposeDefaults("ModalWideNavigationRailDefault",
    "modifier", "!state", "hideOnCollapse", "collapsedShape", "expandedShape",
    "colors", "header", "collapsedShadowElevation", "windowInsets", "arrangement",
    "properties", "!content")]

// androidx.compose.material3.ProgressIndicatorKt.LinearProgressIndicator-rIrjwxo
// (indeterminate, no progress callback). 5 user params, all optional.
[assembly: ComposeDefaults("LinearProgressIndicatorDefault",
    "modifier", "color", "trackColor", "strokeCap", "gapSize")]

// androidx.compose.material3.ProgressIndicatorKt.LinearProgressIndicator-2cYBFYY
// (deprecated determinate float-progress overload). 5 user params:
// progress is always provided so bit 0 stays cleared via the `!`
// prefix; modifier/color/trackColor/strokeCap are optional.
[assembly: ComposeDefaults("LinearProgressIndicatorDeterminateDefault",
    "!progress", "modifier", "color", "trackColor", "strokeCap")]

// androidx.compose.material3.ProgressIndicatorKt.CircularProgressIndicator-4lLiAd8
// (indeterminate). 6 user params, all optional.
[assembly: ComposeDefaults("CircularProgressIndicatorDefault",
    "modifier", "color", "strokeWidth", "trackColor", "strokeCap", "gapSize")]

// androidx.compose.material3.CheckboxKt.Checkbox: 6 user params,
// bit 0 = checked (always provided), bit 1 = onCheckedChange (always
// provided — Function1, the user passes a callback).
[assembly: ComposeDefaults("CheckboxDefault",
    "!checked", "!onCheckedChange", "modifier", "enabled",
    "colors", "interactionSource")]

// androidx.compose.material3.CheckboxKt.TriStateCheckbox: 6 user params,
// bit 0 = state (always provided). onClick is Function0? defaulting to
// null in Kotlin; the facade always provides a callback.
[assembly: ComposeDefaults("TriStateCheckboxDefault",
    "!state", "!onClick", "modifier", "enabled",
    "colors", "interactionSource")]

// androidx.compose.material3.RadioButtonKt.RadioButton: 6 user params,
// bit 0 = selected (always provided). onClick is Function0?, the facade
// always provides a callback.
[assembly: ComposeDefaults("RadioButtonDefault",
    "!selected", "!onClick", "modifier", "enabled",
    "colors", "interactionSource")]

// androidx.compose.material3.SwitchKt.Switch: 7 user params,
// bit 0 = checked (always provided), bit 1 = onCheckedChange (always
// provided). bit 3 = thumbContent is Function2? with Kotlin default
// null; the facade doesn't expose it, so the bit stays set in `All`
// and Kotlin substitutes the default null at the call site.
[assembly: ComposeDefaults("SwitchDefault",
    "!checked", "!onCheckedChange", "modifier", "thumbContent",
    "enabled", "colors", "interactionSource")]

// androidx.compose.material3.SwitchDefaults.colors(...): 16-slot
// $default mask covering thumb/track/border/icon for each of the
// checked, unchecked, disabled-checked, and disabled-unchecked
// states. Used by `composer.SwitchColors(...)` to flip individual
// bits as caller-supplied overrides arrive. Order matches the
// bytecode lowering (every parameter is a `@JvmInline value class
// Color` lowered to `long`, so the 16-slot factory is fully bound).
[assembly: ComposeDefaults("SwitchColorsDefault",
    "checkedThumbColor", "checkedTrackColor",
    "checkedBorderColor", "checkedIconColor",
    "uncheckedThumbColor", "uncheckedTrackColor",
    "uncheckedBorderColor", "uncheckedIconColor",
    "disabledCheckedThumbColor", "disabledCheckedTrackColor",
    "disabledCheckedBorderColor", "disabledCheckedIconColor",
    "disabledUncheckedThumbColor", "disabledUncheckedTrackColor",
    "disabledUncheckedBorderColor", "disabledUncheckedIconColor")]

// androidx.compose.material3.SliderDefaults.colors(...): 10-slot
// $default mask covering thumb / activeTrack / activeTick /
// inactiveTrack / inactiveTick plus the four disabled siblings.
// Used by `composer.SliderColors(...)` to flip bits as caller-
// supplied overrides arrive. Order matches the bytecode lowering
// of the parameterised `colors-q0g_0yA(JJJJJJJJJJ;Composer;III)`
// factory.
[assembly: ComposeDefaults("SliderColorsDefault",
    "thumbColor", "activeTrackColor", "activeTickColor",
    "inactiveTrackColor", "inactiveTickColor",
    "disabledThumbColor", "disabledActiveTrackColor",
    "disabledActiveTickColor", "disabledInactiveTrackColor",
    "disabledInactiveTickColor")]

// androidx.compose.material3.SliderKt.Slider (simple float overload):
// 9 user params; bits 0 (value) and 1 (onValueChange) always provided.
// The longer overload with Function3 thumb/track slots has non-null
// Kotlin defaults that can't be safely substituted, so we lock in this
// simpler shape via the declarative form.
[assembly: ComposeDefaults("SliderDefault",
    "!value", "!onValueChange", "modifier", "enabled", "valueRange",
    "steps", "onValueChangeFinished", "colors", "interactionSource")]

// androidx.compose.material3.SliderKt.RangeSlider (simple
// ClosedFloatingPointRange overload): 8 user params; bits 0 (value)
// and 1 (onValueChange) always provided.
[assembly: ComposeDefaults("RangeSliderDefault",
    "!value", "!onValueChange", "modifier", "enabled", "valueRange",
    "steps", "onValueChangeFinished", "colors")]

// androidx.compose.material3.AppBarKt.{TopAppBar,CenterAlignedTopAppBar}-GHTll3U:
// 8 user params; bit 0 (title) always provided. Optional slot bits 2
// (NavigationIcon) and 3 (Actions) are toggled per-call by the facades.
[assembly: ComposeDefaults("TopAppBarDefault",
    "!title", "modifier", "navigationIcon", "actions", "expandedHeight",
    "windowInsets", "colors", "scrollBehavior")]

// androidx.compose.material3.AppBarKt.{Medium,Large}TopAppBar-oKE7A98:
// 9 user params; bit 0 (title) always provided. Two-row variants take
// both collapsedHeight and expandedHeight as Dp.
[assembly: ComposeDefaults("TwoRowsTopAppBarDefault",
    "!title", "modifier", "navigationIcon", "actions", "collapsedHeight",
    "expandedHeight", "windowInsets", "colors", "scrollBehavior")]

// androidx.compose.material3.TabRowKt.{TabRow,PrimaryTabRow,SecondaryTabRow}-pAZo6Ak:
// 7 user params; bits 0 (selectedTabIndex) and 6 (tabs) always provided.
[assembly: ComposeDefaults("TabRowDefault",
    "!selectedTabIndex", "modifier", "containerColor", "contentColor",
    "indicator", "divider", "!tabs")]

// androidx.compose.material3.TabRowKt.ScrollableTabRow-sKfQg0A:
// 8 user params; bits 0 (selectedTabIndex) and 7 (tabs) always provided.
[assembly: ComposeDefaults("ScrollableTabRowDefault",
    "!selectedTabIndex", "modifier", "containerColor", "contentColor",
    "edgePadding", "indicator", "divider", "!tabs")]

// androidx.compose.material3.TabKt.Tab-wqdebIU: 9 user params; bits 0
// (selected) and 1 (onClick) always provided. text/icon are optional
// slots toggled per-call by Tab.Render.
[assembly: ComposeDefaults("TabDefault",
    "!selected", "!onClick", "modifier", "enabled", "text", "icon",
    "selectedContentColor", "unselectedContentColor", "interactionSource")]

// androidx.compose.material3.TabKt.LeadingIconTab-wqdebIU: 9 user params;
// bits 0 (selected), 1 (onClick), 2 (text), 3 (icon) all always provided
// — Kotlin requires text and icon (no defaults).
[assembly: ComposeDefaults("LeadingIconTabDefault",
    "!selected", "!onClick", "!text", "!icon", "modifier", "enabled",
    "selectedContentColor", "unselectedContentColor", "interactionSource")]

// androidx.compose.material3.SnackbarKt.Snackbar-eQBnUkQ: 10 user params;
// bit 9 (content) always provided. action/dismissAction are optional
// slots toggled per-call by Snackbar.Render.
[assembly: ComposeDefaults("SnackbarDefault",
    "modifier", "action", "dismissAction", "actionOnNewLine", "shape",
    "containerColor", "contentColor", "actionColor", "actionContentColor",
    "!content")]

// androidx.compose.material3.SnackbarHostKt.SnackbarHost: 3 user params;
// bits 0 (hostState) and 2 (snackbar) always provided.
[assembly: ComposeDefaults("SnackbarHostDefault",
    "!hostState", "modifier", "!snackbar")]

// androidx.compose.material3.BadgeKt.Badge-eopBjH0: 4 user params; bit
// 3 (content) always provided.
[assembly: ComposeDefaults("BadgeDefault",
    "modifier", "containerColor", "contentColor", "!content")]

// androidx.compose.material3.BadgeKt.BadgedBox: 3 user params; bits 0
// (badge) and 2 (content) always provided.
[assembly: ComposeDefaults("BadgedBoxDefault",
    "!badge", "modifier", "!content")]

// androidx.compose.material3.ListItemKt.ListItem-HXNGIdc: 9 user params;
// bit 0 (headlineContent) always provided. The four optional slot
// Function2 params (overline, supporting, leading, trailing) are
// toggled per-call by ListItem.Render.
[assembly: ComposeDefaults("ListItemDefault",
    "!headlineContent", "modifier", "overlineContent", "supportingContent",
    "leadingContent", "trailingContent", "colors", "tonalElevation",
    "shadowElevation")]

// androidx.compose.material3.AndroidMenu_androidKt.DropdownMenu-IlH_yew:
// 12 user params; bits 0 (expanded), 1 (onDismissRequest), 11 (content)
// always provided.
[assembly: ComposeDefaults("DropdownMenuDefault",
    "!expanded", "!onDismissRequest", "modifier", "offset", "scrollState",
    "properties", "shape", "containerColor", "tonalElevation", "shadowElevation",
    "border", "!content")]

// androidx.compose.material3.SearchBarKt.rememberSearchBarState: 3 user
// params; all defaulted by the wrapper (which exposes none).
[assembly: ComposeDefaults("RememberSearchBarStateDefault",
    "initialValue", "animationSpecForExpand", "animationSpecForCollapse")]

// androidx.compose.material3.SearchBarKt.SearchBar-nbWgWpA (state-based):
// 7 user params; bits 0 (state), 1 (inputField) always provided.
[assembly: ComposeDefaults("SearchBarDefault",
    "!state", "!inputField", "modifier", "shape", "colors",
    "inputFieldHeight", "floatingHeight")]

// androidx.compose.material3.SearchBarKt.TopSearchBar-qKj4JfE:
// 9 user params; bits 0 (state), 1 (inputField) always provided.
[assembly: ComposeDefaults("TopSearchBarDefault",
    "!state", "!inputField", "modifier", "shape", "colors",
    "inputFieldHeight", "floatingHeight", "windowInsets", "scrollBehavior")]

// androidx.compose.material3.SearchBarKt.ExpandedDockedSearchBar-qKj4JfE:
// 9 user params; bits 0 (state), 1 (inputField), 8 (content) always provided.
[assembly: ComposeDefaults("ExpandedDockedSearchBarDefault",
    "!state", "!inputField", "modifier", "shape", "colors",
    "inputFieldHeight", "floatingHeight", "properties", "!content")]

// androidx.compose.material3.SearchBarKt.ExpandedFullScreenSearchBar-_UtchM0:
// 10 user params; bits 0 (state), 1 (inputField), 9 (content) always provided.
[assembly: ComposeDefaults("ExpandedFullScreenSearchBarDefault",
    "!state", "!inputField", "modifier", "shape", "colors",
    "inputFieldHeight", "floatingHeight", "windowInsets", "properties", "!content")]

// androidx.compose.material3.SearchBarDefaults.InputField (state-based):
// 18 user params. Bits 0 (textFieldState), 1 (searchBarState),
// 2 (onSearch) are always provided by the SearchBarInputField facade —
// onSearch defaults to a no-op ComposableLambda1 so Kotlin never invokes
// a null callback on IME Search.
[assembly: ComposeDefaults("SearchBarDefaultsInputFieldDefault",
    "!textFieldState", "!searchBarState", "!onSearch",
    "modifier", "enabled", "autoFocus", "textStyle",
    "placeholder", "leadingIcon", "trailingIcon", "prefix", "suffix",
    "inputTransformation", "outputTransformation", "scrollState",
    "shape", "colors", "interactionSource")]

// androidx.compose.material3.AndroidMenu_androidKt.DropdownMenuItem: 9 user
// params; bits 0 (text), 1 (onClick) always provided. Declarative form used
// (instead of generic) so leadingIcon and trailingIcon get enum members the
// facade can clear when the caller supplies those optional slots.
[assembly: ComposeDefaults("DropdownMenuItemDefault",
    "!text", "!onClick", "modifier", "leadingIcon", "trailingIcon",
    "enabled", "colors", "contentPadding", "interactionSource")]

// androidx.compose.material3.ExposedDropdownMenuKt.ExposedDropdownMenuBox:
// 4 user params; bits 0 (expanded), 1 (onExpandedChange), 3 (content)
// always provided.
[assembly: ComposeDefaults("ExposedDropdownMenuBoxDefault",
    "!expanded", "!onExpandedChange", "modifier", "!content")]

// androidx.compose.material3.ExposedDropdownMenuBoxScope.ExposedDropdownMenu
// (the unmangled instance-method overload): 5 user params;
// bits 0 (expanded), 1 (onDismissRequest), 4 (content) always provided.
// ScrollState (bit 3) is intentionally exposed so callers can keep the
// menu's scroll position in sync with their own state holder.
[assembly: ComposeDefaults("ExposedDropdownMenuDefault",
    "!expanded", "!onDismissRequest", "modifier", "scrollState", "!content")]

// androidx.compose.material3.SearchBarKt.DockedSearchBar-EQC0FA8 (boolean-
// state, deprecated): 9 user params; bits 0 (inputField), 1 (expanded),
// 2 (onExpandedChange), 8 (content) always provided.
[assembly: ComposeDefaults("DockedSearchBarDefault",
    "!inputField", "!expanded", "!onExpandedChange", "modifier", "shape",
    "colors", "tonalElevation", "shadowElevation", "!content")]

// androidx.compose.material3.SearchBarKt.DockedSearchBar-eWTbjVg (query-
// based, even older / deprecated): 16 user params; bits 0–4 (query,
// onQueryChange, onSearch, active, onActiveChange) and bit 15 (content)
// always provided. The unmapped slots (enabled, shape, colors,
// tonalElevation, shadowElevation, interactionSource) are not exposed on
// the facade and stay defaulted by Kotlin.
[assembly: ComposeDefaults("DockedSearchBarWithQueryDefault",
    "!query", "!onQueryChange", "!onSearch", "!active", "!onActiveChange",
    "modifier", "enabled", "placeholder", "leadingIcon", "trailingIcon",
    "shape", "colors", "tonalElevation", "shadowElevation", "interactionSource",
    "!content")]

// androidx.compose.material3.AppBarKt.TopAppBar-cJHQLPU (subtitle overload):
// 10 user params; bits 0 (title) and 1 (subtitle) always provided.
// Optional slot bits 3 (NavigationIcon) and 4 (Actions) are toggled
// per-call by TopAppBar.Render when Subtitle is set.
[assembly: ComposeDefaults("TopAppBarSubtitleDefault",
    "!title", "!subtitle", "modifier", "navigationIcon", "actions",
    "titleHorizontalAlignment", "expandedHeight", "windowInsets",
    "colors", "scrollBehavior")]

// androidx.compose.material3.AppBarKt.{Medium,Large}FlexibleTopAppBar-eXZ4JBQ:
// 11 user params; bit 0 (title) always provided. Optional slot bits
// 2 (Subtitle), 3 (NavigationIcon), 4 (Actions) toggled per-call.
// 11 params * 3 bits/param > 31, so the bytecode emits two `$changed`
// ints in addition to `$default` (trailing `III`).
[assembly: ComposeDefaults("FlexibleTopAppBarDefault",
    "!title", "modifier", "subtitle", "navigationIcon", "actions",
    "titleHorizontalAlignment", "collapsedHeight", "expandedHeight",
    "windowInsets", "colors", "scrollBehavior")]

// androidx.compose.material3.AppBarKt.BottomAppBar-qhFBPw4 (RowScope actions
// + optional FAB + scrollBehavior overload — the most flexible of the four
// `BottomAppBar` overloads; the older variants are intentionally not bound):
// 9 user params; bit 0 (actions) always provided. Optional FAB slot
// (bit 2) toggled per-call by BottomAppBar.Render.
[assembly: ComposeDefaults("BottomAppBarDefault",
    "!actions", "modifier", "floatingActionButton", "containerColor",
    "contentColor", "tonalElevation", "contentPadding", "windowInsets",
    "scrollBehavior")]

// androidx.compose.material3.AppBarKt.FlexibleBottomAppBar-wBhsO_E:
// 9 user params; bit 8 (content) always provided.
[assembly: ComposeDefaults("FlexibleBottomAppBarDefault",
    "modifier", "containerColor", "contentColor", "contentPadding",
    "horizontalArrangement", "expandedHeight", "windowInsets",
    "scrollBehavior", "!content")]

// androidx.compose.material3.TabRowKt.{Primary,Secondary}ScrollableTabRow-qhFBPw4:
// 9 user params; bits 0 (selectedTabIndex) and 8 (tabs) always provided.
[assembly: ComposeDefaults("PrimaryScrollableTabRowDefault",
    "!selectedTabIndex", "modifier", "scrollState", "containerColor",
    "contentColor", "edgePadding", "indicator", "divider", "!tabs")]

// androidx.compose.material3.TabKt.Tab-bogVsAg (ColumnScope content
// overload — alternative to the text/icon `Tab-wqdebIU`): 8 user
// params; bits 0 (selected), 1 (onClick), 7 (content) always provided.
[assembly: ComposeDefaults("TabContentDefault",
    "!selected", "!onClick", "modifier", "enabled", "selectedContentColor",
    "unselectedContentColor", "interactionSource", "!content")]

// androidx.compose.material3.SnackbarKt.Snackbar-sDKtq54 (SnackbarData
// overload — the one normally rendered inside SnackbarHost's default
// content lambda): 9 user params; bit 0 (snackbarData) always provided.
[assembly: ComposeDefaults("SnackbarFromDataDefault",
    "!snackbarData", "modifier", "actionOnNewLine", "shape", "containerColor",
    "contentColor", "actionColor", "actionContentColor",
    "dismissActionContentColor")]

// androidx.compose.material3.pulltorefresh.PullToRefreshKt.PullToRefreshBox:
// 7 user params. The [ComposeFacade] generator surfaces ctor params for
// isRefreshing / onRefresh (bits 0/1) and a state holder for bit 3 (via
// the [StateHolder] attribute on the bridge), so those three bits are
// suppressed ("!" prefix — consume the bit position, emit no enum
// member). Bits 4/5 (contentAlignment / indicator) are not exposed by
// the bridge — JNI passes null for those slots and the bits MUST be
// set in $default so Kotlin substitutes its defaults instead of
// invoking a null Function3 (NPE). Keeping them as enum members lets
// `All` include those bits; nothing surfaced to callers ever clears
// them. Bit 6 (content) is the container content slot, always
// provided by RenderChildren.
[assembly: ComposeDefaults("PullToRefreshBoxDefault",
    "!isRefreshing", "!onRefresh", "modifier", "!state", "contentAlignment",
    "indicator", "!content")]

// androidx.activity.compose.BackHandlerKt.BackHandler — Kotlin
// signature `BackHandler(enabled: Boolean = true, onBack: () -> Unit)`.
// Bit 0 (enabled) is optional with a Kotlin default of true; the C#
// facade always supplies a bool, so the auto-mask clears the bit and
// Kotlin uses our value. Bit 1 (onBack) is required (no Kotlin
// default), `!`-suppressed so the bit stays clear and the caller's
// lambda is always invoked.
[assembly: ComposeDefaults("BackHandlerDefault", "enabled", "!onBack")]

// androidx.compose.ui.input.nestedscroll.NestedScrollModifierKt.nestedScroll$default —
// non-@Composable Modifier extension. 2 Kotlin params after the
// receiver: connection (NestedScrollConnection — always provided by the
// C# wrapper), dispatcher (NestedScrollDispatcher — optional; null →
// Kotlin allocates a new dispatcher internally).
[assembly: ComposeDefaults("ModifierNestedScrollDefault", "!connection", "dispatcher")]

// androidx.compose.material3.TopAppBarDefaults.enterAlwaysScrollBehavior —
// instance method on the INSTANCE singleton. 4 user params: state
// (always provided), canScroll (defaultable), snapAnimationSpec
// (defaultable), flingAnimationSpec (defaultable). The C# wrapper
// supplies only `state` and leaves the other three to Kotlin's
// defaults so a single-arg call replicates Kotlin's idiomatic
// `enterAlwaysScrollBehavior(state)`.
[assembly: ComposeDefaults("TopAppBarEnterAlwaysScrollBehaviorDefault",
    "!state", "canScroll", "snapAnimationSpec", "flingAnimationSpec")]

// androidx.compose.material3.TopAppBarDefaults.exitUntilCollapsedScrollBehavior —
// same shape as enterAlwaysScrollBehavior above.
[assembly: ComposeDefaults("TopAppBarExitUntilCollapsedScrollBehaviorDefault",
    "!state", "canScroll", "snapAnimationSpec", "flingAnimationSpec")]

// androidx.compose.foundation.ScrollState.animateScrollTo$default —
// suspend extension with one defaultable param. Bit 0 = `value` (always
// provided, `!`-prefixed); bit 1 = `animationSpec` (null → Kotlin
// substitutes SpringSpec()). Receiver (ScrollState) and Continuation
// are NOT in the defaults — they're shape parameters, not defaultable.
[assembly: ComposeDefaults("ScrollStateAnimateScrollToDefault",
    "!value", "animationSpec")]

// androidx.compose.foundation.gestures.TapGestureDetectorKt.detectTapGestures$default —
// suspend extension with four defaultable callback params. All four
// callbacks are nullable; the auto-default-mask leaves a bit set when
// the caller passes null so Kotlin substitutes its real default
// (null for the nullables, NoPressGesture for onPress). Receiver
// (PointerInputScope) and Continuation are NOT in the defaults.
[assembly: ComposeDefaults("DetectTapGesturesDefault",
    "onDoubleTap", "onLongPress", "onPress", "onTap")]

// androidx.compose.foundation.gestures.DragGestureDetectorKt.detectDragGestures$default —
// suspend extension with three defaultable callback params
// (onDragStart / onDragEnd / onDragCancel — all `() -> Unit` style
// no-op defaults) plus a required onDrag. Receiver
// (PointerInputScope) and Continuation are NOT in the defaults; the
// required onDrag (the only Function2 slot) is excluded too.
[assembly: ComposeDefaults("DetectDragGesturesDefault",
    "onDragStart", "onDragEnd", "onDragCancel", "!onDrag")]

// androidx.compose.foundation.gestures.TransformGestureDetectorKt.detectTransformGestures$default —
// only `panZoomLock` is defaultable (false), but it's a primitive
// boolean (auto-mask can't flip its bit because primitives are
// never null). We always pass it explicitly, so suppress the enum
// entry. onGesture is required.
[assembly: ComposeDefaults("DetectTransformGesturesDefault",
    "!panZoomLock", "!onGesture")]
