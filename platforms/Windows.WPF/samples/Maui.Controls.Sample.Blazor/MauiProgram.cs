using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Hosting;

namespace Maui.Controls.Sample.Blazor
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
           // Environment.CurrentDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiAppWPF<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();
            builder.Services.AddWpfBlazorWebView();

#if DEBUG
            //builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
        class App : Application
        {
            protected override Window CreateWindow(IActivationState? activationState)
            {
                return new Window(new MainPage());
                //return new Window(new ContentPage() { Content = new Label() { Text = @"Welcome to the Dub P EF \m/" } });
            }
        }

    }



    class WPFWindow : Window
    {
        public WPFWindow()
        {

        }

        public class WPFNavProxy : NavigationProxy
        {
            public WPFNavProxy()
            {
            }
        }

        class NavigationImpl : NavigationProxy
        {
            readonly Window _owner;

            public NavigationImpl(Window owner)
            {
                _owner = owner;
                _owner.NavigationProxy.Inner = this;
            }

            protected override IReadOnlyList<Page> GetModalStack()
            {
                throw new NotImplementedException();
            }

            protected override Task<Page?> OnPopModal(bool animated)
            {
                throw new NotImplementedException();
            }

            protected override Task OnPushModal(Page modal, bool animated)
            {
                throw new NotImplementedException();
            }
        }
    }
}
