using System.Runtime.Versioning;
using Browser.Essentials.TestApp;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Maui.Platforms.Browser.Essentials;

[assembly: SupportedOSPlatform("browser")]

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<TestPage>("#app");

builder.Services.AddBrowserEssentials();
builder.Services.AddSingleton<EssentialsTestSuite>();

await BrowserEssentials.InitializeAsync();

await builder.Build().RunAsync();
