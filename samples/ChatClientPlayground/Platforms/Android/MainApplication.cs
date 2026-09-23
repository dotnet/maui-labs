using Android.App;
using Android.Runtime;

namespace ChatClientPlayground;

/// <summary>Android application entry point.</summary>
[Application]
public class MainApplication(IntPtr handle, JniHandleOwnership ownership) : MauiApplication(handle, ownership)
{
    /// <inheritdoc />
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
