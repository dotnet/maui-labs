using Android.Runtime;
using AndroidX.Compose.UI.Text;
using AndroidX.Compose.UI.Text.Input;

namespace AndroidX.Compose;

// JNI bridge for the one piece of androidx.compose.ui.text.input.TextFieldValue
// that the binder strips: the constructor.
//
// Everything else — Text/Selection/Composition/AnnotatedString properties,
// Copy(text, selection, composition) overloads, GetSelectedText /
// GetTextAfterSelection / GetTextBeforeSelection extensions — is exposed
// directly by Xamarin.AndroidX.Compose.UI.Text.Android.dll. Don't wrap them.
//
// The Kotlin primary ctor is stripped because its `selection: TextRange`
// parameter is a @JvmInline value class wrapping Long; the JVM signature
// becomes (String, long, TextRange?, …) which the binder declines to
// surface as a managed ctor (it would need to know how to lower a managed
// `long`/`TextRange?` pair back into the inline-class boundary).
//
//   androidx.compose.ui.text.input.TextFieldValue
//     <init>(Ljava/lang/String;JLandroidx/compose/ui/text/TextRange;
//            ILkotlin/jvm/internal/DefaultConstructorMarker;)V
//                                                       ^ $default mask
//                                                        bit 0 = text
//                                                        bit 1 = selection
//                                                        bit 2 = composition
//
// The `TextRange` parameter at slot 2 is the *boxed* TextRange object
// (composition is `TextRange?` in Kotlin so it's the reference form, not
// the unboxed `long`). Callers pass the bound TextRange directly when
// supplying a composition; otherwise we set bit 2 of $default.
internal static partial class ComposeBridges
{
    static IntPtr s_tfv_class;
    static IntPtr s_tfv_ctor;

    static void EnsureTextFieldValueClass()
    {
        if (s_tfv_class != IntPtr.Zero) return;
        s_tfv_class = JNIEnv.FindClass("androidx/compose/ui/text/input/TextFieldValue");
        s_tfv_ctor = JNIEnv.GetMethodID(s_tfv_class, "<init>",
            "(Ljava/lang/String;JLandroidx/compose/ui/text/TextRange;ILkotlin/jvm/internal/DefaultConstructorMarker;)V");
    }

    // Construct a Kotlin TextFieldValue and return the bound binding wrapper.
    // Always invokes the synthetic default-mask ctor so we can pass null for
    // composition without knowing which non-synthetic overloads exist.
    internal static unsafe TextFieldValue NewTextFieldValueImpl(string text, long selection, TextRange? composition)
    {
        EnsureTextFieldValueClass();

        IntPtr textRef = JNIEnv.NewString(text);
        int defaults = composition is null ? (1 << 2) : 0;
        try
        {
            JValue* args = stackalloc JValue[5];
            args[0] = new JValue(textRef);
            args[1] = new JValue(selection);
            args[2] = new JValue(composition is null ? IntPtr.Zero : ((Java.Lang.Object)composition).Handle);
            args[3] = new JValue(defaults);
            args[4] = new JValue(IntPtr.Zero);
            IntPtr peer = JNIEnv.NewObject(s_tfv_class, s_tfv_ctor, args);
            return Java.Lang.Object.GetObject<TextFieldValue>(peer, JniHandleOwnership.TransferLocalRef)!;
        }
        finally
        {
            JNIEnv.DeleteLocalRef(textRef);
            GC.KeepAlive(composition);
        }
    }
}
