using Android.Runtime;
using AndroidX.Compose.Foundation.Layout;
using AndroidX.Compose.Foundation.Lazy.Grid;
using AndroidX.Compose.Material3;
using AndroidX.Compose.Runtime;
using AndroidX.Compose.UI;
using AndroidX.Compose.UI.State;
using Kotlin.Jvm.Functions;
using Kotlin.Ranges;
using Painter = AndroidX.Compose.UI.Graphics.Painter.Painter;

namespace AndroidX.Compose;

// Raw-JNI bridges to Compose functions the .NET-for-Android binding generator
// can't see (Compose @Composable functions don't get $default sibling overloads
// — the trailing $default bitmask lives on the regular method). The bodies
// for every method tagged with [ComposeBridge] are emitted by
// AndroidX.Compose.SourceGenerators.ComposeBridgeGenerator from the attribute
// metadata + (for $default-bearing signatures) the matching [ComposeDefaults]
// enum names. Plain Kotlin static helpers — no Composer, no $default — don't
// need a [ComposeDefaults] declaration.
//
// A few helpers stay hand-written: ModifierHandle (a managed-side conversion
// from the IModifier wrapper to a raw handle) and ModifierCompanionInstance
// (a static field lookup, not a method invocation).
internal static partial class ComposeBridges
{
    // Convert a managed Modifier wrapper (from `Modifier.Build()`) to a
    // raw JNI handle, or IntPtr.Zero when null. Each bridge that takes
    // a modifier param uses this + KeepAlive's the wrapper across the
    // JNI call so its handle stays alive.
    internal static IntPtr ModifierHandle(IModifier? modifier) =>
        modifier is null ? IntPtr.Zero : ((Java.Lang.Object)modifier).Handle;

    // androidx.compose.ui.Modifier$Companion.$$INSTANCE — the empty
    // Modifier that every chain builds on top of. Cached as a global
    // ref so the chain builder doesn't pay the FindClass +
    // GetStaticObjectField cost on every recomposition.
    static IntPtr s_modifierCompanionInstance;

    internal static unsafe IntPtr ModifierCompanionInstance()
    {
        if (s_modifierCompanionInstance == IntPtr.Zero)
        {
            IntPtr cls = JNIEnv.FindClass("androidx/compose/ui/Modifier$Companion");
            IntPtr fid = JNIEnv.GetStaticFieldID(cls, "$$INSTANCE", "Landroidx/compose/ui/Modifier$Companion;");
            IntPtr local = JNIEnv.GetStaticObjectField(cls, fid);
            s_modifierCompanionInstance = JNIEnv.NewGlobalRef(local);
            JNIEnv.DeleteLocalRef(local);
        }
        // Returning a NEW local ref each call so callers can DeleteLocalRef
        // it uniformly while walking the op chain.
        return JNIEnv.NewLocalRef(s_modifierCompanionInstance);
    }

