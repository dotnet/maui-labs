using AndroidX.Compose.Animation;
using AndroidX.Compose.Animation.Core;

namespace AndroidX.Compose;

/// <summary>
/// Factory methods that mirror Kotlin's top-level
/// <c>EnterTransition</c> / <c>ExitTransition</c> builders from
/// <c>androidx.compose.animation</c> — <c>fadeIn()</c>,
/// <c>fadeOut()</c>, <c>scaleIn()</c>, <c>scaleOut()</c>, and the
/// <c>slideIn*</c> / <c>slideOut*</c> family. Pass the returned values
/// to <see cref="AnimatedVisibility.Enter"/> /
/// <see cref="AnimatedVisibility.Exit"/> (and, in a future release, to
/// <c>AnimatedContent</c>) to override the default fade-plus-expand
/// transitions Compose chooses on your behalf.
/// </summary>
/// <remarks>
/// <para>
/// Every factory takes an optional <see cref="IFiniteAnimationSpec"/>
/// (<see langword="null"/> uses
/// <c>spring(stiffness = Spring.StiffnessMediumLow)</c>, matching
/// Kotlin's default). Build a custom spec via
/// <see cref="AnimationSpecKt.Spring(float, float, Java.Lang.Object?)"/>,
/// <see cref="AnimationSpecKt.Tween(int, int, IEasing?)"/>, etc.
/// </para>
/// <para>
/// The <c>expandIn</c>, <c>expandHorizontally</c>, <c>expandVertically</c>,
/// <c>shrinkOut</c>, <c>shrinkHorizontally</c>, and <c>shrinkVertically</c>
/// factories aren't surfaced yet — they need
/// <see cref="AndroidX.Compose.UI.IAlignment"/> singletons that aren't
/// exposed by the v1 wrappers. Use the bound
/// <see cref="EnterExitTransitionKt"/> directly if you need them.
/// </para>
/// </remarks>
public static class Transitions
{
    static IFiniteAnimationSpec DefaultSpec() =>
        AnimationSpecKt.Spring(Spring.DampingRatioNoBouncy, Spring.StiffnessMediumLow, null);

    /// <summary>
    /// Fades the content in from <paramref name="initialAlpha"/> (default
    /// <c>0f</c> = fully transparent) to its final opacity. Mirrors Kotlin
    /// <see cref="EnterExitTransitionKt.FadeIn(IFiniteAnimationSpec, float)"/>.
    /// </summary>
    /// <param name="initialAlpha">The starting alpha, in <c>[0f, 1f]</c>.</param>
    /// <param name="animationSpec">Optional spec; <see langword="null"/> uses
    /// <c>spring(stiffness = StiffnessMediumLow)</c>.</param>
    public static EnterTransition FadeIn(float initialAlpha = 0f, IFiniteAnimationSpec? animationSpec = null) =>
        EnterExitTransitionKt.FadeIn(animationSpec ?? DefaultSpec(), initialAlpha);

    /// <summary>
    /// Fades the content out from its current opacity to
    /// <paramref name="targetAlpha"/> (default <c>0f</c>). Mirrors Kotlin
    /// <see cref="EnterExitTransitionKt.FadeOut(IFiniteAnimationSpec, float)"/>.
    /// </summary>
    /// <param name="targetAlpha">The ending alpha, in <c>[0f, 1f]</c>.</param>
    /// <param name="animationSpec">Optional spec; <see langword="null"/> uses
    /// <c>spring(stiffness = StiffnessMediumLow)</c>.</param>
    public static ExitTransition FadeOut(float targetAlpha = 0f, IFiniteAnimationSpec? animationSpec = null) =>
        EnterExitTransitionKt.FadeOut(animationSpec ?? DefaultSpec(), targetAlpha);

    /// <summary>
    /// Scales the content in from <paramref name="initialScale"/> to
    /// <c>1f</c>, anchored at the layout center. Mirrors Kotlin
    /// <see cref="EnterExitTransitionKt.ScaleIn(IFiniteAnimationSpec, float, long)"/>.
    /// </summary>
    /// <param name="initialScale">The starting scale factor.</param>
    /// <param name="animationSpec">Optional spec; <see langword="null"/> uses
    /// <c>spring(stiffness = StiffnessMediumLow)</c>.</param>
    public static EnterTransition ScaleIn(float initialScale = 0f, IFiniteAnimationSpec? animationSpec = null) =>
        EnterExitTransitionKt.ScaleIn(animationSpec ?? DefaultSpec(), initialScale, TransformOrigin.Center);

