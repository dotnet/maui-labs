using Android.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// C# wrapper around <c>androidx.compose.ui.text.style.TextDecoration</c>.
/// Compose's <c>TextDecoration</c> is a real Kotlin class (not a value
/// class), with companion constants for <c>None</c>, <c>Underline</c>,
/// and <c>LineThrough</c>. Same plumbing story as <see cref="FontWeight"/>
/// — <c>ui-text-android</c> ships as a Java-library-only stub today, so
/// we resolve via JNI (boilerplate emitted by
/// <c>ComposeCompanionGenerator</c>). Will swap to bound
/// <c>AndroidX.Compose.UI.Text.Style.TextDecoration</c> once
/// <see href="https://github.com/dotnet/android-libraries/issues/1439"/>
/// ships.
/// </summary>
[ComposeCompanion("androidx/compose/ui/text/style/TextDecoration")]
public sealed partial class TextDecoration : Java.Lang.Object
{
    TextDecoration(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer) { }

    /// <summary><c>TextDecoration.None</c>.</summary>
    [ComposeCompanionGetter("getNone")]
    public static partial TextDecoration None { get; }

    /// <summary><c>TextDecoration.Underline</c>.</summary>
    [ComposeCompanionGetter("getUnderline")]
    public static partial TextDecoration Underline { get; }

    /// <summary><c>TextDecoration.LineThrough</c>.</summary>
    [ComposeCompanionGetter("getLineThrough")]
    public static partial TextDecoration LineThrough { get; }
}
