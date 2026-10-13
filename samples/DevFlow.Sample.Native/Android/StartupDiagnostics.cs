#if DEBUG
using System.Runtime.CompilerServices;

namespace DevFlow.Sample.Native.Android;

internal static class StartupDiagnostics
{
    [ThreadStatic]
    private static bool _reporting;

    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255",
        Justification = "Android app assemblies compile as libraries; observe loader failures before activity creation.")]
    internal static void Initialize()
    {
        AppDomain.CurrentDomain.FirstChanceException += (_, args) =>
        {
            if (_reporting || args.Exception is not BadImageFormatException exception)
                return;

            // Mono can abort its load hook before an exception reaches the activity or agent logs.
            _reporting = true;
            try
            {
                global::Android.Util.Log.Error("DevFlowNativeStartup",
                    $"BadImageFormatException: {exception.Message}; assembly: {exception.FileName}");
            }
            finally
            {
                _reporting = false;
            }
        };
    }
}
#endif