    /// <summary>
    /// Scales the content out from <c>1f</c> to
    /// <paramref name="targetScale"/>, anchored at the layout center.
    /// Mirrors Kotlin
    /// <see cref="EnterExitTransitionKt.ScaleOut(IFiniteAnimationSpec, float, long)"/>.
    /// </summary>
    /// <param name="targetScale">The ending scale factor.</param>
    /// <param name="animationSpec">Optional spec; <see langword="null"/> uses
    /// <c>spring(stiffness = StiffnessMediumLow)</c>.</param>
    public static ExitTransition ScaleOut(float targetScale = 0f, IFiniteAnimationSpec? animationSpec = null) =>
        EnterExitTransitionKt.ScaleOut(animationSpec ?? DefaultSpec(), targetScale, TransformOrigin.Center);

    /// <summary>
    /// Slides the content in horizontally from <paramref name="initialOffsetX"/>
    /// applied to its measured width — the lambda receives the container's
    /// width in pixels and returns the starting X offset. Kotlin's default
    /// of <c>{ -it / 2 }</c> (half a width to the left) is matched by
    /// passing <c>width =&gt; -width / 2</c>. Mirrors Kotlin
    /// <c>slideInHorizontally(animationSpec, initialOffsetX)</c>.
    /// </summary>
    /// <param name="initialOffsetX">Maps the container's width (px) to the
    /// starting horizontal offset (px). Defaults to half a width to the
    /// left.</param>
    /// <param name="animationSpec">Optional spec; <see langword="null"/> uses
    /// <c>spring(stiffness = StiffnessMediumLow, visibilityThreshold = IntOffset.VisibilityThreshold)</c>
    /// — the binding doesn't expose the offset-typed default, so the same
    /// float-typed spring is substituted.</param>
    public static EnterTransition SlideInHorizontally(Func<int, int>? initialOffsetX = null, IFiniteAnimationSpec? animationSpec = null) =>
        EnterExitTransitionKt.SlideInHorizontally(animationSpec ?? DefaultSpec(), new IntCallback(initialOffsetX ?? (w => -w / 2)));

    /// <summary>
    /// Slides the content in vertically from <paramref name="initialOffsetY"/>
    /// applied to its measured height. Mirrors Kotlin
    /// <c>slideInVertically(animationSpec, initialOffsetY)</c>.
    /// </summary>
    /// <param name="initialOffsetY">Maps the container's height (px) to the
    /// starting vertical offset (px). Defaults to half a height upward.</param>
    /// <param name="animationSpec">Optional spec; see
    /// <see cref="SlideInHorizontally"/>.</param>
    public static EnterTransition SlideInVertically(Func<int, int>? initialOffsetY = null, IFiniteAnimationSpec? animationSpec = null) =>
        EnterExitTransitionKt.SlideInVertically(animationSpec ?? DefaultSpec(), new IntCallback(initialOffsetY ?? (h => -h / 2)));

    /// <summary>
    /// Slides the content out horizontally to <paramref name="targetOffsetX"/>
    /// applied to its measured width. Mirrors Kotlin
    /// <c>slideOutHorizontally(animationSpec, targetOffsetX)</c>.
    /// </summary>
    /// <param name="targetOffsetX">Maps the container's width (px) to the
    /// ending horizontal offset (px). Defaults to half a width to the
    /// left.</param>
    /// <param name="animationSpec">Optional spec; see
    /// <see cref="SlideInHorizontally"/>.</param>
    public static ExitTransition SlideOutHorizontally(Func<int, int>? targetOffsetX = null, IFiniteAnimationSpec? animationSpec = null) =>
        EnterExitTransitionKt.SlideOutHorizontally(animationSpec ?? DefaultSpec(), new IntCallback(targetOffsetX ?? (w => -w / 2)));

    /// <summary>
    /// Slides the content out vertically to <paramref name="targetOffsetY"/>
    /// applied to its measured height. Mirrors Kotlin
    /// <c>slideOutVertically(animationSpec, targetOffsetY)</c>.
    /// </summary>
    /// <param name="targetOffsetY">Maps the container's height (px) to the
    /// ending vertical offset (px). Defaults to half a height upward.</param>
    /// <param name="animationSpec">Optional spec; see
    /// <see cref="SlideInHorizontally"/>.</param>
    public static ExitTransition SlideOutVertically(Func<int, int>? targetOffsetY = null, IFiniteAnimationSpec? animationSpec = null) =>
        EnterExitTransitionKt.SlideOutVertically(animationSpec ?? DefaultSpec(), new IntCallback(targetOffsetY ?? (h => -h / 2)));
}
