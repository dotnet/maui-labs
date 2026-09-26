#nullable enable
using System;
using Comet;
using Comet.Backend;
using Microsoft.Maui;

namespace CometSamples.BaristaNotes.Styles;

public static class BaristaSafeAreaLayout
{
    public const float HeaderContentHeight = CoffeeSpacing.HeaderHeight;
    public const float PickerHeaderContentHeight = 64f;
    public const float HeaderVerticalPadding = 14f;
    public const float PickerHeaderVerticalPadding = 4f;

    public static Thickness GetInsets(View view) =>
        GetInsets(view, applyInsets: true);

    public static Thickness GetInsets(View view, bool applyInsets)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (!applyInsets)
            return Thickness.Zero;

        var safe = view.GetWindowMetrics().SafeAreaDp.Value;
        return new Thickness(
            Normalize(safe.Left),
#if ANDROID
            0,
#else
            Normalize(safe.Top),
#endif
            Normalize(safe.Right),
            Normalize(safe.Bottom));
    }

#if ANDROID
    public static double AndroidStatusBarHeight()
    {
        if (Android.OS.Build.VERSION.SdkInt < Android.OS.BuildVersionCodes.VanillaIceCream)
            return 0;

        var resources = Android.App.Application.Context.Resources;
        var identifier = resources?.GetIdentifier("status_bar_height", "dimen", "android") ?? 0;
        if (identifier <= 0 || resources is null)
            return 0;
        var pixels = resources.GetDimensionPixelSize(identifier);
        var density = resources.DisplayMetrics?.Density ?? 1;
        return pixels / density;
    }
#endif

    public static float GetTopInset(View view) => (float)GetInsets(view).Top;

    public static float GetTopInset(View view, bool applyInset) =>
        (float)GetInsets(view, applyInset).Top;

    public static Thickness HeaderPadding(Thickness safeArea) =>
        new(
            CoffeeSpacing.M + Normalize(safeArea.Left),
            HeaderVerticalPadding + Normalize(safeArea.Top),
            CoffeeSpacing.M + Normalize(safeArea.Right),
            HeaderVerticalPadding);

    public static Thickness HeaderPadding(float topInset) =>
        HeaderPadding(new Thickness(0, Normalize(topInset), 0, 0));

    public static double HeaderMinimumHeight()
    {
#if ANDROID
        return HeaderMinimumHeight(AndroidStatusBarHeight());
#else
        return HeaderMinimumHeight(0);
#endif
    }

    // The minimum belongs to the whole top region. Android reserves its status
    // strip outside the header; Apple includes the inset in the header padding.
    public static double HeaderMinimumHeight(double reservedTopInset) =>
        Math.Max(0, HeaderContentHeight - Normalize(reservedTopInset));

    public static Thickness PickerHeaderPadding(Thickness safeArea) =>
        new(
            CoffeeSpacing.S + Normalize(safeArea.Left),
            PickerHeaderVerticalPadding + Normalize(safeArea.Top),
            CoffeeSpacing.S + Normalize(safeArea.Right),
            PickerHeaderVerticalPadding);

    public static Thickness PickerHeaderPadding(float topInset) =>
        PickerHeaderPadding(new Thickness(0, Normalize(topInset), 0, 0));

    public static Thickness ActionPadding(Thickness safeArea, double horizontal = CoffeeSpacing.S) =>
        new(
            horizontal + Normalize(safeArea.Left),
            CoffeeSpacing.ActionRowTopPadding,
            horizontal + Normalize(safeArea.Right),
            // Preserve the source spacing, but never place content inside the native bottom guide.
            Math.Max(CoffeeSpacing.ActionRowBottomPadding, Normalize(safeArea.Bottom)));

    public static Thickness ActionPadding(View view, double horizontal = CoffeeSpacing.S) =>
        ActionPadding(GetInsets(view), horizontal);

    static double Normalize(double inset) => Math.Max(0, inset);
}
