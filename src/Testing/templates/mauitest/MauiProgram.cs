using Microsoft.Maui.Testing;

namespace MauiTest1;

public static class MauiProgram
{
    public static MauiTestApp CreateMauiTestApp()
    {
        var builder = MauiTestApp.CreateBuilder();

        builder.ConfigureTestApplication(testApplication =>
            testApplication.AddMSTest(() => [typeof(MauiProgram).Assembly]));

        return builder.Build();
    }
}