    // androidx.compose.foundation.lazy.grid.GridCells$Adaptive — the
    // only constructor takes a Dp (`@JvmInline value class Dp(val value: Float)`),
    // mangled to `<init>(F)V` in bytecode, and the binder strips it.
    // The source generator's constructor shape emits FindClass +
    // GetMethodID("<init>") + NewObject + Java.Lang.Object.GetObject<T>
    // with TransferLocalRef. `GridCells.Fixed(int)` doesn't need a
    // bridge (no inline-class mangling); the binder exposes its ctor
    // directly.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/lazy/grid/GridCells$Adaptive",
        JvmName   = "<init>",
        Signature = "(F)V")]
    internal static partial IGridCells GridCellsAdaptive(float minSizeDp);

    // androidx.compose.foundation.lazy.staggeredgrid.StaggeredGridCells$Adaptive
    // and StaggeredGridCells$FixedSize — same Dp inline-class story as
    // GridCells$Adaptive above. StaggeredGridCells.Fixed(int) is
    // unaffected by inline-class mangling and the binder exposes its
    // ctor directly, so no bridge is needed for it.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/lazy/staggeredgrid/StaggeredGridCells$Adaptive",
        JvmName   = "<init>",
        Signature = "(F)V")]
    internal static partial AndroidX.Compose.Foundation.Lazy.Staggeredgrid.IStaggeredGridCells
        StaggeredGridCellsAdaptive(float minSizeDp);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/lazy/staggeredgrid/StaggeredGridCells$FixedSize",
        JvmName   = "<init>",
        Signature = "(F)V")]
    internal static partial AndroidX.Compose.Foundation.Lazy.Staggeredgrid.IStaggeredGridCells
        StaggeredGridCellsFixedSize(float sizeDp);

    // androidx.compose.foundation.layout.PaddingKt — the Dp-taking
    // overloads have hashed JVM names from the inline-class compiler
    // mangling (`@JvmInline value class Dp(val value: Float)`). Bodies
    // are emitted by the source generator's plain-static shape.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/PaddingKt",
        JvmName   = "padding-3ABfNKs",
        Signature = "(Landroidx/compose/ui/Modifier;F)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierPaddingAll(IntPtr modifier, float dp);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/PaddingKt",
        JvmName   = "padding-VpY3zN4",
        Signature = "(Landroidx/compose/ui/Modifier;FF)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierPaddingHV(IntPtr modifier, float horizontal, float vertical);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/PaddingKt",
        JvmName   = "padding-qDBjuR0",
        Signature = "(Landroidx/compose/ui/Modifier;FFFF)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierPaddingLTRB(IntPtr modifier, float start, float top, float end, float bottom);

    // PaddingKt.padding(Modifier, PaddingValues) — unmangled because
    // PaddingValues is a regular interface, not a `value class`.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/PaddingKt",
        JvmName   = "padding",
        Signature = "(Landroidx/compose/ui/Modifier;Landroidx/compose/foundation/layout/PaddingValues;)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierPaddingValues(IntPtr modifier, IntPtr paddingValues);

    // androidx.compose.foundation.layout.SizeKt — fillMax* take a plain
    // Float fraction, not Dp, so the JVM names are NOT mangled.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/SizeKt",
        JvmName   = "fillMaxWidth",
        Signature = "(Landroidx/compose/ui/Modifier;F)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierFillMaxWidth(IntPtr modifier, float fraction);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/SizeKt",
        JvmName   = "fillMaxHeight",
        Signature = "(Landroidx/compose/ui/Modifier;F)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierFillMaxHeight(IntPtr modifier, float fraction);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/SizeKt",
        JvmName   = "fillMaxSize",
        Signature = "(Landroidx/compose/ui/Modifier;F)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierFillMaxSize(IntPtr modifier, float fraction);

    // androidx.compose.foundation.layout.SizeKt — Dp-taking sizing
    // modifiers. The JVM names are mangled like padding's because
    // `Dp` is an inline class (`@JvmInline value class Dp(val value: Float)`).
    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/SizeKt",
        JvmName   = "height-3ABfNKs",
        Signature = "(Landroidx/compose/ui/Modifier;F)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierHeight(IntPtr modifier, float dp);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/SizeKt",
        JvmName   = "width-3ABfNKs",
        Signature = "(Landroidx/compose/ui/Modifier;F)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierWidth(IntPtr modifier, float dp);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/SizeKt",
        JvmName   = "size-3ABfNKs",
        Signature = "(Landroidx/compose/ui/Modifier;F)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierSizeAll(IntPtr modifier, float dp);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/SizeKt",
        JvmName   = "size-VpY3zN4",
        Signature = "(Landroidx/compose/ui/Modifier;FF)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierSizeWH(IntPtr modifier, float width, float height);

    // androidx.compose.foundation.layout.WindowInsetsPadding_androidKt —
    // Modifier extensions that read WindowInsets from CompositionLocals
    // and apply them as padding. Take only Modifier (no Dp), so JVM
    // names are unmangled.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/WindowInsetsPadding_androidKt",
        JvmName   = "safeDrawingPadding",
        Signature = "(Landroidx/compose/ui/Modifier;)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierSafeDrawingPadding(IntPtr modifier);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/WindowInsetsPadding_androidKt",
        JvmName   = "systemBarsPadding",
        Signature = "(Landroidx/compose/ui/Modifier;)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierSystemBarsPadding(IntPtr modifier);

    // androidx.compose.material3.InteractiveComponentSizeKt.minimumInteractiveComponentSize() —
    // declared in `InteractiveComponentSize.kt`, so the synthetic
    // Kotlin file-class is `InteractiveComponentSizeKt`. Reserves at
    // least 48.dp in both dimensions to keep touch targets accessible
    // on icon-only buttons and compact rows. No Composer, no $default.
    [ComposeBridge(
        Class     = "androidx/compose/material3/InteractiveComponentSizeKt",
        JvmName   = "minimumInteractiveComponentSize",
        Signature = "(Landroidx/compose/ui/Modifier;)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierMinimumInteractiveComponentSize(IntPtr modifier);

    // androidx.compose.foundation.shape.RoundedCornerShapeKt.RoundedCornerShape-0680j_4(Dp).
    // Mangled because the lone arg is @JvmInline value class Dp (Float).
    // Plain-static shape: no receiver, no $default, no Composer.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/shape/RoundedCornerShapeKt",
        JvmName   = "RoundedCornerShape-0680j_4",
        Signature = "(F)Landroidx/compose/foundation/shape/RoundedCornerShape;")]
    internal static partial IntPtr RoundedCornerShape(float dp);

    // androidx.compose.foundation.shape.RoundedCornerShapeKt.RoundedCornerShape(Dp, Dp, Dp, Dp).
    // The 4-arg Dp overload keeps a clean name because no Float-arg 4-arg
    // overload exists to collide with — only the 1-arg shape has the
    // Dp/Float overload ambiguity that forces inline-class mangling.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/shape/RoundedCornerShapeKt",
        JvmName   = "RoundedCornerShape",
        Signature = "(FFFF)Landroidx/compose/foundation/shape/RoundedCornerShape;")]
    internal static partial IntPtr RoundedCornerShape4(
        float topStart, float topEnd, float bottomEnd, float bottomStart);

    // androidx.compose.foundation.shape.RoundedCornerShapeKt.RoundedCornerShape(Int, Int, Int, Int)
    // — independent percent-based radii per corner. Int isn't an inline
    // class so no mangling; plain-static shape.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/shape/RoundedCornerShapeKt",
        JvmName   = "RoundedCornerShape",
        Signature = "(IIII)Landroidx/compose/foundation/shape/RoundedCornerShape;")]
    internal static partial IntPtr RoundedCornerShape4Percent(
        int topStartPercent, int topEndPercent, int bottomEndPercent, int bottomStartPercent);

    // androidx.compose.ui.draw.ClipKt.clip(Modifier, Shape) — both args
    // required, no $default and no name mangling. Plain-static shape
    // with a Modifier extension receiver.
    [ComposeBridge(
        Class     = "androidx/compose/ui/draw/ClipKt",
        JvmName   = "clip",
        Signature = "(Landroidx/compose/ui/Modifier;" +
                    "Landroidx/compose/ui/graphics/Shape;)" +
                    "Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierClip(IntPtr modifier, IntPtr shape);

    // Convenience wrapper that composes RoundedCornerShape + ModifierClip
    // and manages the intermediate Shape local ref. Listed as out of
    // scope in #36 — the underlying JNI calls are both generator-driven
    // now; only the two-step orchestration stays hand-written.
    internal static IntPtr ModifierClipRoundedCorners(IntPtr modifier, float dp)
    {
        IntPtr shape = RoundedCornerShape(dp);
        try
        {
            return ModifierClip(modifier, shape);
        }
        finally
        {
            if (shape != IntPtr.Zero)
                JNIEnv.DeleteLocalRef(shape);
        }
    }

    // androidx.compose.ui.res.PainterResources_androidKt.painterResource —
    // returns a NEW local Painter ref the caller is responsible for
    // DeleteLocalRef'ing once it's been handed to the consuming
    // Image/Icon JNI call.
    // DeleteLocalRef'ing once it's been handed to the consuming
    // Image/Icon JNI call.
    [ComposeBridge(
        Class     = "androidx/compose/ui/res/PainterResources_androidKt",
        JvmName   = "painterResource",
        Signature = "(ILandroidx/compose/runtime/Composer;I)Landroidx/compose/ui/graphics/painter/Painter;")]
    public static partial IntPtr PainterResource(int id, IComposer composer);

    // androidx.compose.foundation.layout.RowScope$DefaultImpls.weight$default —
    // synthetic helper for `fun RowScope.Modifier.weight(weight, fill = true)`.
    // Static method taking the dispatch receiver (RowScope) as arg 0,
    // the extension receiver (Modifier) as arg 1, then weight/fill, the
    // $default bitmask, and the synthetic marker. The bridge generator
    // treats the first IntPtr (`rowScope`) as the receiver and binds it
    // to args[0]; everything else flows through ModifierWeightDefault.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/RowScope$DefaultImpls",
        JvmName   = "weight$default",
        Signature = "(Landroidx/compose/foundation/layout/RowScope;" +
                    "Landroidx/compose/ui/Modifier;FZILjava/lang/Object;)" +
                    "Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierWeightDefault))]
    internal static partial IntPtr RowScopeModifierWeight(IntPtr rowScope, IntPtr modifier, float weight, bool fill);

    // androidx.compose.foundation.layout.ColumnScope$DefaultImpls.weight$default —
    // same shape as the RowScope helper above.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/ColumnScope$DefaultImpls",
        JvmName   = "weight$default",
        Signature = "(Landroidx/compose/foundation/layout/ColumnScope;" +
                    "Landroidx/compose/ui/Modifier;FZILjava/lang/Object;)" +
                    "Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierWeightDefault))]
    internal static partial IntPtr ColumnScopeModifierWeight(IntPtr columnScope, IntPtr modifier, float weight, bool fill);

    // Source-generated bridges below. Each [ComposeBridge] partial
    // declaration is paired with a matching [ComposeDefaults] in
    // ComposeDefaults.cs; the generator reads bit positions and parameter
    // names from the enum and emits the cache fields, lazy class/method
    // ID resolution, JValue array fill, $default bitmask, and try/finally
    // with GC.KeepAlive.

    // androidx.compose.material3.TextKt.Text--4IGK_g
    [ComposeBridge(
        Class     = "androidx/compose/material3/TextKt",
        JvmName   = "Text--4IGK_g",
        Signature = "(Ljava/lang/String;Landroidx/compose/ui/Modifier;JJ" +
                    "Landroidx/compose/ui/text/font/FontStyle;" +
                    "Landroidx/compose/ui/text/font/FontWeight;" +
                    "Landroidx/compose/ui/text/font/FontFamily;J" +
                    "Landroidx/compose/ui/text/style/TextDecoration;" +
                    "Landroidx/compose/ui/text/style/TextAlign;JIZII" +
                    "Lkotlin/jvm/functions/Function1;Landroidx/compose/ui/text/TextStyle;" +
                    "Landroidx/compose/runtime/Composer;III)V",
        Defaults  = typeof(TextDefault))]
    [ComposeFacade]
    public static partial void Text(
        string text,
        IModifier? modifier,
        Color? color,
        Sp? fontSize,
        FontStyle? fontStyle,
        FontWeight? fontWeight,
        FontFamily? fontFamily,
        Sp? letterSpacing,
        TextDecoration? decoration,
        TextAlign? align,
        Sp? lineHeight,
        TextOverflow? overflow,
        bool? softWrap,
        int? maxLines,
        int? minLines,
        IComposer composer);

    // androidx.compose.material3.TextKt.Text-IbK3jfQ — the
    // AnnotatedString overload. Mangled JVM name because Kotlin
    // value-class params (Color/TextUnit/TextDecoration/TextAlign)
    // produce a `-XxxxXXX` suffix that strips the overload from the
    // binding. Has one extra slot vs the string variant: a `Map`
    // `inlineContent` between minLines and onTextLayout. Hand-paired
    // with the AnnotatedText facade (Text-generated facade can't host a
    // second constructor pointing at a different bridge); see
    // AnnotatedText.cs.
    [ComposeBridge(
        Class     = "androidx/compose/material3/TextKt",
        JvmName   = "Text-IbK3jfQ",
        Signature = "(Landroidx/compose/ui/text/AnnotatedString;Landroidx/compose/ui/Modifier;JJ" +
                    "Landroidx/compose/ui/text/font/FontStyle;" +
                    "Landroidx/compose/ui/text/font/FontWeight;" +
                    "Landroidx/compose/ui/text/font/FontFamily;J" +
                    "Landroidx/compose/ui/text/style/TextDecoration;" +
                    "Landroidx/compose/ui/text/style/TextAlign;JIZII" +
                    "Ljava/util/Map;Lkotlin/jvm/functions/Function1;" +
                    "Landroidx/compose/ui/text/TextStyle;" +
                    "Landroidx/compose/runtime/Composer;III)V",
        Defaults  = typeof(AnnotatedTextDefault))]
    public static partial void TextAnnotated(
        AnnotatedString text,
        IModifier? modifier,
        Color? color,
        Sp? fontSize,
        FontStyle? fontStyle,
        FontWeight? fontWeight,
        FontFamily? fontFamily,
        Sp? letterSpacing,
        TextDecoration? decoration,
        TextAlign? align,
        Sp? lineHeight,
        TextOverflow? overflow,
        bool? softWrap,
        int? maxLines,
        int? minLines,
        IComposer composer);

    // androidx.compose.material3.ButtonKt.Button
    [ComposeBridge(
        Class     = "androidx/compose/material3/ButtonKt",
        JvmName   = "Button",
        Signature = "(Lkotlin/jvm/functions/Function0;Landroidx/compose/ui/Modifier;Z" +
                    "Landroidx/compose/ui/graphics/Shape;Landroidx/compose/material3/ButtonColors;" +
                    "Landroidx/compose/material3/ButtonElevation;Landroidx/compose/foundation/BorderStroke;" +
                    "Landroidx/compose/foundation/layout/PaddingValues;" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Lkotlin/jvm/functions/Function3;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(ButtonDefault))]
    [ComposeFacade]
    public static partial void Button(IFunction0 onClick, IModifier? modifier,
                                      Shape? shape, AndroidX.Compose.Material3.ButtonColors? colors,
                                      PaddingValues? contentPadding,
                                      IFunction3 content, IComposer composer);

    // androidx.compose.material3.ButtonKt.OutlinedButton — same Kotlin
    // signature as Button (10 user params with shape/colors/elevation/
    // border/contentPadding/interactionSource), so it reuses ButtonDefault.
    [ComposeBridge(
        Class     = "androidx/compose/material3/ButtonKt",
        JvmName   = "OutlinedButton",
        Signature = "(Lkotlin/jvm/functions/Function0;Landroidx/compose/ui/Modifier;Z" +
                    "Landroidx/compose/ui/graphics/Shape;Landroidx/compose/material3/ButtonColors;" +
                    "Landroidx/compose/material3/ButtonElevation;Landroidx/compose/foundation/BorderStroke;" +
                    "Landroidx/compose/foundation/layout/PaddingValues;" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Lkotlin/jvm/functions/Function3;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(ButtonDefault))]
    [ComposeFacade]
    public static partial void OutlinedButton(IFunction0 onClick, IModifier? modifier,
                                              Shape? shape, AndroidX.Compose.Material3.ButtonColors? colors,
                                              PaddingValues? contentPadding,
                                              IFunction3 content, IComposer composer);

    // androidx.compose.material3.ButtonKt.TextButton — same shape as Button.
    [ComposeBridge(
        Class     = "androidx/compose/material3/ButtonKt",
        JvmName   = "TextButton",
        Signature = "(Lkotlin/jvm/functions/Function0;Landroidx/compose/ui/Modifier;Z" +
                    "Landroidx/compose/ui/graphics/Shape;Landroidx/compose/material3/ButtonColors;" +
                    "Landroidx/compose/material3/ButtonElevation;Landroidx/compose/foundation/BorderStroke;" +
                    "Landroidx/compose/foundation/layout/PaddingValues;" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Lkotlin/jvm/functions/Function3;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(ButtonDefault))]
    [ComposeFacade]
    public static partial void TextButton(IFunction0 onClick, IModifier? modifier,
                                          Shape? shape, AndroidX.Compose.Material3.ButtonColors? colors,
                                          PaddingValues? contentPadding,
                                          IFunction3 content, IComposer composer);

    // androidx.compose.material3.ButtonKt.ElevatedButton — same shape as Button.
    [ComposeBridge(
        Class     = "androidx/compose/material3/ButtonKt",
        JvmName   = "ElevatedButton",
        Signature = "(Lkotlin/jvm/functions/Function0;Landroidx/compose/ui/Modifier;Z" +
                    "Landroidx/compose/ui/graphics/Shape;Landroidx/compose/material3/ButtonColors;" +
                    "Landroidx/compose/material3/ButtonElevation;Landroidx/compose/foundation/BorderStroke;" +
                    "Landroidx/compose/foundation/layout/PaddingValues;" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Lkotlin/jvm/functions/Function3;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(ButtonDefault))]
    [ComposeFacade]
    public static partial void ElevatedButton(IFunction0 onClick, IModifier? modifier,
                                              Shape? shape, AndroidX.Compose.Material3.ButtonColors? colors,
                                              PaddingValues? contentPadding,
                                              IFunction3 content, IComposer composer);

    // androidx.compose.material3.ButtonKt.FilledTonalButton — same shape as Button.
    [ComposeBridge(
        Class     = "androidx/compose/material3/ButtonKt",
        JvmName   = "FilledTonalButton",
        Signature = "(Lkotlin/jvm/functions/Function0;Landroidx/compose/ui/Modifier;Z" +
                    "Landroidx/compose/ui/graphics/Shape;Landroidx/compose/material3/ButtonColors;" +
                    "Landroidx/compose/material3/ButtonElevation;Landroidx/compose/foundation/BorderStroke;" +
                    "Landroidx/compose/foundation/layout/PaddingValues;" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Lkotlin/jvm/functions/Function3;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(ButtonDefault))]
    [ComposeFacade]
    public static partial void FilledTonalButton(IFunction0 onClick, IModifier? modifier,
                                                 Shape? shape, AndroidX.Compose.Material3.ButtonColors? colors,
                                                 PaddingValues? contentPadding,
                                                 IFunction3 content, IComposer composer);

    // androidx.compose.material3.IconButtonKt.IconButton
    [ComposeBridge(
        Class     = "androidx/compose/material3/IconButtonKt",
        JvmName   = "IconButton",
        Signature = "(Lkotlin/jvm/functions/Function0;Landroidx/compose/ui/Modifier;Z" +
                    "Landroidx/compose/material3/IconButtonColors;" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Lkotlin/jvm/functions/Function2;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(IconButtonDefault))]
    [ComposeFacade]
    public static partial void IconButton(IFunction0 onClick, IModifier? modifier,
                                          IFunction2 content, IComposer composer);

    // androidx.compose.material3.IconButtonKt.FilledIconButton — adds Shape
    // before colors compared to plain IconButton (7 user params).
    [ComposeBridge(
        Class     = "androidx/compose/material3/IconButtonKt",
        JvmName   = "FilledIconButton",
        Signature = "(Lkotlin/jvm/functions/Function0;Landroidx/compose/ui/Modifier;Z" +
                    "Landroidx/compose/ui/graphics/Shape;" +
                    "Landroidx/compose/material3/IconButtonColors;" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Lkotlin/jvm/functions/Function2;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(FilledIconButtonDefault))]
    [ComposeFacade]
    public static partial void FilledIconButton(IFunction0 onClick, IModifier? modifier,
                                                Shape? shape,
                                                IFunction2 content, IComposer composer);

    // androidx.compose.material3.IconButtonKt.FilledTonalIconButton — same
    // signature as FilledIconButton.
    [ComposeBridge(
        Class     = "androidx/compose/material3/IconButtonKt",
        JvmName   = "FilledTonalIconButton",
        Signature = "(Lkotlin/jvm/functions/Function0;Landroidx/compose/ui/Modifier;Z" +
                    "Landroidx/compose/ui/graphics/Shape;" +
                    "Landroidx/compose/material3/IconButtonColors;" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Lkotlin/jvm/functions/Function2;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(FilledIconButtonDefault))]
    [ComposeFacade]
    public static partial void FilledTonalIconButton(IFunction0 onClick, IModifier? modifier,
                                                     Shape? shape,
                                                     IFunction2 content, IComposer composer);

    // androidx.compose.material3.IconButtonKt.OutlinedIconButton — adds
    // BorderStroke between colors and interactionSource (8 user params).
    [ComposeBridge(
        Class     = "androidx/compose/material3/IconButtonKt",
        JvmName   = "OutlinedIconButton",
        Signature = "(Lkotlin/jvm/functions/Function0;Landroidx/compose/ui/Modifier;Z" +
                    "Landroidx/compose/ui/graphics/Shape;" +
                    "Landroidx/compose/material3/IconButtonColors;" +
                    "Landroidx/compose/foundation/BorderStroke;" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Lkotlin/jvm/functions/Function2;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(OutlinedIconButtonDefault))]
    [ComposeFacade]
    public static partial void OutlinedIconButton(IFunction0 onClick, IModifier? modifier,
                                                  Shape? shape,
                                                  IFunction2 content, IComposer composer);

    // androidx.compose.material3.IconButtonKt.IconToggleButton — toggle
    // shape: leading boolean checked + Function1 onCheckedChange instead of
    // a Function0 onClick (7 user params).
    [ComposeBridge(
        Class     = "androidx/compose/material3/IconButtonKt",
        JvmName   = "IconToggleButton",
        Signature = "(ZLkotlin/jvm/functions/Function1;Landroidx/compose/ui/Modifier;Z" +
                    "Landroidx/compose/material3/IconToggleButtonColors;" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Lkotlin/jvm/functions/Function2;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(IconToggleButtonDefault))]
    [ComposeFacade]
    public static partial void IconToggleButton(bool @checked, [Callback(typeof(bool))] IFunction1 onCheckedChange,
                                                IModifier? modifier, IFunction2 content,
                                                IComposer composer);

    // androidx.compose.material3.IconButtonKt.FilledIconToggleButton — adds
    // Shape between enabled and colors compared to plain IconToggleButton
    // (8 user params).
    [ComposeBridge(
        Class     = "androidx/compose/material3/IconButtonKt",
        JvmName   = "FilledIconToggleButton",
        Signature = "(ZLkotlin/jvm/functions/Function1;Landroidx/compose/ui/Modifier;Z" +
                    "Landroidx/compose/ui/graphics/Shape;" +
                    "Landroidx/compose/material3/IconToggleButtonColors;" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Lkotlin/jvm/functions/Function2;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(FilledIconToggleButtonDefault))]
    [ComposeFacade]
    public static partial void FilledIconToggleButton(bool @checked, [Callback(typeof(bool))] IFunction1 onCheckedChange,
                                                      IModifier? modifier, Shape? shape, IFunction2 content,
                                                      IComposer composer);

    // androidx.compose.material3.IconButtonKt.FilledTonalIconToggleButton —
    // same signature as FilledIconToggleButton.
    [ComposeBridge(
        Class     = "androidx/compose/material3/IconButtonKt",
        JvmName   = "FilledTonalIconToggleButton",
        Signature = "(ZLkotlin/jvm/functions/Function1;Landroidx/compose/ui/Modifier;Z" +
                    "Landroidx/compose/ui/graphics/Shape;" +
                    "Landroidx/compose/material3/IconToggleButtonColors;" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Lkotlin/jvm/functions/Function2;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(FilledIconToggleButtonDefault))]
    [ComposeFacade]
    public static partial void FilledTonalIconToggleButton(bool @checked, [Callback(typeof(bool))] IFunction1 onCheckedChange,
                                                           IModifier? modifier, Shape? shape, IFunction2 content,
                                                           IComposer composer);

    // androidx.compose.material3.IconButtonKt.OutlinedIconToggleButton —
    // adds BorderStroke between colors and interactionSource (9 user params).
    [ComposeBridge(
        Class     = "androidx/compose/material3/IconButtonKt",
        JvmName   = "OutlinedIconToggleButton",
        Signature = "(ZLkotlin/jvm/functions/Function1;Landroidx/compose/ui/Modifier;Z" +
                    "Landroidx/compose/ui/graphics/Shape;" +
                    "Landroidx/compose/material3/IconToggleButtonColors;" +
                    "Landroidx/compose/foundation/BorderStroke;" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Lkotlin/jvm/functions/Function2;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(OutlinedIconToggleButtonDefault))]
    [ComposeFacade]
    public static partial void OutlinedIconToggleButton(bool @checked, [Callback(typeof(bool))] IFunction1 onCheckedChange,
                                                        IModifier? modifier, Shape? shape, IFunction2 content,
                                                        IComposer composer);

    // androidx.compose.material3.FloatingActionButtonKt.FloatingActionButton-X-z6DiA
    [ComposeBridge(
        Class     = "androidx/compose/material3/FloatingActionButtonKt",
        JvmName   = "FloatingActionButton-X-z6DiA",
        Signature = "(Lkotlin/jvm/functions/Function0;Landroidx/compose/ui/Modifier;" +
                    "Landroidx/compose/ui/graphics/Shape;JJ" +
                    "Landroidx/compose/material3/FloatingActionButtonElevation;" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Lkotlin/jvm/functions/Function2;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(FloatingActionButtonDefault))]
    [ComposeFacade]
    public static partial void FloatingActionButton(IFunction0 onClick, IModifier? modifier,
                                                    Shape? shape,
                                                    IFunction2 content, IComposer composer);

    // androidx.compose.material3.FloatingActionButtonKt.SmallFloatingActionButton-X-z6DiA
    [ComposeBridge(
        Class     = "androidx/compose/material3/FloatingActionButtonKt",
        JvmName   = "SmallFloatingActionButton-X-z6DiA",
        Signature = "(Lkotlin/jvm/functions/Function0;Landroidx/compose/ui/Modifier;" +
                    "Landroidx/compose/ui/graphics/Shape;JJ" +
                    "Landroidx/compose/material3/FloatingActionButtonElevation;" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Lkotlin/jvm/functions/Function2;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(SmallFloatingActionButtonDefault))]
    [ComposeFacade]
    public static partial void SmallFloatingActionButton(IFunction0 onClick, IModifier? modifier,
                                                         Shape? shape,
                                                         IFunction2 content, IComposer composer);

    // androidx.compose.material3.FloatingActionButtonKt.LargeFloatingActionButton-X-z6DiA
    [ComposeBridge(
        Class     = "androidx/compose/material3/FloatingActionButtonKt",
        JvmName   = "LargeFloatingActionButton-X-z6DiA",
        Signature = "(Lkotlin/jvm/functions/Function0;Landroidx/compose/ui/Modifier;" +
                    "Landroidx/compose/ui/graphics/Shape;JJ" +
                    "Landroidx/compose/material3/FloatingActionButtonElevation;" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Lkotlin/jvm/functions/Function2;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(LargeFloatingActionButtonDefault))]
    [ComposeFacade]
    public static partial void LargeFloatingActionButton(IFunction0 onClick, IModifier? modifier,
                                                         Shape? shape,
                                                         IFunction2 content, IComposer composer);

    // androidx.compose.material3.FloatingActionButtonKt.ExtendedFloatingActionButton-ElI5-7k
    // (icon + text + expanded multi-slot variant — the canonical animated extended FAB)
    [ComposeBridge(
        Class     = "androidx/compose/material3/FloatingActionButtonKt",
        JvmName   = "ExtendedFloatingActionButton-ElI5-7k",
        Signature = "(Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
                    "Lkotlin/jvm/functions/Function0;Landroidx/compose/ui/Modifier;Z" +
                    "Landroidx/compose/ui/graphics/Shape;JJ" +
                    "Landroidx/compose/material3/FloatingActionButtonElevation;" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(ExtendedFloatingActionButtonDefault))]
    [ComposeFacade]
    public static partial void ExtendedFloatingActionButton(
        IFunction2 text,
        IFunction2 icon,
        IFunction0 onClick,
        IModifier? modifier,
        bool       expanded,
        Shape?     shape,
        IComposer  composer);

    // androidx.compose.material3.SurfaceKt.Surface-T9BRK9s (non-interactive)
    [ComposeBridge(
        Class     = "androidx/compose/material3/SurfaceKt",
        JvmName   = "Surface-T9BRK9s",
        Signature = "(Landroidx/compose/ui/Modifier;Landroidx/compose/ui/graphics/Shape;JJFF" +
                    "Landroidx/compose/foundation/BorderStroke;" +
                    "Lkotlin/jvm/functions/Function2;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(SurfaceDefault))]
    [ComposeFacade]
    public static partial void Surface(IModifier? modifier, Shape? shape, IFunction2 content, IComposer composer);

    // androidx.compose.foundation.ImageKt.Image (Painter overload) — all
    // four `Image` Kotlin overloads share the JVM name `Image`, so the
    // binder strips them. The Painter type itself isn't bound either
    // (inline-class methods like getIntrinsicSize-NH-jbRc), so callers
    // pass a raw IntPtr Painter handle obtained from
    // ComposeBridges.PainterResource.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/ImageKt",
        JvmName   = "Image",
        Signature = "(Landroidx/compose/ui/graphics/painter/Painter;Ljava/lang/String;" +
                    "Landroidx/compose/ui/Modifier;Landroidx/compose/ui/Alignment;" +
                    "Landroidx/compose/ui/layout/ContentScale;F" +
                    "Landroidx/compose/ui/graphics/ColorFilter;" +
                    "Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(ImageDefault))]
    [ComposeFacade]
    public static partial void Image(
        [PainterResource] Painter painter,
        string?       contentDescription,
        IModifier?    modifier,
        Alignment?    alignment,
        ContentScale? contentScale,
        float?        alpha,
        int           defaults,
        IComposer     composer);

    // androidx.compose.material3.IconKt.Icon-ww6aTOc (Painter overload) —
    // the Painter and ImageBitmap overloads share the mangled JVM name
    // `Icon-ww6aTOc` with the bound ImageVector overload and are
    // stripped. Painter handles come from ComposeBridges.PainterResource.
    [ComposeBridge(
        Class     = "androidx/compose/material3/IconKt",
        JvmName   = "Icon-ww6aTOc",
        Signature = "(Landroidx/compose/ui/graphics/painter/Painter;Ljava/lang/String;" +
                    "Landroidx/compose/ui/Modifier;JLandroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(IconPainterDefault))]
    [ComposeFacade(
        ClassName         = "Icon",
        SecondaryCtor     = nameof(IconImageVector),
        SecondaryDefaults = typeof(IconDefault))]
    public static partial void IconPainter(
        [PainterResource] Painter painter,
        string?    contentDescription,
        IModifier? modifier,
        Color?     tint,
        int        defaults,
        IComposer  composer);

    // Phase 11 secondary — thin wrapper over the bound
    // androidx.compose.material3.IconKt.Icon(ImageVector, ...)
    // overload. Mirrors IconPainter's user-param shape but swaps the
    // discriminator (Painter → ImageVector) so the generated Icon
    // facade can dispatch by ctor. No [ComposeBridge] needed — the
    // ImageVector overload is fully bound; the facade reaches it via
    // SecondaryDefaults pointing at the IconDefault enum.
    //
    // `tint` is `Color?` (a registered Compose value type, lowered to
    // the JNI `J` slot via the implicit `Color -> long` conversion) so
    // the facade generator classifies it as OptionalValue and only
    // clears the `$default` bit when the caller assigns a non-null
    // Tint — otherwise Kotlin falls back to `LocalContentColor.current`.
    // A non-nullable `long` (or `Color`) would unconditionally clear
    // the bit and pass `0L` (transparent black) to Kotlin, breaking
    // theme-inherited icon tinting.
    public static void IconImageVector(
        global::AndroidX.Compose.UI.Graphics.Vector.ImageVector imageVector,
        string?    contentDescription,
        IModifier? modifier,
        Color?     tint,
        int        defaults,
        IComposer  composer) =>
        IconKt.Icon(
            imageVector:        imageVector,
            contentDescription: contentDescription!,
            modifier:           modifier,
            tint:               tint.GetValueOrDefault(),
            _composer:          composer,
            p5:                 0,
            _changed:           defaults);

    // androidx.compose.material3.TextFieldKt.TextField (String overload)
    const string TextFieldStringSig =
        "(Ljava/lang/String;Lkotlin/jvm/functions/Function1;Landroidx/compose/ui/Modifier;ZZ" +
        "Landroidx/compose/ui/text/TextStyle;" +
        "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
        "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
        "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
        "Lkotlin/jvm/functions/Function2;Z" +
        "Landroidx/compose/ui/text/input/VisualTransformation;" +
        "Landroidx/compose/foundation/text/KeyboardOptions;" +
        "Landroidx/compose/foundation/text/KeyboardActions;ZII" +
        "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
        "Landroidx/compose/ui/graphics/Shape;" +
        "Landroidx/compose/material3/TextFieldColors;" +
        "Landroidx/compose/runtime/Composer;IIII)V";

    [ComposeBridge(
        Class     = "androidx/compose/material3/TextFieldKt",
        JvmName   = "TextField",
        Signature = TextFieldStringSig,
        Defaults  = typeof(TextFieldDefault))]
    public static partial void TextField(
        string value,
        [Callback(typeof(string))] IFunction1 onValueChange,
        IModifier? modifier,
        bool? enabled,
        bool? readOnly,
        AndroidX.Compose.UI.Text.TextStyle? textStyle,
        IFunction2? label,
        IFunction2? placeholder,
        IFunction2? leadingIcon,
        IFunction2? trailingIcon,
        IFunction2? prefix,
        IFunction2? suffix,
        IFunction2? supportingText,
        bool? isError,
        AndroidX.Compose.UI.Text.Input.IVisualTransformation? visualTransformation,
        AndroidX.Compose.Foundation.Text.KeyboardOptions? keyboardOptions,
        bool? singleLine,
        int? maxLines,
        int? minLines,
        Shape? shape,
        IComposer composer);

    [ComposeBridge(
        Class     = "androidx/compose/material3/OutlinedTextFieldKt",
        JvmName   = "OutlinedTextField",
        Signature = TextFieldStringSig,
        Defaults  = typeof(TextFieldDefault))]
    public static partial void OutlinedTextField(
        string value,
        [Callback(typeof(string))] IFunction1 onValueChange,
        IModifier? modifier,
        bool? enabled,
        bool? readOnly,
        AndroidX.Compose.UI.Text.TextStyle? textStyle,
        IFunction2? label,
        IFunction2? placeholder,
        IFunction2? leadingIcon,
        IFunction2? trailingIcon,
        IFunction2? prefix,
        IFunction2? suffix,
        IFunction2? supportingText,
        bool? isError,
        AndroidX.Compose.UI.Text.Input.IVisualTransformation? visualTransformation,
        AndroidX.Compose.Foundation.Text.KeyboardOptions? keyboardOptions,
        AndroidX.Compose.Foundation.Text.KeyboardActions? keyboardActions,
        bool? singleLine,
        int? maxLines,
        int? minLines,
        Shape? shape,
        IComposer composer);

    // androidx.compose.material3.TextFieldKt.TextField (TextFieldValue
    // overload) and OutlinedTextFieldKt.OutlinedTextField (TextFieldValue
    // overload). Same 23 user-param shape as the String overload — bit
    // layout in TextFieldDefault matches one-for-one — except slot 0 is
    // androidx/compose/ui/text/input/TextFieldValue instead of java/lang/String.
    // Used by the TextField(MutableState<TextFieldValue>) ctor to drive
    // caret placement after programmatic edits (see issue #204).
    const string TextFieldValueSig =
        "(Landroidx/compose/ui/text/input/TextFieldValue;Lkotlin/jvm/functions/Function1;Landroidx/compose/ui/Modifier;ZZ" +
        "Landroidx/compose/ui/text/TextStyle;" +
        "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
        "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
        "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
        "Lkotlin/jvm/functions/Function2;Z" +
        "Landroidx/compose/ui/text/input/VisualTransformation;" +
        "Landroidx/compose/foundation/text/KeyboardOptions;" +
        "Landroidx/compose/foundation/text/KeyboardActions;ZII" +
        "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
        "Landroidx/compose/ui/graphics/Shape;" +
        "Landroidx/compose/material3/TextFieldColors;" +
        "Landroidx/compose/runtime/Composer;IIII)V";

    [ComposeBridge(
        Class     = "androidx/compose/material3/TextFieldKt",
        JvmName   = "TextField",
        Signature = TextFieldValueSig,
        Defaults  = typeof(TextFieldDefault))]
    public static partial void TextFieldWithValue(
        AndroidX.Compose.UI.Text.Input.TextFieldValue value,
        IFunction1 onValueChange,
        IModifier? modifier,
        bool? enabled,
        bool? readOnly,
        AndroidX.Compose.UI.Text.TextStyle? textStyle,
        IFunction2? label,
        IFunction2? placeholder,
        IFunction2? leadingIcon,
        IFunction2? trailingIcon,
        IFunction2? prefix,
        IFunction2? suffix,
        IFunction2? supportingText,
        bool? isError,
        AndroidX.Compose.UI.Text.Input.IVisualTransformation? visualTransformation,
        AndroidX.Compose.Foundation.Text.KeyboardOptions? keyboardOptions,
        bool? singleLine,
        int? maxLines,
        int? minLines,
        Shape? shape,
        IComposer composer);

    [ComposeBridge(
        Class     = "androidx/compose/material3/OutlinedTextFieldKt",
        JvmName   = "OutlinedTextField",
        Signature = TextFieldValueSig,
        Defaults  = typeof(TextFieldDefault))]
    public static partial void OutlinedTextFieldWithValue(
        AndroidX.Compose.UI.Text.Input.TextFieldValue value,
        IFunction1 onValueChange,
        IModifier? modifier,
        bool? enabled,
        bool? readOnly,
        AndroidX.Compose.UI.Text.TextStyle? textStyle,
        IFunction2? label,
        IFunction2? placeholder,
        IFunction2? leadingIcon,
        IFunction2? trailingIcon,
        IFunction2? prefix,
        IFunction2? suffix,
        IFunction2? supportingText,
        bool? isError,
        AndroidX.Compose.UI.Text.Input.IVisualTransformation? visualTransformation,
        AndroidX.Compose.Foundation.Text.KeyboardOptions? keyboardOptions,
        AndroidX.Compose.Foundation.Text.KeyboardActions? keyboardActions,
        bool? singleLine,
        int? maxLines,
        int? minLines,
        Shape? shape,
        IComposer composer);

    // androidx.compose.material3.SecureTextFieldKt.{SecureTextField,OutlinedSecureTextField}-XvU6IwQ.
    // Both overloads have identical 23-user-param signatures: the
    // TextFieldState peer, a Modifier, 19 object slots (TextStyle,
    // TextFieldLabelPosition, six Function2/3 lambdas, InputTransformation,
    // KeyboardOptions, KeyboardActionHandler, an onTextLayout Function2,
    // Shape, TextFieldColors, PaddingValues, MutableInteractionSource),
    // 2 booleans (enabled, isError), 1 int + 1 char for the inline-class
    // textObfuscationMode/textObfuscationCharacter pair, then the trailing
    // Composer + IIII (3 $changed groups + 1 $default int). Hashed JVM
    // names come from the inline-class compiler mangling on
    // textObfuscationMode (TextObfuscationMode is @JvmInline value class
    // wrapping Int).
    const string SecureTextFieldSig =
        "(Landroidx/compose/foundation/text/input/TextFieldState;" +
        "Landroidx/compose/ui/Modifier;Z" +
        "Landroidx/compose/ui/text/TextStyle;" +
        "Landroidx/compose/material3/TextFieldLabelPosition;" +
        "Lkotlin/jvm/functions/Function3;" +
        "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
        "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
        "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;Z" +
        "Landroidx/compose/foundation/text/input/InputTransformation;IC" +
        "Landroidx/compose/foundation/text/KeyboardOptions;" +
        "Landroidx/compose/foundation/text/input/KeyboardActionHandler;" +
        "Lkotlin/jvm/functions/Function2;" +
        "Landroidx/compose/ui/graphics/Shape;" +
        "Landroidx/compose/material3/TextFieldColors;" +
        "Landroidx/compose/foundation/layout/PaddingValues;" +
        "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
        "Landroidx/compose/runtime/Composer;IIII)V";

    [ComposeBridge(
        Class     = "androidx/compose/material3/SecureTextFieldKt",
        JvmName   = "SecureTextField-XvU6IwQ",
        Signature = SecureTextFieldSig,
        Defaults  = typeof(SecureTextFieldDefault))]
    public static partial void SecureTextField(
        IntPtr      state,
        IModifier?  modifier,
        bool        enabled,
        IFunction3? label,
        IFunction2? placeholder,
        IFunction2? leadingIcon,
        IFunction2? trailingIcon,
        IFunction2? supportingText,
        bool        isError,
        Shape?      shape,
        IComposer   composer);

    [ComposeBridge(
        Class     = "androidx/compose/material3/SecureTextFieldKt",
        JvmName   = "OutlinedSecureTextField-XvU6IwQ",
        Signature = SecureTextFieldSig,
        Defaults  = typeof(OutlinedSecureTextFieldDefault))]
    public static partial void OutlinedSecureTextField(
        IntPtr      state,
        IModifier?  modifier,
        bool        enabled,
        IFunction3? label,
        IFunction2? placeholder,
        IFunction2? leadingIcon,
        IFunction2? trailingIcon,
        IFunction2? supportingText,
        bool        isError,
        Shape?      shape,
        IComposer   composer);

    // androidx.compose.material3.AndroidAlertDialog_androidKt.AlertDialog-Oix01E0
    [ComposeBridge(
        Class     = "androidx/compose/material3/AndroidAlertDialog_androidKt",
        JvmName   = "AlertDialog-Oix01E0",
        Signature = "(Lkotlin/jvm/functions/Function0;Lkotlin/jvm/functions/Function2;" +
                    "Landroidx/compose/ui/Modifier;" +
                    "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
                    "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
                    "Landroidx/compose/ui/graphics/Shape;JJJJF" +
                    "Landroidx/compose/ui/window/DialogProperties;" +
                    "Landroidx/compose/runtime/Composer;III)V",
        Defaults  = typeof(AlertDialogDefault))]
    [ComposeFacade]
    public static partial void AlertDialog(
        IFunction0  onDismissRequest,
        IFunction2  confirmButton,
        IModifier?  modifier,
        IFunction2? dismissButton,
        IFunction2? icon,
        IFunction2? title,
        IFunction2? text,
        Shape?      shape,
        int         defaults,
        IComposer   composer);

    // androidx.compose.material3.ModalBottomSheet_androidKt.ModalBottomSheet-dYc4hso
    [ComposeBridge(
        Class     = "androidx/compose/material3/ModalBottomSheet_androidKt",
        JvmName   = "ModalBottomSheet-dYc4hso",
        Signature = "(Lkotlin/jvm/functions/Function0;Landroidx/compose/ui/Modifier;" +
                    "Landroidx/compose/material3/SheetState;FLandroidx/compose/ui/graphics/Shape;JJFJ" +
                    "Lkotlin/jvm/functions/Function2;Landroidx/compose/foundation/layout/WindowInsets;" +
                    "Landroidx/compose/material3/ModalBottomSheetProperties;" +
                    "Lkotlin/jvm/functions/Function3;Landroidx/compose/runtime/Composer;III)V",
        Defaults  = typeof(ModalBottomSheetDefault))]
    [ComposeFacade(Scope = "Column")]
    public static partial void ModalBottomSheet(
        IFunction0  onDismissRequest,
        IModifier?  modifier,
        [StateHolder(Remember = nameof(RememberSheetState),
                     StateType = typeof(SheetStateHolder),
                     SharedState = true)]
        IntPtr      sheetState,
        IFunction2? dragHandle,
        Shape?      shape,
        IFunction3  content,
        int         defaults,
        IComposer   composer);

    // androidx.compose.material3.BottomSheetScaffoldKt.BottomSheetScaffold-sdMYb0k
    [ComposeBridge(
        Class     = "androidx/compose/material3/BottomSheetScaffoldKt",
        JvmName   = "BottomSheetScaffold-sdMYb0k",
        Signature = "(Lkotlin/jvm/functions/Function3;Landroidx/compose/ui/Modifier;" +
                    "Landroidx/compose/material3/BottomSheetScaffoldState;FF" +
                    "Landroidx/compose/ui/graphics/Shape;JJFF" +
                    "Lkotlin/jvm/functions/Function2;Z" +
                    "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function3;JJ" +
                    "Lkotlin/jvm/functions/Function3;Landroidx/compose/runtime/Composer;III)V",
        Defaults  = typeof(BottomSheetScaffoldDefault))]
    public static partial void BottomSheetScaffold(
        IFunction3  sheetContent,
        IModifier?  modifier,
        IntPtr      scaffoldState,
        IFunction2? sheetDragHandle,
        IFunction2? topBar,
        IFunction3? snackbarHost,
        IFunction3  content,
        int         defaults,
        IComposer   composer);

    // androidx.compose.material3.ScaffoldKt.Scaffold-TvnljyQ
    [ComposeBridge(
        Class     = "androidx/compose/material3/ScaffoldKt",
        JvmName   = "Scaffold-TvnljyQ",
        Signature = "(Landroidx/compose/ui/Modifier;" +
                    "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
                    "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
                    "IJJ" +
                    "Landroidx/compose/foundation/layout/WindowInsets;" +
                    "Lkotlin/jvm/functions/Function3;" +
                    "Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(ScaffoldDefault))]
    public static partial void Scaffold(
        IModifier?  modifier,
        IFunction2? topBar,
        IFunction2? bottomBar,
        IFunction2? snackbarHost,
        IFunction2? floatingActionButton,
        IFunction3  content,
        int         defaults,
        IComposer   composer);

    // androidx.compose.material3.DatePickerDialog_androidKt.DatePickerDialog-GmEhDVc
    [ComposeBridge(
        Class     = "androidx/compose/material3/DatePickerDialog_androidKt",
        JvmName   = "DatePickerDialog-GmEhDVc",
        Signature = "(Lkotlin/jvm/functions/Function0;Lkotlin/jvm/functions/Function2;" +
                    "Landroidx/compose/ui/Modifier;Lkotlin/jvm/functions/Function2;" +
                    "Landroidx/compose/ui/graphics/Shape;F" +
                    "Landroidx/compose/material3/DatePickerColors;" +
                    "Landroidx/compose/ui/window/DialogProperties;" +
                    "Lkotlin/jvm/functions/Function3;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(DatePickerDialogDefault))]
    [ComposeFacade]
    public static partial void DatePickerDialog(
        IFunction0  onDismissRequest,
        IFunction2  confirmButton,
        IModifier?  modifier,
        IFunction2? dismissButton,
        [Slot("Body")]
        IFunction3  content,
        int         defaults,
        IComposer   composer);

    // DateRangePickerDialog is just DatePickerDialog with a DateRangePicker
    // body — same Kotlin composable, same $default bitmask. Wrapper-passthrough
    // gives us a distinct C# facade name (and matching XML doc focus) without
    // a second JNI bridge.
    [ComposeFacade(Defaults = typeof(DatePickerDialogDefault))]
    public static partial void DateRangePickerDialog(
        IFunction0  onDismissRequest,
        IFunction2  confirmButton,
        IModifier?  modifier,
        IFunction2? dismissButton,
        [Slot("Body")]
        IFunction3  content,
        int         defaults,
        IComposer   composer);

    public static partial void DateRangePickerDialog(
        IFunction0  onDismissRequest,
        IFunction2  confirmButton,
        IModifier?  modifier,
        IFunction2? dismissButton,
        IFunction3  content,
        int         defaults,
        IComposer   composer)
        => DatePickerDialog(onDismissRequest, confirmButton, modifier, dismissButton, content, defaults, composer);

    // androidx.compose.material3.TimePickerKt.TimePicker-mT9BvqQ
    [ComposeBridge(
        Class     = "androidx/compose/material3/TimePickerKt",
        JvmName   = "TimePicker-mT9BvqQ",
        Signature = "(Landroidx/compose/material3/TimePickerState;Landroidx/compose/ui/Modifier;" +
                    "Landroidx/compose/material3/TimePickerColors;ILandroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(TimePickerDefault))]
    [ComposeFacade]
    public static partial void TimePicker(
        [StateHolder(Remember = nameof(RememberTimePickerState),
                     StateType = typeof(TimePickerState),
                     SharedState = true)] IntPtr state,
        IModifier? modifier,
        int defaults, IComposer composer);

    // androidx.compose.material3.TimePickerKt.TimeInput
    [ComposeBridge(
        Class     = "androidx/compose/material3/TimePickerKt",
        JvmName   = "TimeInput",
        Signature = "(Landroidx/compose/material3/TimePickerState;Landroidx/compose/ui/Modifier;" +
                    "Landroidx/compose/material3/TimePickerColors;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(TimeInputDefault))]
    [ComposeFacade]
    public static partial void TimeInput(
        [StateHolder(Remember = nameof(RememberTimePickerState),
                     StateType = typeof(TimePickerState),
                     SharedState = true)] IntPtr state,
        IModifier? modifier,
        int defaults, IComposer composer);

    // androidx.compose.material3.TimePickerDialogKt.TimePickerDialog-FItCLgY
    [ComposeBridge(
        Class     = "androidx/compose/material3/TimePickerDialogKt",
        JvmName   = "TimePickerDialog-FItCLgY",
        Signature = "(Lkotlin/jvm/functions/Function0;Lkotlin/jvm/functions/Function2;" +
                    "Lkotlin/jvm/functions/Function2;Landroidx/compose/ui/Modifier;" +
                    "Landroidx/compose/ui/window/DialogProperties;" +
                    "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
                    "Landroidx/compose/ui/graphics/Shape;J" +
                    "Lkotlin/jvm/functions/Function3;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(TimePickerDialogDefault))]
    [ComposeFacade]
    public static partial void TimePickerDialog(
        IFunction0  onDismissRequest,
        IFunction2  confirmButton,
        IFunction2  dismissButton,
        IModifier?  modifier,
        IFunction2? title,
        IFunction2? modeToggleButton,
        [Slot("Body")]
        IFunction3  content,
        int         defaults,
        IComposer   composer);

    // androidx.compose.material3.TooltipKt.TooltipBox (7-user-param overload)
    [ComposeBridge(
        Class     = "androidx/compose/material3/TooltipKt",
        JvmName   = "TooltipBox",
        Signature = "(Landroidx/compose/ui/window/PopupPositionProvider;" +
                    "Lkotlin/jvm/functions/Function3;" +
                    "Landroidx/compose/material3/TooltipState;" +
                    "Landroidx/compose/ui/Modifier;ZZ" +
                    "Lkotlin/jvm/functions/Function2;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(TooltipBoxDefault))]
    public static partial void TooltipBox(
        IntPtr     positionProvider,
        IFunction3 tooltip,
        IntPtr     state,
        IModifier? modifier,
        IFunction2 content,
        int        defaults,
        IComposer  composer);

    // androidx.compose.material3.DatePickerKt.DatePicker
    [ComposeBridge(
        Class     = "androidx/compose/material3/DatePickerKt",
        JvmName   = "DatePicker",
        Signature = "(Landroidx/compose/material3/DatePickerState;Landroidx/compose/ui/Modifier;" +
                    "Landroidx/compose/material3/DatePickerFormatter;" +
                    "Landroidx/compose/material3/DatePickerColors;" +
                    "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;Z" +
                    "Landroidx/compose/ui/focus/FocusRequester;" +
                    "Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(DatePickerDefault))]
    [ComposeFacade]
    public static partial void DatePicker(
        [StateHolder(Remember = nameof(RememberDatePickerState),
                     StateType = typeof(DatePickerState))]
        IntPtr      state,
        IModifier?  modifier,
        int         defaults,
        IComposer   composer);

    // androidx.compose.material3.DatePickerKt.rememberDatePickerState-EU0dCGE
    //
    // Phase 4b parameterised Remember (issue #264). Every Kotlin user
    // slot is surfaced — the bridge generator can't skip non-trailing
    // slots. The auto-default-mask clears bit N for every non-null
    // param the caller supplied; null leaves the Kotlin default in
    // place (the matching `RememberDatePickerStateDefault` bit stays
    // set).
    //
    // Wrapper-member resolution (StateType = DatePickerState):
    //   initialSelectedDateMillis  -> InitialSelectedDateMillis    (Java.Lang.Long?)
    //   initialDisplayedMonthMillis-> InitialDisplayedMonthMillis  (Java.Lang.Long?)
    //   yearRange                  -> InitialYearRange             (IntRange?)
    //   initialDisplayMode         -> InitialDisplayMode           (int?)
    //   selectableDates            -> InitialSelectableDates       (ISelectableDates?)
    //
    // The JNI slot for `Long`/`IntRange`/`SelectableDates` is `L…;`,
    // so the C# bridge param types must be reference (boxed Long,
    // IntRange, ISelectableDates). The `initialDisplayMode` slot is
    // `I` so `int?` is correct (auto-mask passes 0 + sets the bit
    // when caller leaves it null).
    [ComposeBridge(
        Class     = "androidx/compose/material3/DatePickerKt",
        JvmName   = "rememberDatePickerState-EU0dCGE",
        Signature = "(Ljava/lang/Long;Ljava/lang/Long;Lkotlin/ranges/IntRange;I" +
                    "Landroidx/compose/material3/SelectableDates;" +
                    "Landroidx/compose/runtime/Composer;II)Landroidx/compose/material3/DatePickerState;",
        Defaults  = typeof(RememberDatePickerStateDefault))]
    public static partial IntPtr RememberDatePickerState(
        Java.Lang.Long?                                  initialSelectedDateMillis,
        Java.Lang.Long?                                  initialDisplayedMonthMillis,
        IntRange?                                        yearRange,
        int?                                             initialDisplayMode,
        AndroidX.Compose.Material3.ISelectableDates?     selectableDates,
        IComposer                                        composer);

    // androidx.compose.material3.DateRangePickerKt.DateRangePicker
    [ComposeBridge(
        Class     = "androidx/compose/material3/DateRangePickerKt",
        JvmName   = "DateRangePicker",
        Signature = "(Landroidx/compose/material3/DateRangePickerState;Landroidx/compose/ui/Modifier;" +
                    "Landroidx/compose/material3/DatePickerFormatter;" +
                    "Landroidx/compose/material3/DatePickerColors;" +
                    "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;Z" +
                    "Landroidx/compose/ui/focus/FocusRequester;" +
                    "Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(DateRangePickerDefault))]
    [ComposeFacade]
    public static partial void DateRangePicker(
        [StateHolder(Remember = nameof(RememberDateRangePickerState),
                     StateType = typeof(DateRangePickerState))]
        IntPtr      state,
        IModifier?  modifier,
        int         defaults,
        IComposer   composer);

    // androidx.compose.material3.DateRangePickerKt.rememberDateRangePickerState-IlFM19s
    [ComposeBridge(
        Class     = "androidx/compose/material3/DateRangePickerKt",
        JvmName   = "rememberDateRangePickerState-IlFM19s",
        Signature = "(Ljava/lang/Long;Ljava/lang/Long;Ljava/lang/Long;Lkotlin/ranges/IntRange;I" +
                    "Landroidx/compose/material3/SelectableDates;" +
                    "Landroidx/compose/runtime/Composer;II)Landroidx/compose/material3/DateRangePickerState;",
        Defaults  = typeof(RememberDateRangePickerStateDefault))]
    public static partial IntPtr RememberDateRangePickerState(IComposer composer);

    // androidx.compose.material3.TimePickerKt.rememberTimePickerState
    [ComposeBridge(
        Class     = "androidx/compose/material3/TimePickerKt",
        JvmName   = "rememberTimePickerState",
        Signature = "(IIZLandroidx/compose/runtime/Composer;II)Landroidx/compose/material3/TimePickerState;",
        Defaults  = typeof(RememberTimePickerStateDefault))]
    public static partial IntPtr RememberTimePickerState(int initialHour, int initialMinute,
                                                         bool is24Hour, IComposer composer);

    // androidx.compose.material3.NavigationDrawerKt.rememberDrawerState.
    // The Kotlin function is bound natively so we go through it instead
    // of duplicating the JNI plumbing here. The [ComposeFacade]-generated
    // facade hands us the per-instance JCW veto adapter as
    // confirmStateChange (stable JNI identity = stable `remember` key);
    // initialValue comes from the wrapper's InitialValue property. The
    // [ConfirmStateChange] attribute is read by the facade generator off
    // this declaration when resolving the Remember parameter to a
    // per-instance JCW field instead of a state-wrapper member.
    public static IntPtr RememberDrawerState(
        DrawerValue initialValue,
        [ConfirmStateChange(typeof(DrawerValue),
            AdapterType = typeof(DrawerValueConfirmStateChange))]
        IFunction1 confirmStateChange,
        IComposer composer)
    {
        var state = AndroidX.Compose.Material3.NavigationDrawerKt.RememberDrawerState(
            initialValue:        initialValue,
            confirmStateChange:  confirmStateChange,
            _composer:           composer,
            p3:                  0,
            _changed:            0);
        return ((Java.Lang.Object)state).Handle;
    }

    // androidx.compose.material3.WideNavigationRailStateKt.rememberWideNavigationRailState.
    // No confirmStateChange callback; the rail's state is driven entirely
    // from C# via the wrapper's InitialValue property. Kotlin function is
    // bound natively — wrapper-passthrough only.
    public static IntPtr RememberWideNavigationRailState(
        WideNavigationRailValue initialValue,
        IComposer composer)
    {
        var state = AndroidX.Compose.Material3.WideNavigationRailStateKt.RememberWideNavigationRailState(
            initialValue: initialValue,
            _composer:    composer,
            p2:           0,
            _changed:     0);
        return ((Java.Lang.Object)state).Handle;
    }

    // androidx.compose.material3.ModalBottomSheetKt.rememberModalBottomSheetState.
    // Bound binding — wrapper-passthrough only. SkipPartiallyExpanded
    // comes from the SheetStateHolder wrapper's like-named property
    // (Phase 4b parameterised Remember). The veto adapter is wired by
    // the facade generator: it reads [ConfirmStateChange] off this
    // declaration and allocates a per-instance JCW field whose JNI
    // identity stays stable across recompositions (so `remember` cache
    // key holds and the sheet keeps its position).
    public static IntPtr RememberSheetState(
        bool skipPartiallyExpanded,
        [ConfirmStateChange(typeof(global::AndroidX.Compose.Material3.SheetValue),
            AdapterType = typeof(SheetValueConfirmStateChange),
            PropertyName = "ConfirmValueChange")]
        IFunction1 confirmValueChange,
        IComposer composer)
    {
        var state = AndroidX.Compose.Material3.ModalBottomSheetKt.RememberModalBottomSheetState(
            skipPartiallyExpanded: skipPartiallyExpanded,
            confirmValueChange:    confirmValueChange,
            _composer:             composer,
            p3:                    0,
            _changed:              0);
        return ((Java.Lang.Object)state).Handle;
    }

    // androidx.compose.material3.TooltipKt.rememberTooltipState
    [ComposeBridge(
        Class     = "androidx/compose/material3/TooltipKt",
        JvmName   = "rememberTooltipState",
        Signature = "(ZZLandroidx/compose/foundation/MutatorMutex;Landroidx/compose/runtime/Composer;II)Landroidx/compose/material3/TooltipState;",
        Defaults  = typeof(RememberTooltipStateDefault))]
    public static partial IntPtr RememberTooltipState(bool isPersistent, IComposer composer);

    // androidx.compose.material3.TooltipDefaults.INSTANCE.rememberPlainTooltipPositionProvider-kHDZbjc
    // Instance method on a Kotlin object singleton.
    [ComposeBridge(
        Class         = "androidx/compose/material3/TooltipDefaults",
        JvmName       = "rememberPlainTooltipPositionProvider-kHDZbjc",
        Signature     = "(FLandroidx/compose/runtime/Composer;II)Landroidx/compose/ui/window/PopupPositionProvider;",
        Defaults      = typeof(RememberPlainTooltipPositionProviderDefault),
        InstanceField = "INSTANCE")]
    public static partial IntPtr RememberPlainTooltipPositionProvider(IComposer composer);

    // androidx.compose.material3.pulltorefresh.PullToRefreshKt.PullToRefreshBox
    [ComposeBridge(
        Class     = "androidx/compose/material3/pulltorefresh/PullToRefreshKt",
        JvmName   = "PullToRefreshBox",
        Signature = "(ZLkotlin/jvm/functions/Function0;Landroidx/compose/ui/Modifier;" +
                    "Landroidx/compose/material3/pulltorefresh/PullToRefreshState;" +
                    "Landroidx/compose/ui/Alignment;Lkotlin/jvm/functions/Function3;" +
                    "Lkotlin/jvm/functions/Function3;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(PullToRefreshBoxDefault))]
    [ComposeFacade]
    public static partial void PullToRefreshBox(
        bool        isRefreshing,
        IFunction0  onRefresh,
        [StateHolder(Remember  = nameof(RememberPullToRefreshState),
                     StateType = typeof(PullToRefreshState))]
        IntPtr      state,
        IModifier?  modifier,
        IFunction3  content,
        int         defaults,
        IComposer   composer);

    // androidx.compose.material3.pulltorefresh.PullToRefreshKt.rememberPullToRefreshState
    // No-arg @Composable; signature has only the trailing _changed I slot
    // (no $default), so omit Defaults entirely per the bridge generator
    // contract (see CN2005).
    [ComposeBridge(
        Class     = "androidx/compose/material3/pulltorefresh/PullToRefreshKt",
        JvmName   = "rememberPullToRefreshState",
        Signature = "(Landroidx/compose/runtime/Composer;I)" +
                    "Landroidx/compose/material3/pulltorefresh/PullToRefreshState;")]
    public static partial IntPtr RememberPullToRefreshState(IComposer composer);

    // androidx.compose.material3.CardKt.Card (non-clickable)
    const string CardSig =
        "(Landroidx/compose/ui/Modifier;Landroidx/compose/ui/graphics/Shape;" +
        "Landroidx/compose/material3/CardColors;Landroidx/compose/material3/CardElevation;" +
        "Landroidx/compose/foundation/BorderStroke;" +
        "Lkotlin/jvm/functions/Function3;Landroidx/compose/runtime/Composer;II)V";

    [ComposeBridge(
        Class     = "androidx/compose/material3/CardKt",
        JvmName   = "Card",
        Signature = CardSig,
        Defaults  = typeof(CardDefault))]
    [ComposeFacade]
    public static partial void Card(IModifier? modifier, Shape? shape, IFunction3 content, IComposer composer);

    // androidx.compose.material3.CardKt.OutlinedCard (same shape as Card)
    [ComposeBridge(
        Class     = "androidx/compose/material3/CardKt",
        JvmName   = "OutlinedCard",
        Signature = CardSig,
        Defaults  = typeof(CardDefault))]
    [ComposeFacade]
    public static partial void OutlinedCard(Shape? shape, IFunction3 content, IComposer composer);

    // androidx.compose.material3.CardKt.ElevatedCard (no border)
    [ComposeBridge(
        Class     = "androidx/compose/material3/CardKt",
        JvmName   = "ElevatedCard",
        Signature = "(Landroidx/compose/ui/Modifier;Landroidx/compose/ui/graphics/Shape;" +
                    "Landroidx/compose/material3/CardColors;Landroidx/compose/material3/CardElevation;" +
                    "Lkotlin/jvm/functions/Function3;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(ElevatedCardDefault))]
    [ComposeFacade]
    public static partial void ElevatedCard(Shape? shape, IFunction3 content, IComposer composer);

    const string AssistChipSig =
        "(Lkotlin/jvm/functions/Function0;Lkotlin/jvm/functions/Function2;" +
        "Landroidx/compose/ui/Modifier;Z" +
        "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
        "Landroidx/compose/ui/graphics/Shape;" +
        "Landroidx/compose/material3/ChipColors;Landroidx/compose/material3/ChipElevation;" +
        "Landroidx/compose/foundation/BorderStroke;" +
        "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
        "Landroidx/compose/runtime/Composer;III)V";

    [ComposeBridge(
        Class     = "androidx/compose/material3/ChipKt",
        JvmName   = "AssistChip",
        Signature = AssistChipSig,
        Defaults  = typeof(AssistChipDefault))]
    [ComposeFacade]
    public static partial void AssistChip(
        IFunction0  onClick,
        IFunction2  label,
        IModifier?  modifier,
        IFunction2? leadingIcon,
        IFunction2? trailingIcon,
        Shape?      shape,
        int         defaults,
        IComposer   composer);

    [ComposeBridge(
        Class     = "androidx/compose/material3/ChipKt",
        JvmName   = "ElevatedAssistChip",
        Signature = AssistChipSig,
        Defaults  = typeof(AssistChipDefault))]
    [ComposeFacade]
    public static partial void ElevatedAssistChip(
        IFunction0  onClick,
        IFunction2  label,
        IFunction2? leadingIcon,
        IFunction2? trailingIcon,
        Shape?      shape,
        int         defaults,
        IComposer   composer);

    const string FilterChipSig =
        "(ZLkotlin/jvm/functions/Function0;Lkotlin/jvm/functions/Function2;" +
        "Landroidx/compose/ui/Modifier;Z" +
        "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
        "Landroidx/compose/ui/graphics/Shape;" +
        "Landroidx/compose/material3/SelectableChipColors;" +
        "Landroidx/compose/material3/SelectableChipElevation;" +
        "Landroidx/compose/foundation/BorderStroke;" +
        "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
        "Landroidx/compose/runtime/Composer;III)V";

    [ComposeBridge(
        Class     = "androidx/compose/material3/ChipKt",
        JvmName   = "FilterChip",
        Signature = FilterChipSig,
        Defaults  = typeof(FilterChipDefault))]
    [ComposeFacade]
    public static partial void FilterChip(
        bool        selected,
        IFunction0  onClick,
        IFunction2  label,
        IModifier?  modifier,
        IFunction2? leadingIcon,
        IFunction2? trailingIcon,
        Shape?      shape,
        int         defaults,
        IComposer   composer);

    [ComposeBridge(
        Class     = "androidx/compose/material3/ChipKt",
        JvmName   = "ElevatedFilterChip",
        Signature = FilterChipSig,
        Defaults  = typeof(FilterChipDefault))]
    [ComposeFacade]
    public static partial void ElevatedFilterChip(
        bool        selected,
        IFunction0  onClick,
        IFunction2  label,
        IFunction2? leadingIcon,
        IFunction2? trailingIcon,
        Shape?      shape,
        int         defaults,
        IComposer   composer);

    // androidx.compose.material3.ChipKt.InputChip
    [ComposeBridge(
        Class     = "androidx/compose/material3/ChipKt",
        JvmName   = "InputChip",
        Signature = "(ZLkotlin/jvm/functions/Function0;Lkotlin/jvm/functions/Function2;" +
                    "Landroidx/compose/ui/Modifier;Z" +
                    "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
                    "Lkotlin/jvm/functions/Function2;Landroidx/compose/ui/graphics/Shape;" +
                    "Landroidx/compose/material3/SelectableChipColors;" +
                    "Landroidx/compose/material3/SelectableChipElevation;" +
                    "Landroidx/compose/foundation/BorderStroke;" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Landroidx/compose/runtime/Composer;III)V",
        Defaults  = typeof(InputChipDefault))]
    [ComposeFacade]
    public static partial void InputChip(
        bool        selected,
        IFunction0  onClick,
        IFunction2  label,
        IModifier?  modifier,
        IFunction2? leadingIcon,
        IFunction2? avatar,
        IFunction2? trailingIcon,
        Shape?      shape,
        int         defaults,
        IComposer   composer);

    const string SuggestionChipSig =
        "(Lkotlin/jvm/functions/Function0;Lkotlin/jvm/functions/Function2;" +
        "Landroidx/compose/ui/Modifier;Z" +
        "Lkotlin/jvm/functions/Function2;Landroidx/compose/ui/graphics/Shape;" +
        "Landroidx/compose/material3/ChipColors;Landroidx/compose/material3/ChipElevation;" +
        "Landroidx/compose/foundation/BorderStroke;" +
        "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
        "Landroidx/compose/runtime/Composer;II)V";

    [ComposeBridge(
        Class     = "androidx/compose/material3/ChipKt",
        JvmName   = "SuggestionChip",
        Signature = SuggestionChipSig,
        Defaults  = typeof(SuggestionChipDefault))]
    [ComposeFacade]
    public static partial void SuggestionChip(
        IFunction0  onClick,
        IFunction2  label,
        IModifier?  modifier,
        IFunction2? icon,
        Shape?      shape,
        int         defaults,
        IComposer   composer);

    [ComposeBridge(
        Class     = "androidx/compose/material3/ChipKt",
        JvmName   = "ElevatedSuggestionChip",
        Signature = SuggestionChipSig,
        Defaults  = typeof(SuggestionChipDefault))]
    [ComposeFacade]
    public static partial void ElevatedSuggestionChip(
        IFunction0  onClick,
        IFunction2  label,
        IFunction2? icon,
        Shape?      shape,
        int         defaults,
        IComposer   composer);

    // androidx.compose.material3.NavigationBarKt.NavigationBar-HsRjFd4
    [ComposeBridge(
        Class     = "androidx/compose/material3/NavigationBarKt",
        JvmName   = "NavigationBar-HsRjFd4",
        Signature = "(Landroidx/compose/ui/Modifier;JJF" +
                    "Landroidx/compose/foundation/layout/WindowInsets;" +
                    "Lkotlin/jvm/functions/Function3;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(NavigationBarDefault))]
    [ComposeFacade(Scope = "Row")]
    public static partial void NavigationBar(IModifier? modifier, IFunction3 content, IComposer composer);

    // androidx.compose.material3.NavigationBarKt.NavigationBarItem (RowScope receiver)
    [ComposeBridge(
        Class     = "androidx/compose/material3/NavigationBarKt",
        JvmName   = "NavigationBarItem",
        Signature = "(Landroidx/compose/foundation/layout/RowScope;Z" +
                    "Lkotlin/jvm/functions/Function0;Lkotlin/jvm/functions/Function2;" +
                    "Landroidx/compose/ui/Modifier;Z" +
                    "Lkotlin/jvm/functions/Function2;Z" +
                    "Landroidx/compose/material3/NavigationBarItemColors;" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(NavigationBarItemDefault))]
    [ComposeFacade]
    public static partial void NavigationBarItem(
        IntPtr      rowScope,
        bool        selected,
        IFunction0  onClick,
        IFunction2  icon,
        IModifier?  modifier,
        IFunction2? label,
        bool        enabled         = true,
        bool        alwaysShowLabel = true,
        int         defaults        = 0,
        IComposer   composer        = null!);

    // androidx.compose.material3.NavigationRailKt.NavigationRail-qi6gXK8
    [ComposeBridge(
        Class     = "androidx/compose/material3/NavigationRailKt",
        JvmName   = "NavigationRail-qi6gXK8",
        Signature = "(Landroidx/compose/ui/Modifier;JJ" +
                    "Lkotlin/jvm/functions/Function3;" +
                    "Landroidx/compose/foundation/layout/WindowInsets;" +
                    "Lkotlin/jvm/functions/Function3;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(NavigationRailDefault))]
    [ComposeFacade]
    public static partial void NavigationRail(IModifier? modifier, IFunction3 content, IComposer composer);

    // androidx.compose.material3.NavigationRailKt.NavigationRailItem
    [ComposeBridge(
        Class     = "androidx/compose/material3/NavigationRailKt",
        JvmName   = "NavigationRailItem",
        Signature = "(ZLkotlin/jvm/functions/Function0;Lkotlin/jvm/functions/Function2;" +
                    "Landroidx/compose/ui/Modifier;Z" +
                    "Lkotlin/jvm/functions/Function2;Z" +
                    "Landroidx/compose/material3/NavigationRailItemColors;" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(NavigationRailItemDefault))]
    [ComposeFacade]
    public static partial void NavigationRailItem(
        bool        selected,
        IFunction0  onClick,
        IFunction2  icon,
        IModifier?  modifier,
        IFunction2? label,
        int         defaults,
        IComposer   composer);

    // androidx.compose.material3.NavigationDrawerKt.NavigationDrawerItem
    // (no JVM mangling — no @JvmInline value-class params). label is the
    // required content slot, icon and badge are optional. shape, colors,
    // and interactionSource are kept defaulted (their $default bits stay
    // set; Kotlin supplies its real defaults).
    [ComposeBridge(
        Class     = "androidx/compose/material3/NavigationDrawerKt",
        JvmName   = "NavigationDrawerItem",
        Signature = "(Lkotlin/jvm/functions/Function2;Z" +
                    "Lkotlin/jvm/functions/Function0;" +
                    "Landroidx/compose/ui/Modifier;" +
                    "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
                    "Landroidx/compose/ui/graphics/Shape;" +
                    "Landroidx/compose/material3/NavigationDrawerItemColors;" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(NavigationDrawerItemDefault))]
    [ComposeFacade]
    public static partial void NavigationDrawerItem(
        IFunction2  label,
        bool        selected,
        IFunction0  onClick,
        IModifier?  modifier,
        IFunction2? icon,
        IFunction2? badge,
        int         defaults,
        IComposer   composer);

    // 3 drawer-sheet variants share the same DrawerSheetDefault enum + signature.
    const string DrawerSheetSig =
        "(Landroidx/compose/ui/Modifier;Landroidx/compose/ui/graphics/Shape;JJF" +
        "Landroidx/compose/foundation/layout/WindowInsets;" +
        "Lkotlin/jvm/functions/Function3;Landroidx/compose/runtime/Composer;II)V";

    [ComposeBridge(
        Class     = "androidx/compose/material3/NavigationDrawerKt",
        JvmName   = "ModalDrawerSheet-afqeVBk",
        Signature = DrawerSheetSig,
        Defaults  = typeof(DrawerSheetDefault))]
    [ComposeFacade(DefaultColorFromTheme = "surfaceContainerLow")]
    public static partial void ModalDrawerSheet(IFunction3 content, Shape? shape, long drawerContainerColor, IComposer composer);

    [ComposeBridge(
        Class     = "androidx/compose/material3/NavigationDrawerKt",
        JvmName   = "DismissibleDrawerSheet-afqeVBk",
        Signature = DrawerSheetSig,
        Defaults  = typeof(DrawerSheetDefault))]
    [ComposeFacade(DefaultColorFromTheme = "surface")]
    public static partial void DismissibleDrawerSheet(IFunction3 content, Shape? shape, long drawerContainerColor, IComposer composer);

    [ComposeBridge(
        Class     = "androidx/compose/material3/NavigationDrawerKt",
        JvmName   = "PermanentDrawerSheet-afqeVBk",
        Signature = DrawerSheetSig,
        Defaults  = typeof(DrawerSheetDefault))]
    [ComposeFacade(DefaultColorFromTheme = "surface")]
    public static partial void PermanentDrawerSheet(IFunction3 content, Shape? shape, long drawerContainerColor, IComposer composer);

    // androidx.compose.material3.SegmentedButtonKt.SegmentedButton
    // (SingleChoiceSegmentedButtonRowScope receiver, longer 11-param overload
    // with PaddingValues). The Kt method has 4 same-named overloads, so the
    // binder strips them all.
    const string SingleChoiceSegmentedButtonSig =
        "(Landroidx/compose/material3/SingleChoiceSegmentedButtonRowScope;Z" +
        "Lkotlin/jvm/functions/Function0;Landroidx/compose/ui/graphics/Shape;" +
        "Landroidx/compose/ui/Modifier;Z" +
        "Landroidx/compose/material3/SegmentedButtonColors;" +
        "Landroidx/compose/foundation/BorderStroke;" +
        "Landroidx/compose/foundation/layout/PaddingValues;" +
        "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
        "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
        "Landroidx/compose/runtime/Composer;III)V";

    [ComposeBridge(
        Class     = "androidx/compose/material3/SegmentedButtonKt",
        JvmName   = "SegmentedButton",
        Signature = SingleChoiceSegmentedButtonSig,
        Defaults  = typeof(SingleChoiceSegmentedButtonDefault))]
    public static partial void SingleChoiceSegmentedButton(
        IntPtr      singleChoiceScope,
        bool        selected,
        IFunction0  onClick,
        IntPtr      shape,
        IFunction2  label,
        IModifier?  modifier,
        IFunction2? icon,
        int         defaults,
        IComposer   composer);

    // androidx.compose.material3.SegmentedButtonKt.SegmentedButton
    // (MultiChoiceSegmentedButtonRowScope receiver, longer 11-param overload).
    const string MultiChoiceSegmentedButtonSig =
        "(Landroidx/compose/material3/MultiChoiceSegmentedButtonRowScope;Z" +
        "Lkotlin/jvm/functions/Function1;Landroidx/compose/ui/graphics/Shape;" +
        "Landroidx/compose/ui/Modifier;Z" +
        "Landroidx/compose/material3/SegmentedButtonColors;" +
        "Landroidx/compose/foundation/BorderStroke;" +
        "Landroidx/compose/foundation/layout/PaddingValues;" +
        "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
        "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
        "Landroidx/compose/runtime/Composer;III)V";

    [ComposeBridge(
        Class     = "androidx/compose/material3/SegmentedButtonKt",
        JvmName   = "SegmentedButton",
        Signature = MultiChoiceSegmentedButtonSig,
        Defaults  = typeof(MultiChoiceSegmentedButtonDefault))]
    public static partial void MultiChoiceSegmentedButton(
        IntPtr      multiChoiceScope,
        bool        @checked,
        IFunction1  onCheckedChange,
        IntPtr      shape,
        IFunction2  label,
        IModifier?  modifier,
        IFunction2? icon,
        int         defaults,
        IComposer   composer);

    // androidx.compose.material3.SegmentedButtonDefaults.itemShape — INSTANCE
    // method on the SegmentedButtonDefaults Kotlin `object` singleton, returns
    // the rounded-corner Shape for a segment given its position in the row.
    // The Material3 binding strips the surrounding SegmentedButtonDefaults
    // helpers (probably because some sibling members use inline-class types),
    // so we go through the source generator's InstanceField shape — that walks
    // the static `INSTANCE` field once, caches a global ref, then calls the
    // method via CallObjectMethod. The auto-defaults logic on the generator
    // sees `baseShape` is in [ComposeDefaults] but missing from the user-facing
    // params, so it sets bit 2 ($default for baseShape) automatically — making
    // Kotlin substitute SegmentedButtonDefaults.getBaseShape() (the theme's
    // default rounded-corner shape).
    [ComposeBridge(
        Class         = "androidx/compose/material3/SegmentedButtonDefaults",
        JvmName       = "itemShape",
        Signature     = "(IILandroidx/compose/foundation/shape/CornerBasedShape;" +
                        "Landroidx/compose/runtime/Composer;II)" +
                        "Landroidx/compose/ui/graphics/Shape;",
        InstanceField = "INSTANCE",
        Defaults      = typeof(SegmentedButtonItemShapeDefault))]
    internal static partial IntPtr ItemShape(int index, int count, IComposer composer);

    // androidx.compose.material3.WideNavigationRailKt.WideNavigationRail.
    // No mangled hash (no inline-class types), but the binder still strips
    // it — likely because it's @ExperimentalMaterial3ExpressiveApi-annotated.
    [ComposeBridge(
        Class     = "androidx/compose/material3/WideNavigationRailKt",
        JvmName   = "WideNavigationRail",
        Signature = "(Landroidx/compose/ui/Modifier;" +
                    "Landroidx/compose/material3/WideNavigationRailState;" +
                    "Landroidx/compose/ui/graphics/Shape;" +
                    "Landroidx/compose/material3/WideNavigationRailColors;" +
                    "Lkotlin/jvm/functions/Function2;" +
                    "Landroidx/compose/foundation/layout/WindowInsets;" +
                    "Landroidx/compose/foundation/layout/Arrangement$Vertical;" +
                    "Lkotlin/jvm/functions/Function2;" +
                    "Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(WideNavigationRailDefault))]
    [ComposeFacade]
    public static partial void WideNavigationRail(IModifier? modifier, IFunction2 content, IComposer composer);

    // androidx.compose.material3.WideNavigationRailKt.ModalWideNavigationRail-k3FuEkE.
    // The "-k3FuEkE" hash comes from the @JvmInline value-class Dp param
    // (collapsedShadowElevation). 12 user params + 3 trailing ints
    // ($changed, $changed1, $default). The raw JNI bridge takes the full
    // surface; the [ComposeFacade]-wrapped partial below hides
    // `hideOnCollapse` (always true) so the public facade matches the
    // visibility-toggle pattern documented on the facade class.
    [ComposeBridge(
        Class     = "androidx/compose/material3/WideNavigationRailKt",
        JvmName   = "ModalWideNavigationRail-k3FuEkE",
        Signature = "(Landroidx/compose/ui/Modifier;" +
                    "Landroidx/compose/material3/WideNavigationRailState;Z" +
                    "Landroidx/compose/ui/graphics/Shape;" +
                    "Landroidx/compose/ui/graphics/Shape;" +
                    "Landroidx/compose/material3/WideNavigationRailColors;" +
                    "Lkotlin/jvm/functions/Function2;F" +
                    "Landroidx/compose/foundation/layout/WindowInsets;" +
                    "Landroidx/compose/foundation/layout/Arrangement$Vertical;" +
                    "Landroidx/compose/material3/ModalWideNavigationRailProperties;" +
                    "Lkotlin/jvm/functions/Function2;" +
                    "Landroidx/compose/runtime/Composer;III)V",
        Defaults  = typeof(ModalWideNavigationRailDefault))]
    internal static partial void ModalWideNavigationRailRaw(
        IModifier?  modifier,
        IntPtr      state,
        bool        hideOnCollapse,
        IFunction2? header,
        IFunction2  content,
        int         defaults,
        IComposer   composer);

    // Phase 8 wrapper-passthrough for the public ModalWideNavigationRail
    // facade. Hides `hideOnCollapse` (always true — the C# visibility-
    // toggle pattern owns mount/unmount), so the auto-mask never
    // computes the HideOnCollapse bit. We pre-clear it here before
    // forwarding, so Kotlin substitutes the body-supplied `true` rather
    // than its own default.
    [ComposeFacade(Container = true, Defaults = typeof(ModalWideNavigationRailDefault))]
    public static partial void ModalWideNavigationRail(
        IModifier?  modifier,
        [StateHolder(Remember = nameof(RememberWideNavigationRailState),
                     StateType = typeof(WideNavigationRailState))] IntPtr state,
        IFunction2? header,
        IFunction2  content,
        int         defaults,
        IComposer   composer);

    public static partial void ModalWideNavigationRail(
        IModifier?  modifier,
        IntPtr      state,
        IFunction2? header,
        IFunction2  content,
        int         defaults,
        IComposer   composer)
    {
        // Clear the HideOnCollapse bit so Kotlin uses our hardcoded
        // `true` rather than its own default (which is `false`).
        defaults &= ~(int)ModalWideNavigationRailDefault.HideOnCollapse;
        ModalWideNavigationRailRaw(
            modifier:        modifier,
            state:           state,
            hideOnCollapse:  true,
            header:          header,
            content:         content,
            defaults:        defaults,
            composer:        composer);
    }

    // Phase 8 wrapper-passthroughs for the navigation-drawer family.
    // Kotlin functions are all bound natively (no hash mangling on the
    // public overload), so we just forward to `NavigationDrawerKt.*`
    // and let the bridge generator stay out of it. Each bridge declares
    // the facade ctor slots the user actually controls (drawer, modifier,
    // optional state, content) and the bound binding fills in the
    // hard-coded Kotlin params (gesturesEnabled: true, scrimColor: 0L
    // — left at the Kotlin default sentinel via the bit mask). The
    // facade-side $default mask is computed by the [ComposeFacade]
    // generator from the matching [ComposeDefaults] declaration and
    // forwarded into _changed.

    [ComposeFacade(Defaults = typeof(ModalNavigationDrawerDefault))]
    public static partial void ModalNavigationDrawer(
        [Slot("Drawer")]  IFunction2 drawerContent,
        IModifier?        modifier,
        [StateHolder(Remember = nameof(RememberDrawerState),
                     StateType = typeof(DrawerStateHolder),
                     SharedState = true)] IntPtr drawerState,
        [Slot("Content")] IFunction2 content,
        bool              gesturesEnabled = true,
        int               defaults        = 0,
        IComposer         composer        = null!);

    public static partial void ModalNavigationDrawer(
        IFunction2 drawerContent, IModifier? modifier, IntPtr drawerState,
        IFunction2 content, bool gesturesEnabled, int defaults, IComposer composer)
    {
        // The bound binding takes a typed DrawerState; reconstitute it
        // from the JNI handle the [StateHolder] handed us.
        var stateObj = Java.Lang.Object.GetObject<DrawerState>(
            drawerState, Android.Runtime.JniHandleOwnership.DoNotTransfer)!;
        AndroidX.Compose.Material3.NavigationDrawerKt.ModalNavigationDrawer(
            drawerContent:    drawerContent,
            modifier:         modifier,
            drawerState:      stateObj,
            // The facade always supplies a value (caller's argument or the
            // `gesturesEnabled = true` ctor default) and clears the matching
            // bit in `_changed` so Kotlin uses our value.
            gesturesEnabled:  gesturesEnabled,
            scrimColor:       0L,
            content:          content,
            _composer:        composer,
            p7:               0,
            _changed:         defaults);
    }

    [ComposeFacade(Defaults = typeof(DismissibleNavigationDrawerDefault))]
    public static partial void DismissibleNavigationDrawer(
        [Slot("Drawer")]  IFunction2 drawerContent,
        IModifier?        modifier,
        [StateHolder(Remember = nameof(RememberDrawerState),
                     StateType = typeof(DrawerStateHolder),
                     SharedState = true)] IntPtr drawerState,
        [Slot("Content")] IFunction2 content,
        bool              gesturesEnabled = true,
        int               defaults        = 0,
        IComposer         composer        = null!);

    public static partial void DismissibleNavigationDrawer(
        IFunction2 drawerContent, IModifier? modifier, IntPtr drawerState,
        IFunction2 content, bool gesturesEnabled, int defaults, IComposer composer)
    {
        var stateObj = Java.Lang.Object.GetObject<DrawerState>(
            drawerState, Android.Runtime.JniHandleOwnership.DoNotTransfer)!;
        AndroidX.Compose.Material3.NavigationDrawerKt.DismissibleNavigationDrawer(
            drawerContent:    drawerContent,
            modifier:         modifier,
            drawerState:      stateObj,
            gesturesEnabled:  gesturesEnabled,
            content:          content,
            _composer:        composer,
            p6:               0,
            _changed:         defaults);
    }

    [ComposeFacade(Defaults = typeof(PermanentNavigationDrawerDefault))]
    public static partial void PermanentNavigationDrawer(
        [Slot("Drawer")]  IFunction2 drawerContent,
        IModifier?        modifier,
        [Slot("Content")] IFunction2 content,
        int               defaults,
        IComposer         composer);

    public static partial void PermanentNavigationDrawer(
        IFunction2 drawerContent, IModifier? modifier,
        IFunction2 content, int defaults, IComposer composer)
    {
        AndroidX.Compose.Material3.NavigationDrawerKt.PermanentNavigationDrawer(
            drawerContent: drawerContent,
            modifier:      modifier,
            content:       content,
            _composer:     composer,
            p4:            0,
            _changed:      defaults);
    }

    // Modifier-chain extensions. These are non-@Composable Kotlin
    // extension functions on Modifier; their JNI signatures end in
    // `I L<marker>` (the $default bitmask plus a synthetic-overload
    // Object marker, which Kotlin always passes as null). The bridge
    // generator emits the marker slot automatically.

    // androidx.compose.foundation.BackgroundKt.background-bw27NRU$default —
    // (Modifier, Color, Shape). Color is mangled because it's a
    // @JvmInline value class (ULong). The C# wrapper always supplies
    // color; shape is optional (null → Kotlin default of RectangleShape).
    [ComposeBridge(
        Class     = "androidx/compose/foundation/BackgroundKt",
        JvmName   = "background-bw27NRU$default",
        Signature = "(Landroidx/compose/ui/Modifier;J" +
                    "Landroidx/compose/ui/graphics/Shape;ILjava/lang/Object;)" +
                    "Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierBackgroundDefault))]
    internal static partial IntPtr ModifierBackground(IntPtr modifier, long color, IntPtr? shape);

    // androidx.compose.foundation.BackgroundKt.background$default —
    // (Modifier, Brush, Shape, float alpha). Not mangled (no value-class
    // params), but the bound C# overload requires non-null Shape and
    // alpha — and we want callers to be able to omit either, so we go
    // through the synthetic $default sibling and let auto-mask handle
    // the bits. alpha is always supplied (caller's default is 1f);
    // shape is auto-cleared when supplied.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/BackgroundKt",
        JvmName   = "background$default",
        Signature = "(Landroidx/compose/ui/Modifier;" +
                    "Landroidx/compose/ui/graphics/Brush;" +
                    "Landroidx/compose/ui/graphics/Shape;FILjava/lang/Object;)" +
                    "Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierBackgroundBrushDefault))]
    internal static partial IntPtr ModifierBackgroundBrush(
        IntPtr modifier, AndroidX.Compose.UI.Graphics.Brush brush, Shape? shape, float alpha);

    // androidx.compose.foundation.BorderKt.border-xT4_qwU$default —
    // (Modifier, Dp width, Color, Shape). Both width and color are
    // mangled inline-class params. Shape is optional (null → Kotlin
    // default of RectangleShape).
    [ComposeBridge(
        Class     = "androidx/compose/foundation/BorderKt",
        JvmName   = "border-xT4_qwU$default",
        Signature = "(Landroidx/compose/ui/Modifier;FJ" +
                    "Landroidx/compose/ui/graphics/Shape;ILjava/lang/Object;)" +
                    "Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierBorderDefault))]
    internal static partial IntPtr ModifierBorder(IntPtr modifier, float width, long color, IntPtr? shape);

    // androidx.compose.foundation.BorderKt.border-ziNgDLE$default —
    // (Modifier, Dp width, Brush, Shape). width is mangled because Dp
    // is a @JvmInline value class. shape is optional (null → Kotlin
    // default of RectangleShape).
    [ComposeBridge(
        Class     = "androidx/compose/foundation/BorderKt",
        JvmName   = "border-ziNgDLE$default",
        Signature = "(Landroidx/compose/ui/Modifier;F" +
                    "Landroidx/compose/ui/graphics/Brush;" +
                    "Landroidx/compose/ui/graphics/Shape;ILjava/lang/Object;)" +
                    "Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierBorderBrushDefault))]
    internal static partial IntPtr ModifierBorderBrush(
        IntPtr modifier, float width, AndroidX.Compose.UI.Graphics.Brush brush, Shape? shape);

    // androidx.compose.foundation.ClickableKt.clickable-XHw0xAI$default —
    // (Modifier, Boolean enabled, String onClickLabel, Role role,
    // Function0 onClick). Returns a Modifier directly — the lambda is
    // wrapped via composed { ... } internally so no Composer is needed.
    // The C# wrapper supplies onClick; enabled/onClickLabel/role are
    // left to Kotlin's defaults.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/ClickableKt",
        JvmName   = "clickable-XHw0xAI$default",
        Signature = "(Landroidx/compose/ui/Modifier;ZLjava/lang/String;" +
                    "Landroidx/compose/ui/semantics/Role;" +
                    "Lkotlin/jvm/functions/Function0;ILjava/lang/Object;)" +
                    "Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierClickableDefault))]
    internal static partial IntPtr ModifierClickable(IntPtr modifier, IFunction0 onClick);

    // androidx.compose.foundation.ScrollKt.verticalScroll$default —
    // non-@Composable Modifier extension. Kotlin params after the
    // receiver: state, enabled, flingBehavior?, reverseScrolling. The
    // C# wrapper always supplies state/enabled/reverseScrolling and
    // leaves flingBehavior to Kotlin's default
    // (ScrollableDefaults.flingBehavior()).
    [ComposeBridge(
        Class     = "androidx/compose/foundation/ScrollKt",
        JvmName   = "verticalScroll$default",
        Signature = "(Landroidx/compose/ui/Modifier;" +
                    "Landroidx/compose/foundation/ScrollState;Z" +
                    "Landroidx/compose/foundation/gestures/FlingBehavior;Z" +
                    "ILjava/lang/Object;)" +
                    "Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierVerticalScrollDefault))]
    internal static partial IntPtr ModifierVerticalScroll(
        IntPtr modifier, IntPtr state, bool enabled, bool reverseScrolling);

    // androidx.compose.foundation.ScrollKt.horizontalScroll$default —
    // same shape as ModifierVerticalScroll above.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/ScrollKt",
        JvmName   = "horizontalScroll$default",
        Signature = "(Landroidx/compose/ui/Modifier;" +
                    "Landroidx/compose/foundation/ScrollState;Z" +
                    "Landroidx/compose/foundation/gestures/FlingBehavior;Z" +
                    "ILjava/lang/Object;)" +
                    "Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierHorizontalScrollDefault))]
    internal static partial IntPtr ModifierHorizontalScroll(
        IntPtr modifier, IntPtr state, bool enabled, bool reverseScrolling);

    // androidx.compose.foundation.layout.SizeKt — ranged size constraints.
    // All Dp params are `@JvmInline value class Dp(val value: Float)`
    // hence the mangled JVM names. Each bridge takes Dp? per parameter so
    // a `null` value leaves the corresponding $default bit set and Kotlin
    // substitutes `Dp.Unspecified` (no constraint), letting callers
    // express one-sided constraints like `Modifier.WidthIn(min: 100)`.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/SizeKt",
        JvmName   = "widthIn-VpY3zN4$default",
        Signature = "(Landroidx/compose/ui/Modifier;FFILjava/lang/Object;)" +
                    "Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierWidthInDefault))]
    internal static partial IntPtr ModifierWidthIn(IntPtr modifier, Dp? min, Dp? max);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/SizeKt",
        JvmName   = "heightIn-VpY3zN4$default",
        Signature = "(Landroidx/compose/ui/Modifier;FFILjava/lang/Object;)" +
                    "Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierHeightInDefault))]
    internal static partial IntPtr ModifierHeightIn(IntPtr modifier, Dp? min, Dp? max);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/SizeKt",
        JvmName   = "sizeIn-qDBjuR0$default",
        Signature = "(Landroidx/compose/ui/Modifier;FFFFILjava/lang/Object;)" +
                    "Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierSizeInDefault))]
    internal static partial IntPtr ModifierSizeIn(IntPtr modifier, Dp? minWidth, Dp? minHeight, Dp? maxWidth, Dp? maxHeight);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/SizeKt",
        JvmName   = "defaultMinSize-VpY3zN4$default",
        Signature = "(Landroidx/compose/ui/Modifier;FFILjava/lang/Object;)" +
                    "Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierDefaultMinSizeDefault))]
    internal static partial IntPtr ModifierDefaultMinSize(IntPtr modifier, Dp? minWidth, Dp? minHeight);

    // androidx.compose.foundation.layout.SizeKt — required size variants.
    // requiredSize / requiredWidth / requiredHeight bypass parent
    // constraints (clip-out is permitted). No $default — both args are
    // mandatory positional. Plain-static shape, no auto-mask.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/SizeKt",
        JvmName   = "requiredSize-3ABfNKs",
        Signature = "(Landroidx/compose/ui/Modifier;F)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierRequiredSizeAll(IntPtr modifier, float dp);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/SizeKt",
        JvmName   = "requiredSize-VpY3zN4",
        Signature = "(Landroidx/compose/ui/Modifier;FF)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierRequiredSizeWH(IntPtr modifier, float width, float height);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/SizeKt",
        JvmName   = "requiredWidth-3ABfNKs",
        Signature = "(Landroidx/compose/ui/Modifier;F)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierRequiredWidth(IntPtr modifier, float dp);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/SizeKt",
        JvmName   = "requiredHeight-3ABfNKs",
        Signature = "(Landroidx/compose/ui/Modifier;F)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierRequiredHeight(IntPtr modifier, float dp);

    // androidx.compose.foundation.layout.SizeKt — wrapContent variants.
    // Each takes an Alignment (or Alignment.Horizontal / Vertical) and
    // an `unbounded` boolean. We always supply the boolean (bit cleared)
    // and leave alignment to Kotlin's default (Center / CenterHorizontally
    // / CenterVertically — bit kept set). Names are not mangled (no
    // inline-class params).
    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/SizeKt",
        JvmName   = "wrapContentSize$default",
        Signature = "(Landroidx/compose/ui/Modifier;Landroidx/compose/ui/Alignment;ZILjava/lang/Object;)" +
                    "Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierWrapContentSizeDefault))]
    internal static partial IntPtr ModifierWrapContentSize(IntPtr modifier, bool unbounded);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/SizeKt",
        JvmName   = "wrapContentWidth$default",
        Signature = "(Landroidx/compose/ui/Modifier;Landroidx/compose/ui/Alignment$Horizontal;ZILjava/lang/Object;)" +
                    "Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierWrapContentWidthDefault))]
    internal static partial IntPtr ModifierWrapContentWidth(IntPtr modifier, bool unbounded);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/SizeKt",
        JvmName   = "wrapContentHeight$default",
        Signature = "(Landroidx/compose/ui/Modifier;Landroidx/compose/ui/Alignment$Vertical;ZILjava/lang/Object;)" +
                    "Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierWrapContentHeightDefault))]
    internal static partial IntPtr ModifierWrapContentHeight(IntPtr modifier, bool unbounded);

    // androidx.compose.foundation.layout.AspectRatioKt.aspectRatio$default —
    // (Modifier, Float ratio, Boolean matchHeightConstraintsFirst). Both
    // params are non-inline so the JVM name is unmangled. Both bits are
    // cleared when the caller supplies values (the auto-mask treats
    // non-nullable primitives as unconditional clears).
    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/AspectRatioKt",
        JvmName   = "aspectRatio$default",
        Signature = "(Landroidx/compose/ui/Modifier;FZILjava/lang/Object;)" +
                    "Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierAspectRatioDefault))]
    internal static partial IntPtr ModifierAspectRatio(IntPtr modifier, float ratio, bool matchHeightConstraintsFirst);

    // androidx.compose.foundation.layout.OffsetKt — Dp-Dp offset shifts
    // a composable's draw position by (x, y) without affecting its parent
    // layout slot. `offset` is layout-direction-aware (start/end on RTL);
    // `absoluteOffset` is always absolute (left/right). Both mangled
    // because of @JvmInline value class Dp.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/OffsetKt",
        JvmName   = "offset-VpY3zN4$default",
        Signature = "(Landroidx/compose/ui/Modifier;FFILjava/lang/Object;)" +
                    "Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierOffsetDefault))]
    internal static partial IntPtr ModifierOffset(IntPtr modifier, Dp? x, Dp? y);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/OffsetKt",
        JvmName   = "absoluteOffset-VpY3zN4$default",
        Signature = "(Landroidx/compose/ui/Modifier;FFILjava/lang/Object;)" +
                    "Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierAbsoluteOffsetDefault))]
    internal static partial IntPtr ModifierAbsoluteOffset(IntPtr modifier, Dp? x, Dp? y);

    // androidx.compose.ui.ZIndexModifierKt.zIndex(Modifier, Float) — sets
    // the draw order within a parent layout (higher z draws later, on
    // top). No $default. Plain-static, unmangled.
    [ComposeBridge(
        Class     = "androidx/compose/ui/ZIndexModifierKt",
        JvmName   = "zIndex",
        Signature = "(Landroidx/compose/ui/Modifier;F)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierZIndex(IntPtr modifier, float z);

    // androidx.compose.ui.draw.AlphaKt.alpha(Modifier, Float) — sets the
    // composable's alpha (0 = invisible, 1 = opaque). Forces a graphics
    // layer; cheap to animate.
    [ComposeBridge(
        Class     = "androidx/compose/ui/draw/AlphaKt",
        JvmName   = "alpha",
        Signature = "(Landroidx/compose/ui/Modifier;F)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierAlpha(IntPtr modifier, float alpha);

    // androidx.compose.ui.draw.RotateKt.rotate(Modifier, Float) — rotates
    // around the composable's center by `degrees`.
    [ComposeBridge(
        Class     = "androidx/compose/ui/draw/RotateKt",
        JvmName   = "rotate",
        Signature = "(Landroidx/compose/ui/Modifier;F)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierRotate(IntPtr modifier, float degrees);

    // androidx.compose.ui.draw.ScaleKt.scale(Modifier, Float) — uniform
    // scale around the composable's center.
    [ComposeBridge(
        Class     = "androidx/compose/ui/draw/ScaleKt",
        JvmName   = "scale",
        Signature = "(Landroidx/compose/ui/Modifier;F)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierScaleUniform(IntPtr modifier, float scale);

    // androidx.compose.ui.draw.ScaleKt.scale(Modifier, Float, Float) —
    // independent X/Y scale. Distinct C# name avoids the partial-method
    // overload collision; the JVM resolves to the (M,F,F) overload.
    [ComposeBridge(
        Class     = "androidx/compose/ui/draw/ScaleKt",
        JvmName   = "scale",
        Signature = "(Landroidx/compose/ui/Modifier;FF)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierScaleXY(IntPtr modifier, float scaleX, float scaleY);

    // androidx.compose.ui.draw.ShadowKt.shadow-ziNgDLE$default —
    // (Modifier, Dp elevation, Shape shape, Boolean clip). Mangled because
    // elevation is @JvmInline value class Dp. The C# wrapper exposes
    // elevation + shape; Kotlin's default for `clip` is
    // `elevation > 0.dp` which we honor by leaving that bit set.
    [ComposeBridge(
        Class     = "androidx/compose/ui/draw/ShadowKt",
        JvmName   = "shadow-ziNgDLE$default",
        Signature = "(Landroidx/compose/ui/Modifier;F" +
                    "Landroidx/compose/ui/graphics/Shape;ZILjava/lang/Object;)" +
                    "Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierShadowDefault))]
    internal static partial IntPtr ModifierShadow(IntPtr modifier, float elevation, IntPtr? shape);

    // androidx.compose.ui.graphics.GraphicsLayerModifierKt.graphicsLayer-sKFY_QE$default —
    // (Modifier, F scaleX, F scaleY, F alpha, F translationX, F
    // translationY, F shadowElevation, F rotationX, F rotationY,
    // F rotationZ, F cameraDistance, J transformOrigin, Shape shape,
    // Z clip). 13 user params, all defaultable. Mangled because
    // `transformOrigin` is `@JvmInline value class TransformOrigin`
    // packed as a `long`. The C# wrapper uses `float?`/`long?`/
    // `IntPtr?`/`bool?` so the auto-mask clears each bit when a
    // value is supplied. This is the simplest of the three
    // graphicsLayer overloads — it omits the `RenderEffect`,
    // `ambientShadowColor`, `spotShadowColor`, and
    // `compositingStrategy` parameters introduced in later
    // versions. Most user code only touches alpha / rotation /
    // scale, which this overload covers.
    [ComposeBridge(
        Class     = "androidx/compose/ui/graphics/GraphicsLayerModifierKt",
        JvmName   = "graphicsLayer-sKFY_QE$default",
        Signature = "(Landroidx/compose/ui/Modifier;FFFFFFFFFFJ" +
                    "Landroidx/compose/ui/graphics/Shape;Z" +
                    "ILjava/lang/Object;)Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierGraphicsLayerDefault))]
    internal static partial IntPtr ModifierGraphicsLayer(
        IntPtr modifier,
        float? scaleX,
        float? scaleY,
        float? alpha,
        float? translationX,
        float? translationY,
        float? shadowElevation,
        float? rotationX,
        float? rotationY,
        float? rotationZ,
        float? cameraDistance,
        long? transformOrigin,
        IntPtr? shape,
        bool? clip);

    // androidx.compose.foundation.layout.WindowInsetsPadding_androidKt —
    // additional inset-padding helpers. Same shape as the existing
    // safeDrawingPadding / systemBarsPadding bridges.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/WindowInsetsPadding_androidKt",
        JvmName   = "imePadding",
        Signature = "(Landroidx/compose/ui/Modifier;)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierImePadding(IntPtr modifier);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/WindowInsetsPadding_androidKt",
        JvmName   = "navigationBarsPadding",
        Signature = "(Landroidx/compose/ui/Modifier;)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierNavigationBarsPadding(IntPtr modifier);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/WindowInsetsPadding_androidKt",
        JvmName   = "statusBarsPadding",
        Signature = "(Landroidx/compose/ui/Modifier;)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierStatusBarsPadding(IntPtr modifier);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/WindowInsetsPadding_androidKt",
        JvmName   = "displayCutoutPadding",
        Signature = "(Landroidx/compose/ui/Modifier;)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierDisplayCutoutPadding(IntPtr modifier);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/WindowInsetsPadding_androidKt",
        JvmName   = "captionBarPadding",
        Signature = "(Landroidx/compose/ui/Modifier;)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierCaptionBarPadding(IntPtr modifier);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/WindowInsetsPadding_androidKt",
        JvmName   = "mandatorySystemGesturesPadding",
        Signature = "(Landroidx/compose/ui/Modifier;)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierMandatorySystemGesturesPadding(IntPtr modifier);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/WindowInsetsPadding_androidKt",
        JvmName   = "safeContentPadding",
        Signature = "(Landroidx/compose/ui/Modifier;)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierSafeContentPadding(IntPtr modifier);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/WindowInsetsPadding_androidKt",
        JvmName   = "safeGesturesPadding",
        Signature = "(Landroidx/compose/ui/Modifier;)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierSafeGesturesPadding(IntPtr modifier);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/WindowInsetsPadding_androidKt",
        JvmName   = "systemGesturesPadding",
        Signature = "(Landroidx/compose/ui/Modifier;)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierSystemGesturesPadding(IntPtr modifier);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/layout/WindowInsetsPadding_androidKt",
        JvmName   = "waterfallPadding",
        Signature = "(Landroidx/compose/ui/Modifier;)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierWaterfallPadding(IntPtr modifier);

    // androidx.compose.ui.platform.TestTagKt.testTag(Modifier, String) —
    // attaches a test tag for UI testing frameworks.
    [ComposeBridge(
        Class     = "androidx/compose/ui/platform/TestTagKt",
        JvmName   = "testTag",
        Signature = "(Landroidx/compose/ui/Modifier;Ljava/lang/String;)" +
                    "Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierTestTag(IntPtr modifier, string tag);

    // androidx.compose.foundation.FocusableKt.focusable$default —
    // non-@Composable Modifier extension. Kotlin params after the
    // receiver: enabled (Boolean), interactionSource (MutableInteractionSource).
    // The C# wrapper always supplies enabled; interactionSource is left
    // to Kotlin's default (Compose allocates one per-call).
    [ComposeBridge(
        Class     = "androidx/compose/foundation/FocusableKt",
        JvmName   = "focusable$default",
        Signature = "(Landroidx/compose/ui/Modifier;Z" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "ILjava/lang/Object;)Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierFocusableDefault))]
    internal static partial IntPtr ModifierFocusable(IntPtr modifier, bool enabled);

    // androidx.compose.foundation.FocusableKt.focusGroup(Modifier) —
    // groups focusable descendants so two-dimensional focus search
    // treats them as a single unit. No defaults.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/FocusableKt",
        JvmName   = "focusGroup",
        Signature = "(Landroidx/compose/ui/Modifier;)Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierFocusGroup(IntPtr modifier);

    // androidx.compose.ui.focus.FocusChangedModifierKt.onFocusChanged —
    // (Modifier, Function1<FocusState, Unit>). No defaults — the
    // listener is always supplied by the caller.
    [ComposeBridge(
        Class     = "androidx/compose/ui/focus/FocusChangedModifierKt",
        JvmName   = "onFocusChanged",
        Signature = "(Landroidx/compose/ui/Modifier;Lkotlin/jvm/functions/Function1;)" +
                    "Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierOnFocusChanged(IntPtr modifier, IFunction1 onFocusChanged);

    // androidx.compose.ui.focus.FocusRequesterModifierKt.focusRequester —
    // (Modifier, FocusRequester). No defaults.
    [ComposeBridge(
        Class     = "androidx/compose/ui/focus/FocusRequesterModifierKt",
        JvmName   = "focusRequester",
        Signature = "(Landroidx/compose/ui/Modifier;Landroidx/compose/ui/focus/FocusRequester;)" +
                    "Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierFocusRequester(IntPtr modifier, IntPtr focusRequester);

    // androidx.compose.foundation.ClickableKt.combinedClickable-cJG_KMw$default —
    // the no-MutableInteractionSource overload. 7 Kotlin params after
    // the receiver: enabled, onClickLabel, role, onLongClickLabel,
    // onLongClick, onDoubleClick, onClick. C# wrapper always supplies
    // onClick (bit 6 cleared); other bits are toggled per-call based
    // on which arguments the caller passed.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/ClickableKt",
        JvmName   = "combinedClickable-cJG_KMw$default",
        Signature = "(Landroidx/compose/ui/Modifier;ZLjava/lang/String;" +
                    "Landroidx/compose/ui/semantics/Role;Ljava/lang/String;" +
                    "Lkotlin/jvm/functions/Function0;Lkotlin/jvm/functions/Function0;" +
                    "Lkotlin/jvm/functions/Function0;ILjava/lang/Object;)" +
                    "Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierCombinedClickableDefault))]
    internal static partial IntPtr ModifierCombinedClickable(
        IntPtr modifier,
        IFunction0? onLongClick,
        IFunction0? onDoubleClick,
        IFunction0  onClick);

    // androidx.compose.foundation.selection.SelectableKt.selectable-XHw0xAI$default —
    // 4 Kotlin params after the receiver: selected, enabled, role, onClick.
    // C# wrapper always supplies selected + onClick.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/selection/SelectableKt",
        JvmName   = "selectable-XHw0xAI$default",
        Signature = "(Landroidx/compose/ui/Modifier;ZZ" +
                    "Landroidx/compose/ui/semantics/Role;" +
                    "Lkotlin/jvm/functions/Function0;ILjava/lang/Object;)" +
                    "Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierSelectableDefault))]
    internal static partial IntPtr ModifierSelectable(
        IntPtr modifier, bool selected, IFunction0 onClick);

    // androidx.compose.foundation.selection.ToggleableKt.toggleable-XHw0xAI$default —
    // 4 Kotlin params after the receiver: value (Bool), enabled, role,
    // onValueChange (Function1<Boolean, Unit>). C# wrapper always
    // supplies value + onValueChange.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/selection/ToggleableKt",
        JvmName   = "toggleable-XHw0xAI$default",
        Signature = "(Landroidx/compose/ui/Modifier;ZZ" +
                    "Landroidx/compose/ui/semantics/Role;" +
                    "Lkotlin/jvm/functions/Function1;ILjava/lang/Object;)" +
                    "Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierToggleableDefault))]
    internal static partial IntPtr ModifierToggleable(
        IntPtr modifier, bool value, IFunction1 onValueChange);

    // androidx.compose.foundation.gestures.DraggableKt.draggable$default —
    // non-@Composable Modifier extension. 8 Kotlin params after the
    // receiver: state, orientation, enabled, interactionSource,
    // startDragImmediately, onDragStarted, onDragStopped,
    // reverseDirection. The C# wrapper always supplies state /
    // orientation / enabled (bits 0/1/2 cleared); the other five slots
    // (interactionSource + the two suspend Function3s + the two
    // booleans) stay defaulted in v1.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/gestures/DraggableKt",
        JvmName   = "draggable$default",
        Signature = "(Landroidx/compose/ui/Modifier;" +
                    "Landroidx/compose/foundation/gestures/DraggableState;" +
                    "Landroidx/compose/foundation/gestures/Orientation;Z" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;Z" +
                    "Lkotlin/jvm/functions/Function3;Lkotlin/jvm/functions/Function3;Z" +
                    "ILjava/lang/Object;)Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierDraggableDefault))]
    internal static partial IntPtr ModifierDraggable(
        IntPtr modifier, IntPtr state, IntPtr orientation, bool enabled);

    // androidx.compose.ui.semantics.SemanticsModifierKt.semantics$default —
    // 2 Kotlin params after the receiver: mergeDescendants (Bool),
    // properties (Function1<SemanticsPropertyReceiver, Unit>). The C#
    // wrapper always supplies the properties lambda.
    [ComposeBridge(
        Class     = "androidx/compose/ui/semantics/SemanticsModifierKt",
        JvmName   = "semantics$default",
        Signature = "(Landroidx/compose/ui/Modifier;Z" +
                    "Lkotlin/jvm/functions/Function1;ILjava/lang/Object;)" +
                    "Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierSemanticsDefault))]
    internal static partial IntPtr ModifierSemantics(
        IntPtr modifier, bool mergeDescendants, IFunction1 properties);

    // androidx.compose.ui.semantics.SemanticsModifierKt.clearAndSetSemantics —
    // (Modifier, Function1<SemanticsPropertyReceiver, Unit>). No defaults.
    [ComposeBridge(
        Class     = "androidx/compose/ui/semantics/SemanticsModifierKt",
        JvmName   = "clearAndSetSemantics",
        Signature = "(Landroidx/compose/ui/Modifier;" +
                    "Lkotlin/jvm/functions/Function1;)" +
                    "Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierClearAndSetSemantics(IntPtr modifier, IFunction1 properties);

    // androidx.compose.foundation.draganddrop.DragAndDropTargetKt.dragAndDropTarget —
    // non-@Composable Modifier extension. Both Kotlin params are required
    // (no $default). `shouldStartDragAndDrop` is a `(DragAndDropEvent) -> Boolean`
    // predicate that Compose calls once per drag-start to decide whether
    // this target should participate; `target` is the per-instance
    // `DragAndDropTarget` JCW whose `OnDrop` (and the other no-op
    // callbacks) the runtime fires while the drag is in progress.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/draganddrop/DragAndDropTargetKt",
        JvmName   = "dragAndDropTarget",
        Signature = "(Landroidx/compose/ui/Modifier;" +
                    "Lkotlin/jvm/functions/Function1;" +
                    "Landroidx/compose/ui/draganddrop/DragAndDropTarget;)" +
                    "Landroidx/compose/ui/Modifier;")]
    internal static partial IntPtr ModifierDragAndDropTarget(
        IntPtr modifier, IFunction1 shouldStartDragAndDrop, DragAndDropTarget target);

    // androidx.compose.ui.input.nestedscroll.NestedScrollModifierKt.nestedScroll$default —
    // non-@Composable Modifier extension. 2 Kotlin params after the
    // receiver: connection (NestedScrollConnection — always supplied by
    // the C# wrapper), dispatcher (NestedScrollDispatcher — left
    // defaulted so Kotlin allocates its own).
    [ComposeBridge(
        Class     = "androidx/compose/ui/input/nestedscroll/NestedScrollModifierKt",
        JvmName   = "nestedScroll$default",
        Signature = "(Landroidx/compose/ui/Modifier;" +
                    "Landroidx/compose/ui/input/nestedscroll/NestedScrollConnection;" +
                    "Landroidx/compose/ui/input/nestedscroll/NestedScrollDispatcher;" +
                    "ILjava/lang/Object;)Landroidx/compose/ui/Modifier;",
        Defaults  = typeof(ModifierNestedScrollDefault))]
    internal static partial IntPtr ModifierNestedScroll(
        IntPtr modifier,
        AndroidX.Compose.UI.Input.NestedScroll.INestedScrollConnection connection);

    // androidx.compose.material3.TopAppBarDefaults.enterAlwaysScrollBehavior —
    // @Composable instance method on the singleton. 4 user params; the
    // C# wrapper supplies only `state` and leaves `canScroll`,
    // `snapAnimationSpec`, `flingAnimationSpec` to Kotlin's defaults.
    [ComposeBridge(
        Class         = "androidx/compose/material3/TopAppBarDefaults",
        JvmName       = "enterAlwaysScrollBehavior",
        Signature     = "(Landroidx/compose/material3/TopAppBarState;" +
                        "Lkotlin/jvm/functions/Function0;" +
                        "Landroidx/compose/animation/core/AnimationSpec;" +
                        "Landroidx/compose/animation/core/DecayAnimationSpec;" +
                        "Landroidx/compose/runtime/Composer;II)" +
                        "Landroidx/compose/material3/TopAppBarScrollBehavior;",
        InstanceField = "INSTANCE",
        Defaults      = typeof(TopAppBarEnterAlwaysScrollBehaviorDefault))]
    internal static partial IntPtr TopAppBarDefaultsEnterAlwaysScrollBehavior(
        TopAppBarState state,
        IComposer composer);

    // androidx.compose.material3.TopAppBarDefaults.exitUntilCollapsedScrollBehavior —
    // same shape as enterAlwaysScrollBehavior above.
    [ComposeBridge(
        Class         = "androidx/compose/material3/TopAppBarDefaults",
        JvmName       = "exitUntilCollapsedScrollBehavior",
        Signature     = "(Landroidx/compose/material3/TopAppBarState;" +
                        "Lkotlin/jvm/functions/Function0;" +
                        "Landroidx/compose/animation/core/AnimationSpec;" +
                        "Landroidx/compose/animation/core/DecayAnimationSpec;" +
                        "Landroidx/compose/runtime/Composer;II)" +
                        "Landroidx/compose/material3/TopAppBarScrollBehavior;",
        InstanceField = "INSTANCE",
        Defaults      = typeof(TopAppBarExitUntilCollapsedScrollBehaviorDefault))]
    internal static partial IntPtr TopAppBarDefaultsExitUntilCollapsedScrollBehavior(
        TopAppBarState state,
        IComposer composer);

    // BoxScope / RowScope / ColumnScope `align` and `matchParentSize`
    // are abstract interface methods on the scope class (no static
    // $default helper exists — the methods themselves don't have
    // defaults). The [ComposeBridge] generator's static/ctor/instance-
    // field shapes don't fit, so we hand-write the JNI lookup +
    // CallObjectMethod here. The scope handle comes from
    // RenderContext.CurrentScope, set by the enclosing Box/Row/Column
    // facade for the duration of its content lambda.
    static IntPtr s_boxScopeClass;
    static IntPtr s_boxScopeAlignMethodId;
    static IntPtr s_boxScopeMatchParentSizeMethodId;

    internal static unsafe IntPtr BoxScopeAlign(IntPtr boxScope, IntPtr modifier, IntPtr alignment)
    {
        if (s_boxScopeAlignMethodId == IntPtr.Zero)
        {
            s_boxScopeClass = JNIEnv.FindClass("androidx/compose/foundation/layout/BoxScope");
            s_boxScopeAlignMethodId = JNIEnv.GetMethodID(
                s_boxScopeClass, "align",
                "(Landroidx/compose/ui/Modifier;Landroidx/compose/ui/Alignment;)Landroidx/compose/ui/Modifier;");
        }
        var args = stackalloc JValue[2];
        args[0] = new JValue(modifier);
        args[1] = new JValue(alignment);
        return JNIEnv.CallObjectMethod(boxScope, s_boxScopeAlignMethodId, args);
    }

    internal static unsafe IntPtr BoxScopeMatchParentSize(IntPtr boxScope, IntPtr modifier)
    {
        if (s_boxScopeMatchParentSizeMethodId == IntPtr.Zero)
        {
            if (s_boxScopeClass == IntPtr.Zero)
                s_boxScopeClass = JNIEnv.FindClass("androidx/compose/foundation/layout/BoxScope");
            s_boxScopeMatchParentSizeMethodId = JNIEnv.GetMethodID(
                s_boxScopeClass, "matchParentSize",
                "(Landroidx/compose/ui/Modifier;)Landroidx/compose/ui/Modifier;");
        }
        var args = stackalloc JValue[1];
        args[0] = new JValue(modifier);
        return JNIEnv.CallObjectMethod(boxScope, s_boxScopeMatchParentSizeMethodId, args);
    }

    static IntPtr s_rowScopeClass;
    static IntPtr s_rowScopeAlignMethodId;

    internal static unsafe IntPtr RowScopeAlignVertical(IntPtr rowScope, IntPtr modifier, IntPtr verticalAlignment)
    {
        if (s_rowScopeAlignMethodId == IntPtr.Zero)
        {
            s_rowScopeClass = JNIEnv.FindClass("androidx/compose/foundation/layout/RowScope");
            s_rowScopeAlignMethodId = JNIEnv.GetMethodID(
                s_rowScopeClass, "align",
                "(Landroidx/compose/ui/Modifier;Landroidx/compose/ui/Alignment$Vertical;)Landroidx/compose/ui/Modifier;");
        }
        var args = stackalloc JValue[2];
        args[0] = new JValue(modifier);
        args[1] = new JValue(verticalAlignment);
        return JNIEnv.CallObjectMethod(rowScope, s_rowScopeAlignMethodId, args);
    }

    static IntPtr s_columnScopeClass;
    static IntPtr s_columnScopeAlignMethodId;

    internal static unsafe IntPtr ColumnScopeAlignHorizontal(IntPtr columnScope, IntPtr modifier, IntPtr horizontalAlignment)
    {
        if (s_columnScopeAlignMethodId == IntPtr.Zero)
        {
            s_columnScopeClass = JNIEnv.FindClass("androidx/compose/foundation/layout/ColumnScope");
            s_columnScopeAlignMethodId = JNIEnv.GetMethodID(
                s_columnScopeClass, "align",
                "(Landroidx/compose/ui/Modifier;Landroidx/compose/ui/Alignment$Horizontal;)Landroidx/compose/ui/Modifier;");
        }
        var args = stackalloc JValue[2];
        args[0] = new JValue(modifier);
        args[1] = new JValue(horizontalAlignment);
        return JNIEnv.CallObjectMethod(columnScope, s_columnScopeAlignMethodId, args);
    }

    // androidx.compose.ui.focus.FocusRequester.requestFocus()V —
    // the no-arg overload the binding doesn't surface (it only exposed
    // the parameterised `requestFocus-3ESFkO8(int):Boolean`). Wrapped
    // hand-written because [ComposeBridge] doesn't support instance
    // method calls.
    static IntPtr s_focusRequesterClass;
    static IntPtr s_focusRequesterRequestFocusMethodId;

    internal static void FocusRequesterRequestFocus(IntPtr focusRequester)
    {
        if (s_focusRequesterRequestFocusMethodId == IntPtr.Zero)
        {
            s_focusRequesterClass = JNIEnv.FindClass("androidx/compose/ui/focus/FocusRequester");
            s_focusRequesterRequestFocusMethodId = JNIEnv.GetMethodID(
                s_focusRequesterClass, "requestFocus", "()V");
        }
        JNIEnv.CallVoidMethod(focusRequester, s_focusRequesterRequestFocusMethodId);
    }

    // androidx.compose.ui.semantics.SemanticsPropertiesKt.setContentDescription(
    //   SemanticsPropertyReceiver, String) — called from inside the
    // Function1 body built by Modifier.Semantics(string)/.ClearAndSetSemantics(string).
    // Static helper, no $default — wrap with the bridge generator.
    [ComposeBridge(
        Class     = "androidx/compose/ui/semantics/SemanticsPropertiesKt",
        JvmName   = "setContentDescription",
        Signature = "(Landroidx/compose/ui/semantics/SemanticsPropertyReceiver;" +
                    "Ljava/lang/String;)V")]
    internal static partial void SemanticsSetContentDescription(IntPtr receiver, string description);

    // androidx.compose.ui.semantics.SemanticsPropertiesKt.setRole-kuIjeqM(
    //   SemanticsPropertyReceiver, int) — JVM-mangled because the
    // Kotlin source signature takes a `Role` value class whose
    // underlying type is `int`. Same lambda-receiver invocation shape
    // as SetContentDescription. The int comes from
    // <see cref="AndroidX.Compose.SemanticsRole"/>.
    [ComposeBridge(
        Class     = "androidx/compose/ui/semantics/SemanticsPropertiesKt",
        JvmName   = "setRole-kuIjeqM",
        Signature = "(Landroidx/compose/ui/semantics/SemanticsPropertyReceiver;I)V")]
    internal static partial void SemanticsSetRole(IntPtr receiver, int role);

    // androidx.compose.ui.semantics.SemanticsPropertiesKt.setSelected(
    //   SemanticsPropertyReceiver, boolean) — toggles the "selected"
    // state read by TalkBack on grouped selection nodes (tabs, list
    // items). No mangling — no value class params. No $default.
    [ComposeBridge(
        Class     = "androidx/compose/ui/semantics/SemanticsPropertiesKt",
        JvmName   = "setSelected",
        Signature = "(Landroidx/compose/ui/semantics/SemanticsPropertyReceiver;Z)V")]
    internal static partial void SemanticsSetSelected(IntPtr receiver, bool selected);

    // androidx.compose.ui.semantics.SemanticsPropertiesKt.setStateDescription(
    //   SemanticsPropertyReceiver, String) — short state hint TalkBack
    // reads after the content description ("expanded", "3 of 5"). Same
    // shape as setContentDescription.
    [ComposeBridge(
        Class     = "androidx/compose/ui/semantics/SemanticsPropertiesKt",
        JvmName   = "setStateDescription",
        Signature = "(Landroidx/compose/ui/semantics/SemanticsPropertyReceiver;" +
                    "Ljava/lang/String;)V")]
    internal static partial void SemanticsSetStateDescription(IntPtr receiver, string description);

    // androidx.compose.ui.semantics.SemanticsPropertiesKt.heading(
    //   SemanticsPropertyReceiver) — tags the node as a heading for
    //   TalkBack. Compose's Foundation/Material baseline `heading()` is
    //   a single boolean (no level), matching MAUI's
    //   `SemanticHeadingLevel != None` semantic and ViewCompat's
    //   `setAccessibilityHeading(true)`. No mangling, no $default.
    [ComposeBridge(
        Class     = "androidx/compose/ui/semantics/SemanticsPropertiesKt",
        JvmName   = "heading",
        Signature = "(Landroidx/compose/ui/semantics/SemanticsPropertyReceiver;)V")]
    internal static partial void SemanticsSetHeading(IntPtr receiver);

    // androidx.compose.ui.semantics.SemanticsProperties_androidKt.setTestTagsAsResourceId(
    //   SemanticsPropertyReceiver, boolean) — opts the subtree into
    //   surfacing `Modifier.testTag(...)` as AccessibilityNodeInfo's
    //   `viewIdResourceName`. UIAutomator / Espresso / Appium-Android
    //   read elements from there for `By.id(...)` lookups, so MAUI
    //   `AutomationId` only round-trips to Appium when this flag is
    //   set somewhere on (or above) the testTag-carrying node.
    //
    // Lives on the Android-specific `SemanticsProperties_androidKt`
    // file (not the common `SemanticsPropertiesKt`) because the
    // property is `actual` only on the JVM target. Marked
    // `@ExperimentalComposeUiApi` upstream but stable enough for
    // production — Google uses it in Jetcaster, Now in Android, and
    // it's been the canonical Compose-↔-Espresso/UIAutomator bridge
    // since 2021. Boolean isn't a value class so no name mangling.
    [ComposeBridge(
        Class     = "androidx/compose/ui/semantics/SemanticsProperties_androidKt",
        JvmName   = "setTestTagsAsResourceId",
        Signature = "(Landroidx/compose/ui/semantics/SemanticsPropertyReceiver;Z)V")]
    internal static partial void SemanticsSetTestTagsAsResourceId(IntPtr receiver, bool value);

    // androidx.compose.ui.semantics.SemanticsPropertiesKt.onClick$default(
    //   SemanticsPropertyReceiver, String?, Function0<Boolean>?,
    //   int $default, Object marker) — registers a labelled
    // accessibility click action. Two defaultable Kotlin params:
    // label (bit 0, may be null = use platform default label) and
    // action (bit 1, always supplied by the C# wrapper). Auto-mask
    // shape: `string? label` lowers to the label bit; `IFunction0
    // action` is suppressed via `!action` in the defaults enum.
    [ComposeBridge(
        Class     = "androidx/compose/ui/semantics/SemanticsPropertiesKt",
        JvmName   = "onClick$default",
        Signature = "(Landroidx/compose/ui/semantics/SemanticsPropertyReceiver;" +
                    "Ljava/lang/String;Lkotlin/jvm/functions/Function0;" +
                    "ILjava/lang/Object;)V",
        Defaults  = typeof(SemanticsOnClickDefault))]
    internal static partial void SemanticsOnClick(IntPtr receiver, string? label, IFunction0 action);

    // Shape factories — additional overloads of RoundedCornerShape /
    // CutCornerShape beyond the existing Dp variant. The Int (percent)
    // overloads aren't mangled because Int isn't an inline class. The
    // Dp variant of CutCornerShape is mangled.
    [ComposeBridge(
        Class     = "androidx/compose/foundation/shape/RoundedCornerShapeKt",
        JvmName   = "RoundedCornerShape",
        Signature = "(I)Landroidx/compose/foundation/shape/RoundedCornerShape;")]
    internal static partial IntPtr RoundedCornerShapePercent(int percent);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/shape/CutCornerShapeKt",
        JvmName   = "CutCornerShape-0680j_4",
        Signature = "(F)Landroidx/compose/foundation/shape/CutCornerShape;")]
    internal static partial IntPtr CutCornerShapeDp(float dp);

    [ComposeBridge(
        Class     = "androidx/compose/foundation/shape/CutCornerShapeKt",
        JvmName   = "CutCornerShape",
        Signature = "(I)Landroidx/compose/foundation/shape/CutCornerShape;")]
    internal static partial IntPtr CutCornerShapePercent(int percent);

    // androidx.compose.material3.AppBarKt — TopAppBar / CenterAlignedTopAppBar
    // share the `-GHTll3U` shape (extra `expandedHeight: Dp` vs. the older
    // unmangled overload). 8 user params: title, modifier, navigationIcon,
    // actions, expandedHeight, windowInsets, colors, scrollBehavior.
    const string TopAppBarSig =
        "(Lkotlin/jvm/functions/Function2;Landroidx/compose/ui/Modifier;" +
        "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function3;F" +
        "Landroidx/compose/foundation/layout/WindowInsets;" +
        "Landroidx/compose/material3/TopAppBarColors;" +
        "Landroidx/compose/material3/TopAppBarScrollBehavior;" +
        "Landroidx/compose/runtime/Composer;II)V";

    [ComposeBridge(
        Class     = "androidx/compose/material3/AppBarKt",
        JvmName   = "TopAppBar-GHTll3U",
        Signature = TopAppBarSig,
        Defaults  = typeof(TopAppBarDefault))]
    [ComposeFacade(
        BranchOn        = "Subtitle",
        AlternateBridge = nameof(TopAppBarWithSubtitle))]
    public static partial void TopAppBar(
        IFunction2  title,
        IModifier?  modifier,
        IFunction2? navigationIcon,
        IFunction3? actions,
        ITopAppBarScrollBehavior? scrollBehavior,
        int         defaults,
        IComposer   composer);

    [ComposeBridge(
        Class     = "androidx/compose/material3/AppBarKt",
        JvmName   = "CenterAlignedTopAppBar-GHTll3U",
        Signature = TopAppBarSig,
        Defaults  = typeof(TopAppBarDefault))]
    [ComposeFacade]
    public static partial void CenterAlignedTopAppBar(
        IFunction2  title,
        IModifier?  modifier,
        IFunction2? navigationIcon,
        IFunction3? actions,
        ITopAppBarScrollBehavior? scrollBehavior,
        int         defaults,
        IComposer   composer);

    // MediumTopAppBar / LargeTopAppBar share `-oKE7A98` (two-row variants
    // take BOTH `collapsedHeight` and `expandedHeight` Dp). 9 user params.
    const string TwoRowsTopAppBarSig =
        "(Lkotlin/jvm/functions/Function2;Landroidx/compose/ui/Modifier;" +
        "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function3;FF" +
        "Landroidx/compose/foundation/layout/WindowInsets;" +
        "Landroidx/compose/material3/TopAppBarColors;" +
        "Landroidx/compose/material3/TopAppBarScrollBehavior;" +
        "Landroidx/compose/runtime/Composer;II)V";

    [ComposeBridge(
        Class     = "androidx/compose/material3/AppBarKt",
        JvmName   = "MediumTopAppBar-oKE7A98",
        Signature = TwoRowsTopAppBarSig,
        Defaults  = typeof(TwoRowsTopAppBarDefault))]
    [ComposeFacade(
        BranchOn        = "Subtitle",
        AlternateBridge = nameof(MediumFlexibleTopAppBar))]
    public static partial void MediumTopAppBar(
        IFunction2  title,
        IModifier?  modifier,
        IFunction2? navigationIcon,
        IFunction3? actions,
        ITopAppBarScrollBehavior? scrollBehavior,
        int         defaults,
        IComposer   composer);

    [ComposeBridge(
        Class     = "androidx/compose/material3/AppBarKt",
        JvmName   = "LargeTopAppBar-oKE7A98",
        Signature = TwoRowsTopAppBarSig,
        Defaults  = typeof(TwoRowsTopAppBarDefault))]
    [ComposeFacade(
        BranchOn        = "Subtitle",
        AlternateBridge = nameof(LargeFlexibleTopAppBar))]
    public static partial void LargeTopAppBar(
        IFunction2  title,
        IModifier?  modifier,
        IFunction2? navigationIcon,
        IFunction3? actions,
        ITopAppBarScrollBehavior? scrollBehavior,
        int         defaults,
        IComposer   composer);

    // androidx.compose.material3.TabRowKt — TabRow / PrimaryTabRow /
    // SecondaryTabRow all share `-pAZo6Ak`. 7 user params: selectedTabIndex,
    // modifier, containerColor, contentColor, indicator, divider, tabs.
    // (Primary/Secondary's indicator Function3 has a TabIndicatorScope
    // receiver; TabRow's has List<TabPosition> — irrelevant to the
    // descriptor since both compile to Function3.)
    const string TabRowSig =
        "(ILandroidx/compose/ui/Modifier;JJ" +
        "Lkotlin/jvm/functions/Function3;Lkotlin/jvm/functions/Function2;" +
        "Lkotlin/jvm/functions/Function2;Landroidx/compose/runtime/Composer;II)V";

    [ComposeBridge(
        Class     = "androidx/compose/material3/TabRowKt",
        JvmName   = "TabRow-pAZo6Ak",
        Signature = TabRowSig,
        Defaults  = typeof(TabRowDefault))]
    [ComposeFacade]
    public static partial void TabRow(
        int        selectedTabIndex,
        IModifier? modifier,
        IFunction2 tabs,
        IComposer  composer);

    [ComposeBridge(
        Class     = "androidx/compose/material3/TabRowKt",
        JvmName   = "PrimaryTabRow-pAZo6Ak",
        Signature = TabRowSig,
        Defaults  = typeof(TabRowDefault))]
    [ComposeFacade]
    public static partial void PrimaryTabRow(
        int        selectedTabIndex,
        IModifier? modifier,
        IFunction2 tabs,
        IComposer  composer);

    [ComposeBridge(
        Class     = "androidx/compose/material3/TabRowKt",
        JvmName   = "SecondaryTabRow-pAZo6Ak",
        Signature = TabRowSig,
        Defaults  = typeof(TabRowDefault))]
    [ComposeFacade]
    public static partial void SecondaryTabRow(
        int        selectedTabIndex,
        IModifier? modifier,
        IFunction2 tabs,
        IComposer  composer);

    // androidx.compose.material3.TabRowKt.ScrollableTabRow-sKfQg0A.
    // 8 user params: same as TabRow plus a leading `edgePadding: Dp`.
    [ComposeBridge(
        Class     = "androidx/compose/material3/TabRowKt",
        JvmName   = "ScrollableTabRow-sKfQg0A",
        Signature = "(ILandroidx/compose/ui/Modifier;JJF" +
                    "Lkotlin/jvm/functions/Function3;Lkotlin/jvm/functions/Function2;" +
                    "Lkotlin/jvm/functions/Function2;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(ScrollableTabRowDefault))]
    [ComposeFacade]
    public static partial void ScrollableTabRow(
        int        selectedTabIndex,
        IModifier? modifier,
        IFunction2 tabs,
        IComposer  composer);

    // androidx.compose.material3.TabKt.Tab-wqdebIU (text/icon overload).
    // 9 user params: selected, onClick, modifier, enabled, text, icon,
    // selectedContentColor, unselectedContentColor, interactionSource.
    [ComposeBridge(
        Class     = "androidx/compose/material3/TabKt",
        JvmName   = "Tab-wqdebIU",
        Signature = "(ZLkotlin/jvm/functions/Function0;Landroidx/compose/ui/Modifier;Z" +
                    "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;JJ" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(TabDefault))]
    [ComposeFacade]
    public static partial void Tab(
        bool        selected,
        IFunction0  onClick,
        IModifier?  modifier,
        IFunction2? text,
        IFunction2? icon,
        int         defaults,
        IComposer   composer);

    // androidx.compose.material3.TabKt.LeadingIconTab-wqdebIU.
    // 9 user params: selected, onClick, text, icon, modifier, enabled,
    // selectedContentColor, unselectedContentColor, interactionSource.
    // Note text and icon are REQUIRED (no Kotlin default), unlike Tab.
    [ComposeBridge(
        Class     = "androidx/compose/material3/TabKt",
        JvmName   = "LeadingIconTab-wqdebIU",
        Signature = "(ZLkotlin/jvm/functions/Function0;" +
                    "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
                    "Landroidx/compose/ui/Modifier;ZJJ" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(LeadingIconTabDefault))]
    [ComposeFacade]
    public static partial void LeadingIconTab(
        bool       selected,
        IFunction0 onClick,
        IFunction2 text,
        IFunction2 icon,
        IModifier? modifier,
        IComposer  composer);

    // androidx.compose.material3.SnackbarKt.Snackbar-eQBnUkQ. 10 user
    // params: modifier, action, dismissAction, actionOnNewLine, shape,
    // 4 colors, content. Bit 9 (content) always provided.
    [ComposeBridge(
        Class     = "androidx/compose/material3/SnackbarKt",
        JvmName   = "Snackbar-eQBnUkQ",
        Signature = "(Landroidx/compose/ui/Modifier;" +
                    "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;Z" +
                    "Landroidx/compose/ui/graphics/Shape;JJJJ" +
                    "Lkotlin/jvm/functions/Function2;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(SnackbarDefault))]
    [ComposeFacade]
    public static partial void Snackbar(
        IModifier?  modifier,
        IFunction2? action,
        IFunction2? dismissAction,
        [Slot("Body")] IFunction2 content,
        int         defaults,
        IComposer   composer);

    // androidx.compose.material3.SnackbarHostKt.SnackbarHost — UNMANGLED
    // (no inline-class params). 3 user params: hostState, modifier,
    // snackbar. Bits 0 (hostState) and 2 (snackbar) always provided.
    [ComposeBridge(
        Class     = "androidx/compose/material3/SnackbarHostKt",
        JvmName   = "SnackbarHost",
        Signature = "(Landroidx/compose/material3/SnackbarHostState;" +
                    "Landroidx/compose/ui/Modifier;Lkotlin/jvm/functions/Function3;" +
                    "Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(SnackbarHostDefault))]
    public static partial void SnackbarHost(
        IntPtr     hostState,
        IModifier? modifier,
        IFunction3 snackbar,
        IComposer  composer);

    // androidx.compose.material3.BadgeKt.Badge-eopBjH0. 4 user params:
    // modifier, containerColor, contentColor, content (RowScope-receiver
    // Function3). Bit 3 (content) always provided.
    [ComposeBridge(
        Class     = "androidx/compose/material3/BadgeKt",
        JvmName   = "Badge-eopBjH0",
        Signature = "(Landroidx/compose/ui/Modifier;JJ" +
                    "Lkotlin/jvm/functions/Function3;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(BadgeDefault))]
    [ComposeFacade(Scope = "Row")]
    public static partial void Badge(IModifier? modifier, IFunction3 content, IComposer composer);

    // androidx.compose.material3.BadgeKt.BadgedBox — UNMANGLED. 3 user
    // params: badge (BoxScope-receiver Function3), modifier, content
    // (BoxScope-receiver Function3). Bits 0 (badge) and 2 (content)
    // always provided.
    [ComposeBridge(
        Class     = "androidx/compose/material3/BadgeKt",
        JvmName   = "BadgedBox",
        Signature = "(Lkotlin/jvm/functions/Function3;Landroidx/compose/ui/Modifier;" +
                    "Lkotlin/jvm/functions/Function3;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(BadgedBoxDefault))]
    [ComposeFacade]
    public static partial void BadgedBox(
        IFunction3 badge,
        IModifier? modifier,
        IFunction3 content,
        IComposer  composer);

    // androidx.compose.material3.ListItemKt.ListItem-HXNGIdc. 9 user
    // params: headlineContent, modifier, overlineContent, supportingContent,
    // leadingContent, trailingContent, colors, tonalElevation,
    // shadowElevation. Bit 0 (headlineContent) always provided.
    [ComposeBridge(
        Class     = "androidx/compose/material3/ListItemKt",
        JvmName   = "ListItem-HXNGIdc",
        Signature = "(Lkotlin/jvm/functions/Function2;Landroidx/compose/ui/Modifier;" +
                    "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
                    "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
                    "Landroidx/compose/material3/ListItemColors;FF" +
                    "Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(ListItemDefault))]
    [ComposeFacade]
    public static partial void ListItem(
        [Slot("Headline")]   IFunction2  headlineContent,
        IModifier?  modifier,
        [Slot("Overline")]   IFunction2? overlineContent,
        [Slot("Supporting")] IFunction2? supportingContent,
        [Slot("Leading")]    IFunction2? leadingContent,
        [Slot("Trailing")]   IFunction2? trailingContent,
        int         defaults,
        IComposer   composer);

    // androidx.compose.material3.AndroidMenu_androidKt.DropdownMenu-IlH_yew —
    // the menu *container* that anchors the (already-bound) DropdownMenuItem.
    // 12 user params: expanded, onDismissRequest, modifier, offset (DpOffset
    // packed in J), scrollState, properties, shape, containerColor (J),
    // tonalElevation (Dp F), shadowElevation (Dp F), border, content
    // (ColumnScope receiver Function3). Bits 0/1/11 (expanded, onDismissRequest,
    // content) always provided.
    [ComposeBridge(
        Class     = "androidx/compose/material3/AndroidMenu_androidKt",
        JvmName   = "DropdownMenu-IlH_yew",
        Signature = "(ZLkotlin/jvm/functions/Function0;Landroidx/compose/ui/Modifier;J" +
                    "Landroidx/compose/foundation/ScrollState;" +
                    "Landroidx/compose/ui/window/PopupProperties;" +
                    "Landroidx/compose/ui/graphics/Shape;JFF" +
                    "Landroidx/compose/foundation/BorderStroke;" +
                    "Lkotlin/jvm/functions/Function3;Landroidx/compose/runtime/Composer;III)V",
        Defaults  = typeof(DropdownMenuDefault))]
    [ComposeFacade]
    public static partial void DropdownMenu(
        bool        expanded,
        IFunction0  onDismissRequest,
        IModifier?  modifier,
        IFunction3  content,
        IComposer   composer);

    // androidx.compose.material3.ExposedDropdownMenuKt.ExposedDropdownMenuBox —
    // textfield-anchored menu container. 4 user params: expanded, onExpandedChange
    // (Function1<Boolean,Unit>), modifier, content (Function3 with
    // ExposedDropdownMenuBoxScope receiver). Bits 0/1/3 always provided.
    // The Kt method itself is NOT mangled (no inline-class params), but the
    // binder still strips the trailing `$default` int, so we go through JNI.
    [ComposeBridge(
        Class     = "androidx/compose/material3/ExposedDropdownMenuKt",
        JvmName   = "ExposedDropdownMenuBox",
        Signature = "(ZLkotlin/jvm/functions/Function1;Landroidx/compose/ui/Modifier;" +
                    "Lkotlin/jvm/functions/Function3;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(ExposedDropdownMenuBoxDefault))]
    [ComposeFacade(Scope = "Other")]
    public static partial void ExposedDropdownMenuBox(
        bool       expanded,
        [Callback(typeof(bool))] IFunction1 onExpandedChange,
        IModifier? modifier,
        IFunction3 content,
        IComposer  composer);

    // androidx.compose.material3.ExposedDropdownMenuBoxScope.ExposedDropdownMenu
    // (the simplest unmangled overload — boolean + Function0 + Modifier +
    // ScrollState + Function3<ColumnScope, Composer, Integer, Unit>). This is
    // an INSTANCE method on the abstract scope class (not a static helper, not
    // a Kotlin object singleton), so the [ComposeBridge] generator's static /
    // ctor / InstanceField shapes don't fit — hand-written raw JNI in the
    // wrapper-passthrough body below.
    //
    // The receiver is whichever scope the enclosing ExposedDropdownMenuBox
    // pushed onto RenderContext (auto-bound by the facade generator because
    // the param name ends in "Scope"). We do NOT cache the receiver —
    // Compose hands a fresh scope per recomposition.
    static IntPtr s_exposedDropdownMenuBoxScopeClass;
    static IntPtr s_exposedDropdownMenuMethodId;

    [ComposeFacade(Defaults = typeof(ExposedDropdownMenuDefault))]
    public static unsafe partial void ExposedDropdownMenu(
        IntPtr     exposedDropdownMenuBoxScope,
        bool       expanded,
        IFunction0 onDismissRequest,
        IModifier? modifier,
        IFunction3 content,
        int        defaults,
        IComposer  composer);

    public static unsafe partial void ExposedDropdownMenu(
        IntPtr     exposedDropdownMenuBoxScope,
        bool       expanded,
        IFunction0 onDismissRequest,
        IModifier? modifier,
        IFunction3 content,
        int        defaults,
        IComposer  composer)
    {
        if (exposedDropdownMenuBoxScope == IntPtr.Zero)
            throw new InvalidOperationException(
                "ExposedDropdownMenu must be rendered inside an ExposedDropdownMenuBox " +
                "so it can resolve the menu-anchor scope.");

        if (s_exposedDropdownMenuMethodId == IntPtr.Zero)
        {
            // JNIEnv.FindClass on .NET for Android returns a long-lived
            // global handle the runtime caches — passing it to
            // DeleteLocalRef trips CheckJNI ("expected Local but found
            // Global"). Cache it directly, matching every generated bridge.
            s_exposedDropdownMenuBoxScopeClass = JNIEnv.FindClass(
                "androidx/compose/material3/ExposedDropdownMenuBoxScope");
            s_exposedDropdownMenuMethodId = JNIEnv.GetMethodID(
                s_exposedDropdownMenuBoxScopeClass,
                "ExposedDropdownMenu",
                "(ZLkotlin/jvm/functions/Function0;Landroidx/compose/ui/Modifier;" +
                "Landroidx/compose/foundation/ScrollState;Lkotlin/jvm/functions/Function3;" +
                "Landroidx/compose/runtime/Composer;II)V");
        }

        IntPtr modifierHandle = ModifierHandle(modifier);
        var args = stackalloc JValue[8];
        args[0] = new JValue(expanded);
        args[1] = new JValue(((Java.Lang.Object)onDismissRequest).Handle);
        args[2] = new JValue(modifierHandle);
        args[3] = new JValue(IntPtr.Zero); // scrollState — defaulted
        args[4] = new JValue(((Java.Lang.Object)content).Handle);
        args[5] = new JValue(((Java.Lang.Object)composer).Handle);
        args[6] = new JValue(0); // $changed
        args[7] = new JValue(defaults);

        try
        {
            JNIEnv.CallVoidMethod(exposedDropdownMenuBoxScope, s_exposedDropdownMenuMethodId, args);
        }
        finally
        {
            GC.KeepAlive(onDismissRequest);
            GC.KeepAlive(modifier);
            GC.KeepAlive(content);
            GC.KeepAlive(composer);
        }
    }

    // androidx.compose.material3.SearchBarKt.rememberSearchBarState —
    // factory @Composable that produces a `SearchBarState`. 3 user params:
    // initialValue (enum), animationSpecForExpand, animationSpecForCollapse.
    // The wrapper defaults all three (initialValue defaults to Collapsed).
    [ComposeBridge(
        Class     = "androidx/compose/material3/SearchBarKt",
        JvmName   = "rememberSearchBarState",
        Signature = "(Landroidx/compose/material3/SearchBarValue;" +
                    "Landroidx/compose/animation/core/AnimationSpec;" +
                    "Landroidx/compose/animation/core/AnimationSpec;" +
                    "Landroidx/compose/runtime/Composer;II)" +
                    "Landroidx/compose/material3/SearchBarState;",
        Defaults  = typeof(RememberSearchBarStateDefault))]
    public static partial IntPtr RememberSearchBarState(IComposer composer);

    // androidx.compose.material3.SearchBarKt.SearchBar-nbWgWpA — the
    // state-based collapsed search bar. 7 user params: state, inputField
    // (Function2), modifier, shape, colors, inputFieldHeight (Dp F),
    // floatingHeight (Dp F). Bits 0/1 (state, inputField) always provided.
    [ComposeBridge(
        Class     = "androidx/compose/material3/SearchBarKt",
        JvmName   = "SearchBar-nbWgWpA",
        Signature = "(Landroidx/compose/material3/SearchBarState;" +
                    "Lkotlin/jvm/functions/Function2;Landroidx/compose/ui/Modifier;" +
                    "Landroidx/compose/ui/graphics/Shape;" +
                    "Landroidx/compose/material3/SearchBarColors;FF" +
                    "Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(SearchBarDefault))]
    public static partial void SearchBar(
        IntPtr     state,
        IFunction2 inputField,
        IModifier? modifier,
        IComposer  composer);

    // androidx.compose.material3.SearchBarKt.TopSearchBar-qKj4JfE — like
    // SearchBar but draws as a scrollable top bar (adds WindowInsets +
    // SearchBarScrollBehavior). 9 user params; bits 0/1 always provided.
    [ComposeBridge(
        Class     = "androidx/compose/material3/SearchBarKt",
        JvmName   = "TopSearchBar-qKj4JfE",
        Signature = "(Landroidx/compose/material3/SearchBarState;" +
                    "Lkotlin/jvm/functions/Function2;Landroidx/compose/ui/Modifier;" +
                    "Landroidx/compose/ui/graphics/Shape;" +
                    "Landroidx/compose/material3/SearchBarColors;FF" +
                    "Landroidx/compose/foundation/layout/WindowInsets;" +
                    "Landroidx/compose/material3/SearchBarScrollBehavior;" +
                    "Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(TopSearchBarDefault))]
    public static partial void TopSearchBar(
        IntPtr     state,
        IFunction2 inputField,
        IModifier? modifier,
        IComposer  composer);

    // androidx.compose.material3.SearchBarKt.ExpandedDockedSearchBar-qKj4JfE —
    // the expanded popup half of a docked SearchBar pair. Renders only when
    // the SearchBarState is Expanded. 9 user params: state, inputField,
    // modifier, shape, colors, inputFieldHeight, floatingHeight, properties,
    // content (ColumnScope receiver Function3). Bits 0/1/8 always provided.
    [ComposeBridge(
        Class     = "androidx/compose/material3/SearchBarKt",
        JvmName   = "ExpandedDockedSearchBar-qKj4JfE",
        Signature = "(Landroidx/compose/material3/SearchBarState;" +
                    "Lkotlin/jvm/functions/Function2;Landroidx/compose/ui/Modifier;" +
                    "Landroidx/compose/ui/graphics/Shape;" +
                    "Landroidx/compose/material3/SearchBarColors;FF" +
                    "Landroidx/compose/ui/window/PopupProperties;" +
                    "Lkotlin/jvm/functions/Function3;" +
                    "Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(ExpandedDockedSearchBarDefault))]
    public static partial void ExpandedDockedSearchBar(
        IntPtr     state,
        IFunction2 inputField,
        IModifier? modifier,
        IFunction3 content,
        IComposer  composer);

    // androidx.compose.material3.SearchBarKt.ExpandedFullScreenSearchBar-_UtchM0 —
    // the full-screen popup half of a SearchBar pair (Dialog-based). 10
    // user params: state, inputField, modifier, shape, colors, inputFieldHeight,
    // floatingHeight, windowInsets (Function2 producing WindowInsets),
    // properties (DialogProperties), content. Bits 0/1/9 always provided.
    [ComposeBridge(
        Class     = "androidx/compose/material3/SearchBarKt",
        JvmName   = "ExpandedFullScreenSearchBar-_UtchM0",
        Signature = "(Landroidx/compose/material3/SearchBarState;" +
                    "Lkotlin/jvm/functions/Function2;Landroidx/compose/ui/Modifier;" +
                    "Landroidx/compose/ui/graphics/Shape;" +
                    "Landroidx/compose/material3/SearchBarColors;FF" +
                    "Lkotlin/jvm/functions/Function2;" +
                    "Landroidx/compose/ui/window/DialogProperties;" +
                    "Lkotlin/jvm/functions/Function3;" +
                    "Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(ExpandedFullScreenSearchBarDefault))]
    public static partial void ExpandedFullScreenSearchBar(
        IntPtr     state,
        IFunction2 inputField,
        IModifier? modifier,
        IFunction3 content,
        IComposer  composer);

    // androidx.compose.material3.SearchBarKt.DockedSearchBar-EQC0FA8 — the
    // boolean-state docked variant. Deprecated in M3 1.4 in favor of the
    // state-based `SearchBar` + `ExpandedDockedSearchBar` pair, but still
    // ships in 1.4.0.3 and is the only standalone "DockedSearchBar" entry
    // point — the new state-based design has no separate `DockedSearchBar`
    // composable. 9 user params: inputField (Function2), expanded (Z),
    // onExpandedChange (Function1<Boolean,Unit>), modifier, shape, colors,
    // tonalElevation (Dp F), shadowElevation (Dp F), content (Function3
    // ColumnScope receiver). Bits 0/1/2/8 always provided.
    [ComposeBridge(
        Class     = "androidx/compose/material3/SearchBarKt",
        JvmName   = "DockedSearchBar-EQC0FA8",
        Signature = "(Lkotlin/jvm/functions/Function2;ZLkotlin/jvm/functions/Function1;" +
                    "Landroidx/compose/ui/Modifier;" +
                    "Landroidx/compose/ui/graphics/Shape;" +
                    "Landroidx/compose/material3/SearchBarColors;FF" +
                    "Lkotlin/jvm/functions/Function3;" +
                    "Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(DockedSearchBarDefault))]
    public static partial void DockedSearchBar(
        IFunction2 inputField,
        bool       expanded,
        IFunction1 onExpandedChange,
        IModifier? modifier,
        IFunction3 content,
        IComposer  composer);

    // androidx.compose.material3.SearchBarKt.DockedSearchBar-eWTbjVg — the
    // even older query-based docked search bar. Self-contained input field
    // (no separate inputField slot); the caller drives `query` text and the
    // `active` boolean directly, with `placeholder` / `leadingIcon` /
    // `trailingIcon` as composable slots and `content` as the docked
    // results list. Deprecated in M3 1.2 in favor of the boolean-state
    // overload above, and again in M3 1.4 in favor of the state-based
    // `SearchBar` + `ExpandedDockedSearchBar` pair, but still ships in
    // 1.4.0.3 — and it's what compose-samples/Reply uses for the inbox
    // search affordance (see issue #165). 16 user params, packed into two
    // `$changed` ints + one `$default` (trailing `III`). Bits 0/1/2/3/4
    // (query, onQueryChange, onSearch, active, onActiveChange) and bit 15
    // (content) are always provided. Bits 10–14 (shape, colors,
    // tonalElevation, shadowElevation, interactionSource) are unmapped
    // and stay defaulted by Kotlin; bit 6 (enabled) likewise defaults to
    // Kotlin's `true`.
    [ComposeBridge(
        Class     = "androidx/compose/material3/SearchBarKt",
        JvmName   = "DockedSearchBar-eWTbjVg",
        Signature = "(Ljava/lang/String;" +
                    "Lkotlin/jvm/functions/Function1;" +
                    "Lkotlin/jvm/functions/Function1;Z" +
                    "Lkotlin/jvm/functions/Function1;" +
                    "Landroidx/compose/ui/Modifier;Z" +
                    "Lkotlin/jvm/functions/Function2;" +
                    "Lkotlin/jvm/functions/Function2;" +
                    "Lkotlin/jvm/functions/Function2;" +
                    "Landroidx/compose/ui/graphics/Shape;" +
                    "Landroidx/compose/material3/SearchBarColors;FF" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Lkotlin/jvm/functions/Function3;" +
                    "Landroidx/compose/runtime/Composer;III)V",
        Defaults  = typeof(DockedSearchBarWithQueryDefault))]
    public static partial void DockedSearchBarWithQuery(
        string      query,
        IFunction1  onQueryChange,
        IFunction1  onSearch,
        bool        active,
        IFunction1  onActiveChange,
        IModifier?  modifier,
        IFunction2? placeholder,
        IFunction2? leadingIcon,
        IFunction2? trailingIcon,
        IFunction3  content,
        IComposer   composer);

    // androidx.compose.material3.SearchBarDefaults.InputField — state-based
    // overload. The state-aware InputField is what wires focus/click events
    // to SearchBarState (animateToExpanded/Collapsed), so this is what makes
    // tapping the bar actually expand the popup. Instance method on the
    // Kotlin SearchBarDefaults singleton — InstanceField walks the static
    // INSTANCE field once, caches a global ref, then calls via CallVoidMethod.
    //
    // 18 user params (textFieldState, searchBarState, onSearch, modifier,
    // enabled, autoFocus, textStyle, placeholder, leadingIcon, trailingIcon,
    // prefix, suffix, inputTransformation, outputTransformation, scrollState,
    // shape, colors, interactionSource). Bits 0-2 always provided
    // (textFieldState, searchBarState, onSearch); we expose modifier +
    // placeholder + leadingIcon + trailingIcon and let the rest default.
    [ComposeBridge(
        Class         = "androidx/compose/material3/SearchBarDefaults",
        JvmName       = "InputField",
        Signature     = "(Landroidx/compose/foundation/text/input/TextFieldState;" +
                        "Landroidx/compose/material3/SearchBarState;" +
                        "Lkotlin/jvm/functions/Function1;Landroidx/compose/ui/Modifier;ZZ" +
                        "Landroidx/compose/ui/text/TextStyle;" +
                        "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
                        "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
                        "Lkotlin/jvm/functions/Function2;" +
                        "Landroidx/compose/foundation/text/input/InputTransformation;" +
                        "Landroidx/compose/foundation/text/input/OutputTransformation;" +
                        "Landroidx/compose/foundation/ScrollState;" +
                        "Landroidx/compose/ui/graphics/Shape;" +
                        "Landroidx/compose/material3/TextFieldColors;" +
                        "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                        "Landroidx/compose/runtime/Composer;III)V",
        InstanceField = "INSTANCE",
        Defaults      = typeof(SearchBarDefaultsInputFieldDefault))]
    public static partial void SearchBarDefaultsInputField(
        IntPtr      textFieldState,
        IntPtr      searchBarState,
        IFunction1  onSearch,
        IModifier?  modifier,
        IFunction2? placeholder,
        IFunction2? leadingIcon,
        IFunction2? trailingIcon,
        IComposer   composer);

    // androidx.compose.material3.AppBarKt.TopAppBar-cJHQLPU (subtitle
    // overload). 10 user params: title, subtitle, modifier, navigationIcon,
    // actions, titleHorizontalAlignment, expandedHeight, windowInsets,
    // colors, scrollBehavior.
    [ComposeBridge(
        Class     = "androidx/compose/material3/AppBarKt",
        JvmName   = "TopAppBar-cJHQLPU",
        Signature = "(Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
                    "Landroidx/compose/ui/Modifier;Lkotlin/jvm/functions/Function2;" +
                    "Lkotlin/jvm/functions/Function3;Landroidx/compose/ui/Alignment$Horizontal;F" +
                    "Landroidx/compose/foundation/layout/WindowInsets;" +
                    "Landroidx/compose/material3/TopAppBarColors;" +
                    "Landroidx/compose/material3/TopAppBarScrollBehavior;" +
                    "Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(TopAppBarSubtitleDefault))]
    public static partial void TopAppBarWithSubtitle(
        IFunction2  title,
        IFunction2  subtitle,
        IModifier?  modifier,
        IFunction2? navigationIcon,
        IFunction3? actions,
        ITopAppBarScrollBehavior? scrollBehavior,
        int         defaults,
        IComposer   composer);

    // androidx.compose.material3.AppBarKt.{Medium,Large}FlexibleTopAppBar-eXZ4JBQ
    // share the descriptor (trailing `III` because 11 params * 3 bits per
    // $changed slot exceeds the 31 bits in one int). 11 user params:
    // title, modifier, subtitle, navigationIcon, actions,
    // titleHorizontalAlignment, collapsedHeight, expandedHeight,
    // windowInsets, colors, scrollBehavior.
    const string FlexibleTopAppBarSig =
        "(Lkotlin/jvm/functions/Function2;Landroidx/compose/ui/Modifier;" +
        "Lkotlin/jvm/functions/Function2;Lkotlin/jvm/functions/Function2;" +
        "Lkotlin/jvm/functions/Function3;Landroidx/compose/ui/Alignment$Horizontal;FF" +
        "Landroidx/compose/foundation/layout/WindowInsets;" +
        "Landroidx/compose/material3/TopAppBarColors;" +
        "Landroidx/compose/material3/TopAppBarScrollBehavior;" +
        "Landroidx/compose/runtime/Composer;III)V";

    [ComposeBridge(
        Class     = "androidx/compose/material3/AppBarKt",
        JvmName   = "MediumFlexibleTopAppBar-eXZ4JBQ",
        Signature = FlexibleTopAppBarSig,
        Defaults  = typeof(FlexibleTopAppBarDefault))]
    [ComposeFacade]
    public static partial void MediumFlexibleTopAppBar(
        IFunction2  title,
        IModifier?  modifier,
        IFunction2? subtitle,
        IFunction2? navigationIcon,
        IFunction3? actions,
        ITopAppBarScrollBehavior? scrollBehavior,
        int         defaults,
        IComposer   composer);

    [ComposeBridge(
        Class     = "androidx/compose/material3/AppBarKt",
        JvmName   = "LargeFlexibleTopAppBar-eXZ4JBQ",
        Signature = FlexibleTopAppBarSig,
        Defaults  = typeof(FlexibleTopAppBarDefault))]
    [ComposeFacade]
    public static partial void LargeFlexibleTopAppBar(
        IFunction2  title,
        IModifier?  modifier,
        IFunction2? subtitle,
        IFunction2? navigationIcon,
        IFunction3? actions,
        ITopAppBarScrollBehavior? scrollBehavior,
        int         defaults,
        IComposer   composer);

    // androidx.compose.material3.AppBarKt.BottomAppBar-qhFBPw4 (actions +
    // FAB + scrollBehavior — the most flexible of the four BottomAppBar
    // overloads). 9 user params: actions (RowScope-receiver Function3),
    // modifier, floatingActionButton, containerColor, contentColor,
    // tonalElevation, contentPadding, windowInsets, scrollBehavior.
    [ComposeBridge(
        Class     = "androidx/compose/material3/AppBarKt",
        JvmName   = "BottomAppBar-qhFBPw4",
        Signature = "(Lkotlin/jvm/functions/Function3;Landroidx/compose/ui/Modifier;" +
                    "Lkotlin/jvm/functions/Function2;JJF" +
                    "Landroidx/compose/foundation/layout/PaddingValues;" +
                    "Landroidx/compose/foundation/layout/WindowInsets;" +
                    "Landroidx/compose/material3/BottomAppBarScrollBehavior;" +
                    "Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(BottomAppBarDefault))]
    [ComposeFacade(Scope = "Row")]
    public static partial void BottomAppBar(
        IFunction3  actions,
        IModifier?  modifier,
        IFunction2? floatingActionButton,
        IComposer   composer);

    // androidx.compose.material3.AppBarKt.FlexibleBottomAppBar-wBhsO_E.
    // 9 user params: modifier, containerColor, contentColor, contentPadding,
    // horizontalArrangement, expandedHeight, windowInsets, scrollBehavior,
    // content (RowScope-receiver Function3). Bit 8 (content) always provided.
    [ComposeBridge(
        Class     = "androidx/compose/material3/AppBarKt",
        JvmName   = "FlexibleBottomAppBar-wBhsO_E",
        Signature = "(Landroidx/compose/ui/Modifier;JJ" +
                    "Landroidx/compose/foundation/layout/PaddingValues;" +
                    "Landroidx/compose/foundation/layout/Arrangement$Horizontal;F" +
                    "Landroidx/compose/foundation/layout/WindowInsets;" +
                    "Landroidx/compose/material3/BottomAppBarScrollBehavior;" +
                    "Lkotlin/jvm/functions/Function3;" +
                    "Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(FlexibleBottomAppBarDefault))]
    [ComposeFacade(Scope = "Row")]
    public static partial void FlexibleBottomAppBar(
        IModifier? modifier,
        IFunction3 content,
        IComposer  composer);

    // androidx.compose.material3.TabRowKt.{Primary,Secondary}ScrollableTabRow-qhFBPw4
    // (simpler overload of -cx2KkNY without the TabIndicatorScope-aware
    // indicator/divider Function2 wrap — both share the descriptor).
    // 9 user params: selectedTabIndex, modifier, scrollState (optional —
    // null lets Kotlin allocate the default ScrollState), containerColor,
    // contentColor, edgePadding, indicator, divider, tabs.
    const string PrimaryScrollableTabRowSig =
        "(ILandroidx/compose/ui/Modifier;Landroidx/compose/foundation/ScrollState;JJF" +
        "Lkotlin/jvm/functions/Function3;Lkotlin/jvm/functions/Function2;" +
        "Lkotlin/jvm/functions/Function2;Landroidx/compose/runtime/Composer;II)V";

    [ComposeBridge(
        Class     = "androidx/compose/material3/TabRowKt",
        JvmName   = "PrimaryScrollableTabRow-qhFBPw4",
        Signature = PrimaryScrollableTabRowSig,
        Defaults  = typeof(PrimaryScrollableTabRowDefault))]
    public static partial void PrimaryScrollableTabRow(
        int        selectedTabIndex,
        IModifier? modifier,
        IntPtr?    scrollState,
        IFunction2 tabs,
        IComposer  composer);

    [ComposeBridge(
        Class     = "androidx/compose/material3/TabRowKt",
        JvmName   = "SecondaryScrollableTabRow-qhFBPw4",
        Signature = PrimaryScrollableTabRowSig,
        Defaults  = typeof(PrimaryScrollableTabRowDefault))]
    public static partial void SecondaryScrollableTabRow(
        int        selectedTabIndex,
        IModifier? modifier,
        IntPtr?    scrollState,
        IFunction2 tabs,
        IComposer  composer);

    // androidx.compose.material3.TabKt.Tab-bogVsAg (ColumnScope content
    // lambda overload — alternative to Tab-wqdebIU). 8 user params:
    // selected, onClick, modifier, enabled, selectedContentColor,
    // unselectedContentColor, interactionSource, content.
    [ComposeBridge(
        Class     = "androidx/compose/material3/TabKt",
        JvmName   = "Tab-bogVsAg",
        Signature = "(ZLkotlin/jvm/functions/Function0;Landroidx/compose/ui/Modifier;ZJJ" +
                    "Landroidx/compose/foundation/interaction/MutableInteractionSource;" +
                    "Lkotlin/jvm/functions/Function3;Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(TabContentDefault))]
    [ComposeFacade(ClassName = "CustomTab", Scope = "Column")]
    public static partial void TabContent(
        bool       selected,
        IFunction0 onClick,
        IModifier? modifier,
        IFunction3 content,
        IComposer  composer);

    // androidx.compose.material3.SnackbarKt.Snackbar-sDKtq54 (SnackbarData
    // overload — fed by SnackbarHost's content lambda when state has
    // queued data). 9 user params: snackbarData, modifier, actionOnNewLine,
    // shape, containerColor, contentColor, actionColor, actionContentColor,
    // dismissActionContentColor. Bit 0 (snackbarData) always provided —
    // we take the raw JNI handle so SnackbarHost can forward Function3's
    // p0 (a Java.Lang.Object SnackbarData) directly.
    [ComposeBridge(
        Class     = "androidx/compose/material3/SnackbarKt",
        JvmName   = "Snackbar-sDKtq54",
        Signature = "(Landroidx/compose/material3/SnackbarData;" +
                    "Landroidx/compose/ui/Modifier;Z" +
                    "Landroidx/compose/ui/graphics/Shape;JJJJJ" +
                    "Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(SnackbarFromDataDefault))]
    public static partial void SnackbarFromData(
        IntPtr     snackbarData,
        IModifier? modifier,
        IComposer  composer);

    // Phase 8 — wrapper-passthrough facades. These are [ComposeFacade]-only
    // partial methods with hand-written bodies that delegate to a bound
    // binding. They eliminate per-facade boilerplate (ComposableLambda
    // wrapping, modifier handling, $default mask) by routing through the
    // ComposeFacadeGenerator. The wrapper itself is just one explicit
    // call to the binding — no overload resolution magic, every arg
    // hand-picked.

    [ComposeFacade(Defaults = typeof(BoxDefault), Scope = "Box")]
    public static partial void Box(IModifier? modifier, IFunction3 content, int defaults, IComposer composer);

    public static partial void Box(IModifier? modifier, IFunction3 content, int defaults, IComposer composer)
        => BoxKt.Box(
            modifier:                modifier,
            contentAlignment:        null,
            propagateMinConstraints: false,
            content:                 content,
            _composer:               composer,
            p5:                      0,
            _changed:                defaults);

    // Internal forwarder for Column. The public-facing facade
    // (Column.cs) is hand-written because the [ComposeFacade] generator
    // can't surface a typed Arrangement? ctor parameter; it owns the
    // $default mask and forwards the chosen verticalArrangement here.
    internal static void Column(
        IModifier? modifier,
        AndroidX.Compose.Foundation.Layout.Arrangement.IVertical? verticalArrangement,
        IAlignmentHorizontal? horizontalAlignment,
        IFunction3 content,
        int defaults,
        IComposer composer)
        => ColumnKt.Column(
            modifier:            modifier,
            verticalArrangement: verticalArrangement,
            horizontalAlignment: horizontalAlignment,
            content:             content,
            _composer:           composer,
            p5:                  0,
            _changed:            defaults);

    // Internal forwarder for Row — see the Column helper above for why
    // this isn't an auto-generated facade. Row.cs supplies the typed
    // horizontalArrangement / verticalAlignment and matching $default mask.
    internal static void Row(
        IModifier? modifier,
        AndroidX.Compose.Foundation.Layout.Arrangement.IHorizontal? horizontalArrangement,
        IAlignmentVertical? verticalAlignment,
        IFunction3 content,
        int defaults,
        IComposer composer)
        => RowKt.Row(
            modifier:              modifier,
            horizontalArrangement: horizontalArrangement,
            verticalAlignment:     verticalAlignment,
            content:               content,
            _composer:             composer,
            p5:                    0,
            _changed:              defaults);

    // Spacer has no Kotlin $default — the modifier param is required.
    // The wrapper materialises Modifier.Companion when the caller leaves
    // it null so the binding receives a non-null receiver.
    [ComposeFacade]
    public static partial void Spacer(IModifier? modifier, IComposer composer);

    public static partial void Spacer(IModifier? modifier, IComposer composer)
        => SpacerKt.Spacer(
            modifier ?? Java.Lang.Object.GetObject<IModifier>(
                ModifierCompanionInstance(),
                JniHandleOwnership.TransferLocalRef)!,
            composer,
            0);

    [ComposeFacade(Defaults = typeof(CheckboxDefault))]
    public static partial void Checkbox(
        bool                       @checked,
        [Callback(typeof(bool))]
        IFunction1                 onCheckedChange,
        IModifier?                 modifier,
        bool                       enabled  = true,
        AndroidX.Compose.Material3.CheckboxColors? colors = null,
        int                        defaults = 0,
        IComposer                  composer = null!);

    public static partial void Checkbox(bool @checked, IFunction1 onCheckedChange, IModifier? modifier, bool enabled, AndroidX.Compose.Material3.CheckboxColors? colors, int defaults, IComposer composer)
        => CheckboxKt.Checkbox(
            @checked:          @checked,
            onCheckedChange:   onCheckedChange,
            modifier:          modifier,
            enabled:           enabled,
            colors:            colors,
            interactionSource: null,
            _composer:         composer,
            p7:                0,
            _changed:          defaults);

    [ComposeFacade(Defaults = typeof(SwitchDefault))]
    public static partial void Switch(
        bool                       @checked,
        [Callback(typeof(bool))]
        IFunction1                 onCheckedChange,
        IModifier?                 modifier,
        bool                       enabled  = true,
        AndroidX.Compose.Material3.SwitchColors? colors = null,
        int                        defaults = 0,
        IComposer                  composer = null!);

    public static partial void Switch(bool @checked, IFunction1 onCheckedChange, IModifier? modifier, bool enabled, AndroidX.Compose.Material3.SwitchColors? colors, int defaults, IComposer composer)
        => SwitchKt.Switch(
            @checked:          @checked,
            onCheckedChange:   onCheckedChange,
            modifier:          modifier,
            thumbContent:      null,
            enabled:           enabled,
            colors:            colors,
            interactionSource: null,
            _composer:         composer,
            p8:                0,
            _changed:          defaults);

    [ComposeFacade(Defaults = typeof(RadioButtonDefault))]
    public static partial void RadioButton(
        bool        selected,
        IFunction0  onClick,
        IModifier?  modifier,
        bool        enabled  = true,
        int         defaults = 0,
        IComposer   composer = null!);

    public static partial void RadioButton(bool selected, IFunction0 onClick, IModifier? modifier, bool enabled, int defaults, IComposer composer)
        => RadioButtonKt.RadioButton(
            selected:          selected,
            onClick:           onClick,
            modifier:          modifier,
            enabled:           enabled,
            colors:            null,
            interactionSource: null,
            _composer:         composer,
            p7:                0,
            _changed:          defaults);

    [ComposeFacade(Defaults = typeof(SliderDefault))]
    public static partial void Slider(
        float                       value,
        [Callback(typeof(float))]
        IFunction1                  onValueChange,
        IModifier?                  modifier,
        IClosedFloatingPointRange?  valueRange,
        SliderColors?               colors,
        bool                        enabled  = true,
        int                         steps    = 0,
        int                         defaults = 0,
        IComposer                   composer = null!);

    // Slot rename pattern: C# `p5` is the real Kotlin `steps` Int; C# named
    // `steps:` is the JVM `$changed` recomposition int; `_changed` is `$default`.
    // Cross-check with RangeSlider above, which uses the same SliderKt
    // overload (both pass `steps: 0` for $changed and route the user-facing
    // steps through `p5:`).
    public static partial void Slider(float value, IFunction1 onValueChange, IModifier? modifier, IClosedFloatingPointRange? valueRange, SliderColors? colors, bool enabled, int steps, int defaults, IComposer composer)
        => SliderKt.Slider(
            value:                  value,
            onValueChange:          onValueChange,
            modifier:               modifier,
            enabled:                enabled,
            valueRange:             valueRange,
            p5:                     steps,
            onValueChangeFinished:  null,
            colors:                 colors,
            interactionSource:      null,
            _composer:              composer,
            steps:                  0,
            _changed:               defaults);

    // FlowRow / FlowColumn — Phase 8 wrapper-passthrough facades. The
    // simpler 7-Kotlin-param overloads (no FlowRowOverflow / FlowColumnOverflow
    // slot) are bound directly. The Int params `maxItemsInEachRow` /
    // `maxLines` (resp. `maxItemsInEachColumn` / `maxLines`) can't be
    // safely auto-masked from a nullable C# slot, so v1 leaves their
    // $default bits set and passes 0 — Kotlin substitutes Int.MAX_VALUE.
    // The binder rename pattern: the C# `p4` param is the actual Kotlin
    // `maxItemsInEachRow`/`maxItemsInEachColumn` Int, and the C# named
    // `maxItemsInEachRow`/`maxItemsInEachColumn` is the Kotlin `maxLines`
    // Int. The `maxLines` slot in C# is the JVM `$changed` int; `_changed`
    // is `$default`.
    [ComposeFacade(Defaults = typeof(FlowRowDefault), Scope = "Row")]
    public static partial void FlowRow(IModifier? modifier, IFunction3 content, int defaults, IComposer composer);

    public static partial void FlowRow(IModifier? modifier, IFunction3 content, int defaults, IComposer composer)
        => FlowLayoutKt.FlowRow(
            modifier:              modifier,
            horizontalArrangement: null,
            verticalArrangement:   null,
            itemVerticalAlignment: null,
            p4:                    0,
            maxItemsInEachRow:     0,
            content:               content,
            _composer:             composer,
            maxLines:              0,
            _changed:              defaults);

    [ComposeFacade(Defaults = typeof(FlowColumnDefault), Scope = "Column")]
    public static partial void FlowColumn(IModifier? modifier, IFunction3 content, int defaults, IComposer composer);

    public static partial void FlowColumn(IModifier? modifier, IFunction3 content, int defaults, IComposer composer)
        => FlowLayoutKt.FlowColumn(
            modifier:                modifier,
            verticalArrangement:     null,
            horizontalArrangement:   null,
            itemHorizontalAlignment: null,
            p4:                      0,
            maxItemsInEachColumn:    0,
            content:                 content,
            _composer:               composer,
            maxLines:                0,
            _changed:                defaults);

    // WideNavigationRailKt.WideNavigationRailItem-pli-t6k. Bound C# wrapper
    // has misnamed trailing params: `iconPosition` is actually $changed,
    // `_changed` is actually $default. The mid-list `int p7` is the real
    // iconPosition slot (not user-exposed).
    [ComposeFacade(Defaults = typeof(WideNavigationRailItemDefault))]
    public static partial void WideNavigationRailItem(
        bool        selected,
        IFunction0  onClick,
        IFunction2  icon,
        IFunction2? label,
        IModifier?  modifier,
        bool        enabled  = true,
        int         defaults = 0,
        IComposer   composer = null!);

    public static partial void WideNavigationRailItem(bool selected, IFunction0 onClick, IFunction2 icon, IFunction2? label, IModifier? modifier, bool enabled, int defaults, IComposer composer)
        => AndroidX.Compose.Material3.WideNavigationRailKt.WideNavigationRailItem(
            selected:          selected,
            onClick:           onClick,
            icon:              icon,
            label:             label,
            railExpanded:      false,
            modifier:          modifier,
            enabled:           enabled,
            p7:                0,
            colors:            null,
            interactionSource: null,
            _composer:         composer,
            iconPosition:      0,
            _changed:          defaults);

    [ComposeFacade(Defaults = typeof(TriStateCheckboxDefault))]
    public static partial void TriStateCheckbox(
        ToggleableState state,
        IFunction0  onClick,
        IModifier?  modifier,
        bool        enabled  = true,
        int         defaults = 0,
        IComposer   composer = null!);

    public static partial void TriStateCheckbox(ToggleableState state, IFunction0 onClick, IModifier? modifier, bool enabled, int defaults, IComposer composer)
        => AndroidX.Compose.Material3.CheckboxKt.TriStateCheckbox(
            state:             state,
            onClick:           onClick,
            modifier:          modifier,
            enabled:           enabled,
            colors:            null,
            interactionSource: null,
            _composer:         composer,
            p7:                0,
            _changed:          defaults);

    // Phase 8 wrapper-passthrough for the segmented-button row
    // containers. The generator's IndexedChildren=true emits the
    // PushRow + per-child SetIndex publishing required by the child
    // SegmentedButton's ItemShape computation. Inner call is the bound
    // binding directly (the binder exposes the *Kt static for these
    // unmangled overloads), so the facade must supply the $default mask
    // explicitly via the trailing `int defaults` slot.
    [ComposeFacade(Defaults = typeof(SegmentedButtonRowDefault), Scope = "Other", IndexedChildren = true)]
    public static partial void SingleChoiceSegmentedButtonRow(
        IModifier? modifier,
        IFunction3 content,
        int        defaults,
        IComposer  composer);

    public static partial void SingleChoiceSegmentedButtonRow(IModifier? modifier, IFunction3 content, int defaults, IComposer composer)
        => SegmentedButtonKt.SingleChoiceSegmentedButtonRow(
            modifier:  modifier,
            space:     0f,
            content:   content,
            _composer: composer,
            p4:        0,
            _changed:  defaults);

    [ComposeFacade(Defaults = typeof(SegmentedButtonRowDefault), Scope = "Other", IndexedChildren = true)]
    public static partial void MultiChoiceSegmentedButtonRow(
        IModifier? modifier,
        IFunction3 content,
        int        defaults,
        IComposer  composer);

    public static partial void MultiChoiceSegmentedButtonRow(IModifier? modifier, IFunction3 content, int defaults, IComposer composer)
        => SegmentedButtonKt.MultiChoiceSegmentedButtonRow(
            modifier:  modifier,
            space:     0f,
            content:   content,
            _composer: composer,
            p4:        0,
            _changed:  defaults);

    // Phase 8 wrapper-passthrough for the scrollable tab rows. The
    // existing 5-param [ComposeBridge] overloads above (search for
    // `PrimaryScrollableTabRow-qhFBPw4`) expose an `IntPtr? scrollState`
    // slot the facade always passes as null — the bound binding's
    // auto-mask handles the corresponding $default bit. These 4-param
    // overloads forward to those bridges; the generator emits the
    // facade class as a same-name overload selection at compile time
    // (no `ClassName` redirect needed).
    [ComposeFacade]
    public static partial void PrimaryScrollableTabRow(
        int        selectedTabIndex,
        IModifier? modifier,
        IFunction2 tabs,
        IComposer  composer);

    public static partial void PrimaryScrollableTabRow(int selectedTabIndex, IModifier? modifier, IFunction2 tabs, IComposer composer)
        => PrimaryScrollableTabRow(
            selectedTabIndex: selectedTabIndex,
            modifier:         modifier,
            scrollState:      null,
            tabs:             tabs,
            composer:         composer);

    [ComposeFacade]
    public static partial void SecondaryScrollableTabRow(
        int        selectedTabIndex,
        IModifier? modifier,
        IFunction2 tabs,
        IComposer  composer);

    public static partial void SecondaryScrollableTabRow(int selectedTabIndex, IModifier? modifier, IFunction2 tabs, IComposer composer)
        => SecondaryScrollableTabRow(
            selectedTabIndex: selectedTabIndex,
            modifier:         modifier,
            scrollState:      null,
            tabs:             tabs,
            composer:         composer);

    // androidx.compose.foundation.text.selection.SelectionContainerKt.SelectionContainer —
    // `(modifier=Modifier, content)`. The binder exposes the Kt class
    // and the 5-arg `_composer:/p3:/_changed:` overload directly, so
    // this is a Phase 8 wrapper-passthrough. The auto-mask clears
    // SelectionContainerDefault.Modifier when the caller supplies one.
    [ComposeFacade(Defaults = typeof(SelectionContainerDefault))]
    public static partial void SelectionContainer(
        IModifier? modifier,
        IFunction2 content,
        int        defaults,
        IComposer  composer);

    public static partial void SelectionContainer(IModifier? modifier, IFunction2 content, int defaults, IComposer composer)
        => AndroidX.Compose.Foundation.Text.Selection.SelectionContainerKt.SelectionContainer(
            modifier:  modifier,
            content:   content,
            _composer: composer,
            p3:        0,
            _changed:  defaults);

    // androidx.compose.foundation.text.selection.SelectionContainerKt.DisableSelection —
    // `(content)`. No optional params → no `$default` slot, so no
    // `Defaults` enum and no `int defaults` user param (mirrors the
    // Spacer wrapper above). Nests inside a SelectionContainer to opt
    // a subtree out of selection.
    [ComposeFacade]
    public static partial void DisableSelection(
        IFunction2 content,
        IComposer  composer);

    public static partial void DisableSelection(IFunction2 content, IComposer composer)
        => AndroidX.Compose.Foundation.Text.Selection.SelectionContainerKt.DisableSelection(
            content:   content,
            _composer: composer,
            _changed:  0);

    // Tracking dotnet/android-libraries#1444: the binder strips every
    // @Composable static from `Xamarin.AndroidX.Navigation.Compose`'s
    // *Kt wrapper classes (NavHostKt, NavGraphBuilderKt,
    // NavHostControllerKt, ...). We bridge the three we need —
    // NavHost, NavGraphBuilder.composable, and rememberNavController.

    // androidx.navigation.compose.NavHostControllerKt.rememberNavController.
    // Kotlin signature: `rememberNavController(vararg navigators: Navigator<*>): NavHostController`.
    // Hand-written rather than [ComposeBridge] because the bridge
    // generator has no path for "construct an empty Java array of
    // Navigator and pass it as the varargs argument". Empty
    // `Navigator[]` is cached as a global ref so we don't allocate one
    // per recomposition.
    static IntPtr s_rememberNavController_class;
    static IntPtr s_rememberNavController_method;
    static IntPtr s_rememberNavController_emptyArray;

    internal static unsafe AndroidX.Navigation.NavHostController RememberNavController(IComposer composer)
    {
        if (s_rememberNavController_method == IntPtr.Zero)
        {
            s_rememberNavController_class = JNIEnv.FindClass("androidx/navigation/compose/NavHostControllerKt");
            s_rememberNavController_method = JNIEnv.GetStaticMethodID(
                s_rememberNavController_class,
                "rememberNavController",
                "([Landroidx/navigation/Navigator;Landroidx/compose/runtime/Composer;I)Landroidx/navigation/NavHostController;");

            IntPtr navigatorClass = JNIEnv.FindClass("androidx/navigation/Navigator");
            IntPtr local = JNIEnv.NewObjectArray(0, navigatorClass, IntPtr.Zero);
            s_rememberNavController_emptyArray = JNIEnv.NewGlobalRef(local);
            JNIEnv.DeleteLocalRef(local);
        }

        try
        {
            JValue* args = stackalloc JValue[3];
            args[0] = new JValue(s_rememberNavController_emptyArray);
            args[1] = new JValue(((Java.Lang.Object)composer).Handle);
            args[2] = new JValue(0); // $changed — Compose substitutes its own
            IntPtr handle = JNIEnv.CallStaticObjectMethod(
                s_rememberNavController_class, s_rememberNavController_method, args);
            return Java.Lang.Object.GetObject<AndroidX.Navigation.NavHostController>(
                handle, JniHandleOwnership.TransferLocalRef)!;
        }
        finally
        {
            GC.KeepAlive(composer);
        }
    }

    // androidx.navigation.compose.NavHostKt.NavHost (string-route overload).
    // Kotlin: `NavHost(navController, startDestination, modifier=Modifier,
    // route=null, builder)`. Trailing `II` on the JNI sig = $changed +
    // $default. The auto-mask drives the $default bits — modifier and
    // route are nullable so the generator clears their bits only when
    // the caller supplies a non-null value.
    [ComposeBridge(
        Class     = "androidx/navigation/compose/NavHostKt",
        JvmName   = "NavHost",
        Signature = "(Landroidx/navigation/NavHostController;Ljava/lang/String;" +
                    "Landroidx/compose/ui/Modifier;Ljava/lang/String;" +
                    "Lkotlin/jvm/functions/Function1;" +
                    "Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(NavHostDefault))]
    internal static partial void NavHost(
        AndroidX.Navigation.NavHostController navController,
        string                                startDestination,
        IModifier?                            modifier,
        string?                               route,
        IFunction1                            builder,
        IComposer                             composer);

    // androidx.navigation.compose.NavGraphBuilderKt.composable$default
    // (string-route overload). Kotlin extension on NavGraphBuilder:
    // `NavGraphBuilder.composable(route, arguments=emptyList(),
    // deepLinks=emptyList(), content)`. NOT @Composable itself —
    // invoked synchronously inside NavHost's builder lambda. The
    // trailing `IL...;` on the JNI sig is `$default` + the synthetic
    // marker the bridge generator fills with `IntPtr.Zero` for any
    // `*$default` overload. The first IntPtr (navGraphBuilder) is the
    // extension receiver; route/content are required (suppressed bits);
    // arguments and deepLinks are nullable lists with empty defaults.
    [ComposeBridge(
        Class     = "androidx/navigation/compose/NavGraphBuilderKt",
        JvmName   = "composable$default",
        Signature = "(Landroidx/navigation/NavGraphBuilder;Ljava/lang/String;" +
                    "Ljava/util/List;Ljava/util/List;" +
                    "Lkotlin/jvm/functions/Function3;ILjava/lang/Object;)V",
        Defaults  = typeof(NavComposableDefault))]
    internal static partial void NavGraphBuilderComposable(
        IntPtr           navGraphBuilder,
        string           route,
        Java.Lang.Object? arguments,
        Java.Lang.Object? deepLinks,
        IFunction3       content);

    static IntPtr s_modifierPointerInput_class;
    static IntPtr s_modifierPointerInput_method;

    // androidx.compose.ui.input.pointer.SuspendingPointerInputFilterKt
    //     .pointerInput(Modifier, Object key1, PointerInputEventHandler): Modifier
    //
    // Hand-written because the [ComposeBridge] generator doesn't know
    // about non-bound interface types like IPointerInputEventHandler.
    // Returns a fresh local ref to the chained Modifier; the caller
    // (Modifier.Append's lambda) DeleteLocalRefs the previous one.
    internal static unsafe IntPtr ModifierPointerInput(
        IntPtr modifier, IntPtr key, IntPtr handler)
    {
        if (s_modifierPointerInput_method == IntPtr.Zero)
        {
            s_modifierPointerInput_class = JNIEnv.FindClass(
                "androidx/compose/ui/input/pointer/SuspendingPointerInputFilterKt");
            s_modifierPointerInput_method = JNIEnv.GetStaticMethodID(
                s_modifierPointerInput_class,
                "pointerInput",
                "(Landroidx/compose/ui/Modifier;Ljava/lang/Object;" +
                "Landroidx/compose/ui/input/pointer/PointerInputEventHandler;)" +
                "Landroidx/compose/ui/Modifier;");
        }

        JValue* args = stackalloc JValue[3];
        args[0] = new JValue(modifier);
        args[1] = new JValue(key);
        args[2] = new JValue(handler);
        return JNIEnv.CallStaticObjectMethod(
            s_modifierPointerInput_class, s_modifierPointerInput_method, args);
    }

    static IntPtr s_pointerInputHandler_class;
    static IntPtr s_pointerInputHandler_ctor;

    // net.compose.PointerInputEventHandlerImpl.<init>(Function2)
    //
    // Allocates the Java-side helper (shipped via <AndroidJavaSource>)
    // that implements the bound but otherwise-unreachable
    // PointerInputEventHandler interface by forwarding invoke(scope,
    // continuation) to the supplied Function2 handle.
    //
    // Returns a fresh local ref to the new PointerInputEventHandlerImpl
    // instance; caller owns it (typically pass straight through to
    // ModifierPointerInput, whose CallStaticObjectMethod frame pop
    // reclaims it).
    internal static unsafe IntPtr NewPointerInputEventHandler(IntPtr function2)
    {
        if (s_pointerInputHandler_ctor == IntPtr.Zero)
        {
            s_pointerInputHandler_class = JNIEnv.FindClass(
                "net/compose/PointerInputEventHandlerImpl");
            s_pointerInputHandler_ctor = JNIEnv.GetMethodID(
                s_pointerInputHandler_class,
                "<init>",
                "(Lkotlin/jvm/functions/Function2;)V");
        }

        JValue* args = stackalloc JValue[1];
        args[0] = new JValue(function2);
        return JNIEnv.NewObject(
            s_pointerInputHandler_class, s_pointerInputHandler_ctor, args);
    }

    // androidx.lifecycle.viewmodel.compose.LocalViewModelStoreOwner.current
    // — `@Composable @ReadOnlyComposable` getter on a Kotlin object
    // singleton. We need this to read the current ViewModelStoreOwner
    // (NavBackStackEntry inside a NavHost destination, or the
    // ComponentActivity at the root) so ComposeExtensions.ViewModel<T> can
    // hand the right owner to ViewModelProvider. The binding exposes
    // the singleton (LocalViewModelStoreOwner.Instance) but not the
    // @Composable getter, so we go through InstanceField + JNI.
    // Returns null when no owner is installed; the caller throws.
    [ComposeBridge(
        Class         = "androidx/lifecycle/viewmodel/compose/LocalViewModelStoreOwner",
        JvmName       = "getCurrent",
        Signature     = "(Landroidx/compose/runtime/Composer;I)Landroidx/lifecycle/ViewModelStoreOwner;",
        InstanceField = "INSTANCE")]
    public static partial IntPtr LocalViewModelStoreOwnerCurrent(IComposer composer);

    // androidx.activity.compose.BackHandlerKt.BackHandler — Kotlin
    // signature `BackHandler(enabled: Boolean = true, onBack: () -> Unit)`.
    // Bridged via raw JNI because `Xamarin.AndroidX.Activity.Compose`
    // ships an empty stub DLL (no types bound). The trailing `II)V` on
    // the JNI sig is `$changed, $default` — the primary @Composable
    // overload, not a `*$default` synthetic. Compose's BackHandler
    // wraps `onBack` in `rememberUpdatedState` so identity churn from
    // a fresh ComposableLambda0 per recomposition is safe; the
    // OnBackPressedDispatcher registration is keyed on lifecycle owner.
    [ComposeBridge(
        Class     = "androidx/activity/compose/BackHandlerKt",
        JvmName   = "BackHandler",
        Signature = "(ZLkotlin/jvm/functions/Function0;" +
                    "Landroidx/compose/runtime/Composer;II)V",
        Defaults  = typeof(BackHandlerDefault))]
    [ComposeFacade]
    public static partial void BackHandler(
        IFunction0 onBack,
        bool       enabled,
        IComposer  composer);
}

