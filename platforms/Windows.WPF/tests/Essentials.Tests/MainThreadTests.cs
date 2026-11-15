using System.Windows.Threading;
using Dispatcher = System.Windows.Threading.Dispatcher;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.Hosting;

namespace Essentials.Tests;

// UseMauiAppWPF applies ConfigureEssentials, whose initializer bridges MainThread from the
// application dispatcher: here the WPF dispatcher of the (STA) thread that builds the app.
public class MainThreadTests
{
    [Fact]
    public void Build_WithBackend_MainThreadUsesWpfDispatcher()
    {
        StaticFacadeTests.Run(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var uiThread = Environment.CurrentManagedThreadId;
            using var app = MauiApp.CreateBuilder().UseMauiAppWPF<StaticFacadeTests.TestApplication>().Build();

            Assert.True(MainThread.IsMainThread);
            Assert.False(Task.Run(() => MainThread.IsMainThread).GetAwaiter().GetResult());

            int? ranOn = null;
            var frame = new DispatcherFrame();
            var timeout = new DispatcherTimer(TimeSpan.FromSeconds(10), DispatcherPriority.Normal, (_, _) => frame.Continue = false, dispatcher);
            Task.Run(() => MainThread.BeginInvokeOnMainThread(() =>
            {
                ranOn = Environment.CurrentManagedThreadId;
                frame.Continue = false;
            }));
            Dispatcher.PushFrame(frame);
            timeout.Stop();

            Assert.Equal(uiThread, ranOn);
        });
    }
}
