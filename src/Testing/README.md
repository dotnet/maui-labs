# Microsoft.Maui.Testing

`Microsoft.Maui.Testing` runs Microsoft.Testing.Platform (MTP) tests inside native Android, iOS, and Mac Catalyst applications. It uses the same startup shape as a .NET MAUI app: a shared `MauiProgram.CreateMauiTestApp()` method plus thin platform lifecycle classes.

> [!WARNING]
> Microsoft.Maui.Testing is experimental. It requires the .NET 11 SDK and the .NET 11 Android, iOS, and Mac Catalyst workloads.

## Requirements

- .NET 11 SDK
- Android workload for Android tests
- iOS and Mac Catalyst workloads on macOS for Apple tests
- An Android device/emulator or Apple simulator/device when executing tests

Install the workloads:

```console
dotnet workload install maui
```

## Install the template

```console
dotnet new install Microsoft.Maui.Testing.Templates --prerelease
```

## Create a test project

```console
dotnet new mauitest -n MyApp.Tests
cd MyApp.Tests
```

The project targets `net11.0-android`, `net11.0-ios`, and `net11.0-maccatalyst`. MAUI Controls and MSTest are configured by default.

## Run tests

Run the project and select a target framework from the interactive prompt:

```console
dotnet test
```

For non-interactive automation, pass a target framework explicitly, such as `dotnet test -f net11.0-android`.

`dotnet test` prompts for the target framework, then builds and launches the native test application through MTP. Android reports live instrumentation status, and Apple platforms report test status to the application log. Each platform writes a TRX file under its application data `TestResults` directory.

For direct Android instrumentation launches, pass MTP arguments as a JSON string array in the `mtp-arguments` extra:

```console
adb shell am instrument -w -r \
  -e mtp-arguments '["--filter","FullyQualifiedName~Smoke"]' \
  com.companyname.myapptests/com.companyname.myapptests.TestInstrumentation
```

The Android SDK test host can use the same extra when forwarding `dotnet test` request arguments.

`dotnet run -f net11.0-android -- --filter FullyQualifiedName~Smoke` is also supported through the Android host's standard `args` instrumentation extra. The current .NET 11 preview Android `dotnet test` adapter does not yet forward its filter request to the on-device MTP process; use `dotnet run` or the explicit instrumentation extra when an on-device filter is required.

## Configure services

Register services in `MauiProgram.cs`:

```csharp
public static MauiTestApp CreateMauiTestApp()
{
    var builder = MauiTestApp.CreateBuilder();

    builder.Services.AddSingleton<WeatherService>();
    builder.ConfigureTestApplication(testApplication =>
        testApplication.AddMSTest(() => [typeof(MauiProgram).Assembly]));

    return builder.Build();
}
```

## Switch to NUnit

1. Replace the `MSTest.TestAdapter` and `MSTest.TestFramework` packages and implicit using:

```xml
<PropertyGroup>
  <EnableNUnitRunner>true</EnableNUnitRunner>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Microsoft.Maui.Testing" Version="0.1.0-preview.12" />
  <PackageReference Include="NUnit" Version="4.4.0" />
  <PackageReference Include="NUnit3TestAdapter" Version="6.2.0" />
</ItemGroup>

<ItemGroup>
  <Using Include="NUnit.Framework" />
</ItemGroup>
```

2. Change the registration line in `MauiProgram.cs`:

```csharp
using NUnit.VisualStudio.TestAdapter.TestingPlatformAdapter;

builder.ConfigureTestApplication(testApplication =>
    testApplication.AddNUnit(() => [typeof(MauiProgram).Assembly]));
```

3. Change `[TestClass]`/`[TestMethod]` to NUnit's `[TestFixture]`/`[Test]` attributes and use NUnit assertions.

## xUnit status

The current xUnit v3 MTP package validates desktop app-host properties that mobile SDKs intentionally override. `Microsoft.Maui.Testing` does not patch xUnit's build targets, so xUnit mobile support remains unavailable until that validation supports Android, iOS, and Mac Catalyst.

## Project structure

```text
MyApp.Tests/
├── MauiProgram.cs
├── Test1.cs
├── Platforms/
│   ├── Android/TestInstrumentation.cs
│   ├── iOS/AppDelegate.cs
│   └── MacCatalyst/AppDelegate.cs
└── MyApp.Tests.csproj
```

`MauiTestInstrumentation` owns Android test execution and instrumentation result bundles. `MauiTestAppDelegate` owns the Apple lifecycle and logs test status. The generated platform classes only forward `CreateMauiTestApp()` to `MauiProgram`.

## Build this product

```console
cd src/Testing
dotnet build Microsoft.Maui.Testing/Microsoft.Maui.Testing.csproj
dotnet test Microsoft.Maui.Testing.Tests/Microsoft.Maui.Testing.Tests.csproj
dotnet pack Microsoft.Maui.Testing/Microsoft.Maui.Testing.csproj
dotnet pack templates/Microsoft.Maui.Testing.Templates.csproj
```
