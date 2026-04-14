using System.Diagnostics;
using CommunityToolkit.Mvvm.Messaging;
using ControlGallery.Common.Messages;

namespace ControlGallery;

public partial class App : Microsoft.Maui.Controls.Application
{
    public App()
    {
        InitializeComponent();

        MauiExceptions.UnhandledException += (sender, args) =>
        {
            Debug.WriteLine($"Unhandled Exception: {args.ExceptionObject}");
            // _logger.LogCritical(e.ExceptionObject as Exception, "App failed to handle exception");
            throw (Exception)args.ExceptionObject;
        };
    }

    protected override Window CreateWindow(IActivationState activationState)
    {
        AppWindow window = new AppWindow();
        window.Page = new AppShell();
        return window;
    }

    
}