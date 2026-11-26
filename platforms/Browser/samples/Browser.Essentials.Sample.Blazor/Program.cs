using System.Runtime.Versioning;
using Browser.Essentials.Sample.Blazor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Maui.Platforms.Browser.Essentials;

[assembly: SupportedOSPlatform("browser")]

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddBrowserEssentials();

// Load the JS interop module before any Essentials API is used.
await BrowserEssentials.InitializeAsync();

await builder.Build().RunAsync();
