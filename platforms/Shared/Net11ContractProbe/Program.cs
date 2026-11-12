using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Platform;
using Microsoft.Maui.Media;

var requiredTypes = new[]
{
    typeof(IAlertManager),
    typeof(IAlertManagerSubscription),
    typeof(IGesturePlatformManager),
    typeof(IGesturePlatformManagerFactory),
    typeof(IViewScreenshot),
    typeof(MauiBlazorWebViewBuilderExtensions),
};

RequireMethod(typeof(TapGestureRecognizer), "SendTapped");
RequireMethod(typeof(LongPressGestureRecognizer), "SendLongPressed");
RequireMethod(typeof(MauiBlazorWebViewBuilderExtensions), "UsePlatformHandler");
RequireMethod(typeof(IViewScreenshot), "CaptureViewAsync");

Console.WriteLine($"Verified {requiredTypes.Length} .NET 11 community-platform contract types.");

static void RequireMethod(Type type, string name)
{
    if (!type.GetMethods().Any(method => method.Name == name))
        throw new MissingMethodException(type.FullName, name);
}
