---
name: maui-unit-testing
description: >-
  Add and improve automated tests for .NET MAUI apps, focusing on ViewModels,
  services, platform abstractions, and boundaries between unit, integration, and
  device tests. USE FOR: xUnit test projects; ViewModel tests (do not call
  MauiProgram.CreateMauiApp for pure ViewModel logic — use fakes/mocks instead);
  using MAUI's built-in injectable platform interfaces (IGeolocation, IConnectivity,
  IPreferences, IFileSystem, ISecureStorage) instead of static Essentials access
  to avoid PlatformNotSupportedException in test hosts; testing navigation and
  data services; using Microsoft.Maui.Build.AppProjectReference NuGet package when
  a normal ProjectReference to a MAUI app project causes multi-targeting build
  errors; deciding what must run on device. DO NOT USE FOR: runtime UI automation
  with a running app (use maui-devflow-debug), generic app architecture (use
  maui-app-architecture), or production performance profiling.
---

# MAUI Unit Testing

Use this skill to make MAUI app logic testable without requiring every test to
start a device or simulator. Keep UI framework dependencies at the edges.

## Workflow

1. Identify what is being tested: ViewModel, service, navigation abstraction,
   storage wrapper, platform capability, or UI integration.
2. For ViewModels and services, create a normal xUnit test project.
3. Introduce interfaces around platform APIs such as geolocation, preferences,
   secure storage, media picker, file picker, and navigation.
4. Use fakes or mocks for those interfaces in unit tests.
5. Use device/integration tests only for behavior that requires handlers,
   platform permissions, native controls, or OS services.
6. If a test or tooling project must reference a MAUI app project, use the
   AppProjectReference support provided by `Microsoft.Maui.Build.AppProjectReference`
   instead of forcing the app project to behave like a normal class library.

## Test Boundary Guide

| Test target | Test type |
| --- | --- |
| ViewModel commands, validation, state transitions | Unit test |
| API/data service with fake handler | Unit test |
| Secure storage wrapper behavior with fake storage | Unit test |
| Shell route strings and navigation abstraction calls | Unit test |
| Handler rendering, native permissions, real pickers | Device/integration test |
| End-to-end UI flow in a running app | DevFlow/runtime automation |

## ViewModel Test Pattern

Always show at least a happy-path test AND an error/edge-case test. Use a
mocking library (NSubstitute, Moq) or a hand-rolled fake — both are valid.

```csharp
public sealed class ProductsViewModelTests
{
    [Fact]
    public async Task LoadAsync_ServiceReturnsProducts_PopulatesProducts()
    {
        var service = Substitute.For<IProductsService>();
        service.LoadAsync().Returns([new Product("Coffee")]);
        var viewModel = new ProductsViewModel(service);

        await viewModel.LoadAsync();

        Assert.Single(viewModel.Products);
        Assert.Equal("Coffee", viewModel.Products[0].Name);
    }

    [Fact]
    public async Task LoadAsync_ServiceThrows_SetsErrorState()
    {
        var service = Substitute.For<IProductsService>();
        service.LoadAsync().Returns(Task.FromException<IReadOnlyList<Product>>(
            new HttpRequestException("offline")));
        var viewModel = new ProductsViewModel(service);

        await viewModel.LoadAsync();

        Assert.Empty(viewModel.Products);
        Assert.NotNull(viewModel.ErrorMessage);
    }
}
```

## Platform Abstraction Pattern

MAUI exposes injectable interfaces for many platform services. Prefer the built-in
interface when it exists, such as `IGeolocation`, `IFileSystem`, `IPreferences`,
`IConnectivity`, or `ISecureStorage`. Create a custom wrapper when the app needs
to combine platform APIs, normalize errors, or expose an app-specific contract.

```csharp
public interface ILocationService
{
    Task<Location?> GetLocationAsync(CancellationToken cancellationToken);
}
```

ViewModels depend on `ILocationService`, not `Geolocation.Default` directly.
The production MAUI project registers the platform implementation; tests pass a
fake implementation.

## Anti-Patterns

- Do not call `MauiProgram.CreateMauiApp()` in ordinary ViewModel unit tests.
- Do not require a simulator for tests that only validate C# state transitions.
- Do not use static MAUI platform services directly from ViewModels if the logic
  needs deterministic unit tests.
- Do not call `MainThread.BeginInvokeOnMainThread` or `Dispatcher.Dispatch`
  inside synchronous ViewModel command logic; in plain xUnit hosts there may be
  no initialized platform dispatcher. Keep dispatching at the UI layer or behind
  an injectable dispatcher abstraction.
- Do not put UI automation expectations in unit tests; use DevFlow/runtime
  automation for running-app behavior.

## Validation Checklist

- ViewModel tests run without a device.
- Platform APIs are behind interfaces or wrappers.
- Test project references follow the repo's package management conventions.
- Device-only behavior is explicitly separated from unit-testable logic.
