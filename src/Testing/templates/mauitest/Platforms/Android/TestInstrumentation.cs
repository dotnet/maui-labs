using Android.App;
using Android.Runtime;
using Microsoft.Maui.Testing;

namespace MauiTest1;

[Instrumentation(Name = "com.companyname.mauitest1.TestInstrumentation")]
public sealed class TestInstrumentation : MauiTestInstrumentation
{
    public TestInstrumentation(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override MauiTestApp CreateMauiTestApp() => MauiProgram.CreateMauiTestApp();
}
