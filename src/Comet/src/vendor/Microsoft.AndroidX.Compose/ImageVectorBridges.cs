using Android.Runtime;
using AndroidX.Compose.UI.Graphics.Vector;

namespace AndroidX.Compose;

/// <summary>Material vectors that are not part of the Compose core icon set.</summary>
public static class MaterialIconVectors
{
    /// <summary>The official Material <c>local_cafe</c> vector.</summary>
    public static ImageVector LocalCafe => ComposeBridges.LocalCafeImageVector();
}

internal static partial class ComposeBridges
{
    const string ImageVectorBuilderCtorSignature =
        "(Ljava/lang/String;FFFFJIZILkotlin/jvm/internal/DefaultConstructorMarker;)V";
    const string ImageVectorAddPathSignature =
        "(Ljava/util/List;ILjava/lang/String;Landroidx/compose/ui/graphics/Brush;" +
        "FLandroidx/compose/ui/graphics/Brush;FFIIFFFF)" +
        "Landroidx/compose/ui/graphics/vector/ImageVector$Builder;";

    static IntPtr s_imageVectorBuilderClass;
    static IntPtr s_imageVectorBuilderCtor;
    static IntPtr s_imageVectorAddPath;
    static ImageVector? s_localCafe;

    internal static ImageVector LocalCafeImageVector()
        => s_localCafe ??= BuildLocalCafeImageVector();

    static unsafe ImageVector BuildLocalCafeImageVector()
    {
        if (s_imageVectorBuilderCtor == IntPtr.Zero)
        {
            s_imageVectorBuilderClass = JNIEnv.FindClass(
                "androidx/compose/ui/graphics/vector/ImageVector$Builder");
            s_imageVectorBuilderCtor = JNIEnv.GetMethodID(
                s_imageVectorBuilderClass, "<init>", ImageVectorBuilderCtorSignature);
            s_imageVectorAddPath = JNIEnv.GetMethodID(
                s_imageVectorBuilderClass, "addPath-oIyEayM", ImageVectorAddPathSignature);
        }

        var vectorName = JNIEnv.NewString("LocalCafe");
        IntPtr builderHandle;
        try
        {
            JValue* args = stackalloc JValue[10];
            args[0] = new JValue(vectorName);
            args[1] = new JValue(24f);
            args[2] = new JValue(24f);
            args[3] = new JValue(24f);
            args[4] = new JValue(24f);
            args[5] = new JValue(0L);
            args[6] = new JValue(0);
            args[7] = new JValue(false);
            args[8] = new JValue(224);
            args[9] = new JValue(IntPtr.Zero);
            builderHandle = JNIEnv.NewObject(
                s_imageVectorBuilderClass, s_imageVectorBuilderCtor, args);
        }
        finally
        {
            JNIEnv.DeleteLocalRef(vectorName);
        }

        var builder = Java.Lang.Object.GetObject<ImageVector.Builder>(
            builderHandle, JniHandleOwnership.TransferLocalRef)!;
        using var path = new PathBuilder();
        path.MoveTo(20f, 3f);
        path.HorizontalLineTo(4f);
        path.VerticalLineToRelative(10f);
        path.CurveToRelative(0f, 2.21f, 1.79f, 4f, 4f, 4f);
        path.HorizontalLineToRelative(6f);
        path.CurveToRelative(2.21f, 0f, 4f, -1.79f, 4f, -4f);
        path.VerticalLineToRelative(-3f);
        path.HorizontalLineToRelative(2f);
        path.CurveToRelative(1.11f, 0f, 2f, -0.9f, 2f, -2f);
        path.VerticalLineTo(5f);
        path.CurveToRelative(0f, -1.11f, -0.89f, -2f, -2f, -2f);
        path.Close();
        path.MoveTo(20f, 8f);
        path.HorizontalLineToRelative(-2f);
        path.VerticalLineTo(5f);
        path.HorizontalLineTo(20f);
        path.VerticalLineToRelative(3f);
        path.Close();
        path.MoveTo(4f, 19f);
        path.HorizontalLineTo(20f);
        path.VerticalLineToRelative(2f);
        path.HorizontalLineTo(4f);
        path.Close();

        var nodes = path.Nodes as Java.Lang.Object
            ?? throw new InvalidOperationException("Compose path nodes are unavailable.");
        var pathName = JNIEnv.NewString(string.Empty);
        var fill = BrushSolidColor((long)Color.Black);
        try
        {
            JValue* args = stackalloc JValue[14];
            args[0] = new JValue(nodes.Handle);
            args[1] = new JValue(0);
            args[2] = new JValue(pathName);
            args[3] = new JValue(fill);
            args[4] = new JValue(1f);
            args[5] = new JValue(IntPtr.Zero);
            args[6] = new JValue(1f);
            args[7] = new JValue(1f);
            args[8] = new JValue(0);
            args[9] = new JValue(0);
            args[10] = new JValue(4f);
            args[11] = new JValue(0f);
            args[12] = new JValue(1f);
            args[13] = new JValue(0f);
            var result = JNIEnv.CallObjectMethod(
                builder.Handle, s_imageVectorAddPath, args);
            JNIEnv.DeleteLocalRef(result);
        }
        finally
        {
            JNIEnv.DeleteLocalRef(pathName);
            JNIEnv.DeleteLocalRef(fill);
        }

        return builder.Build();
    }
}
