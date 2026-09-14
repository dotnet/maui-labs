using Android.Runtime;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// <c>Function1&lt;String, Unit&gt;</c> stub that ignores its argument
/// and returns <c>null</c>. Supplied to
/// <c>SearchBarDefaults.InputField.onSearch</c> when the C# caller did
/// not provide an <c>onSearch</c> callback — the Kotlin API does not
/// tolerate a null callback (it invokes the lambda when the user
/// presses the IME Search action). Used as a singleton to avoid
/// allocating one per render.
/// </summary>
[Register("net/compose/NoOpSearchCallback")]
internal sealed class NoOpSearchCallback : Java.Lang.Object, IFunction1
{
    public static readonly NoOpSearchCallback Instance = new();

    public Java.Lang.Object? Invoke(Java.Lang.Object? p0) => null;
}
