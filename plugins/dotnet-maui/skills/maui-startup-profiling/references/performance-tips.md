# MAUI Startup Performance Tips

From [MS Learn: Improve app performance](https://learn.microsoft.com/dotnet/maui/deployment/performance). Read when trace shows bottlenecks in a category.

## Quick Reference

| Bottleneck | Top suspects | Fix |
|---|---|---|
| MAUI Framework | DI, XAML parsing, handler mapping | §DI, §XAML, §Shell |
| App Code | constructor work, sync I/O, heavy first page | §Lazy Init, §Async, §Page Complexity |
| .NET Bindings | excessive managed↔native calls | batch calls, cache results |
| .NET Runtime | JIT, assembly loading, reflection | §R2R/MIBC (mibc-r2r-guide.md), §Trimming |
| Platform/OS | OS rendering, native lib loading | rarely actionable |

## DI Container

DI uses reflection for construction. Deep dependency graphs at startup are expensive.

**Trace signal:** `Microsoft.Extensions.DependencyInjection` frames >5% of startup CPU.

**Fixes:**
- `AddSingleton` / `AddTransient` over `AddScoped` when possible
- Factory registrations (`sp => new Service()`) avoid reflection
- `Lazy<T>` wrappers for services not needed until after first render
- Flatten deep dependency chains

[MS Learn ref](https://learn.microsoft.com/dotnet/maui/deployment/performance#choose-a-dependency-injection-container-carefully)

## Shell vs TabbedPage

Shell creates pages on demand. TabbedPage creates all tabs at startup.

**Trace signal:** multiple page constructors during startup.

**Fixes:**
- Use Shell, not TabbedPage
- Never set `ShellContent.Content` to instance directly (forces eager creation)
- Use `DataTemplate`: `<ShellContent ContentTemplate="{DataTemplate local:MyPage}" />`

[MS Learn ref](https://learn.microsoft.com/dotnet/maui/deployment/performance#create-shell-apps)

## XAML Compilation

Uncompiled XAML parsed at runtime — much slower.

**Trace signal:** `Microsoft.Maui.Controls.Xaml` frames.

**Fixes:**
- Ensure `XamlCompilationOptions.Compile` (default, but can be disabled per-page)
- Use compiled bindings (`x:DataType`)

[MS Learn ref](https://learn.microsoft.com/dotnet/maui/deployment/performance#use-compiled-bindings)

## Compiled Bindings

Reflection bindings resolve at runtime. Compiled bindings resolve at build.

**Trace signal:** `BindingExpression` or `System.Reflection` during page inflation.

**Fixes:**
- Add `x:DataType="vm:MyViewModel"` to XAML
- Avoid string-based `SetBinding()` — use lambda overload or XAML

[MS Learn ref](https://learn.microsoft.com/dotnet/maui/deployment/performance#use-compiled-bindings)

## Lazy Init

Eager creation of expensive objects delays first frame.

**Trace signal:** app namespace constructors doing heavy work before first visual frame.

**Fixes:**
- `Lazy<T>` for expensive services
- `Dispatcher.DispatchAsync()` for non-critical init after first frame
- Move network/DB calls out of `CreateMauiApp()`

[MS Learn ref](https://learn.microsoft.com/dotnet/framework/performance/lazy-initialization)

## Async

Blocking sync calls on UI thread freeze first render.

**Trace signal:** large self time in app methods on main thread.

**Fixes:**
- Never `.Result` or `.Wait()` on startup — use `await`
- Async `OnAppearing` over sync constructors
- `Task.Run` for CPU-bound init not needing UI thread

[MS Learn ref](https://learn.microsoft.com/dotnet/maui/deployment/performance#use-asynchronous-programming)

## Page Complexity

Complex first pages take longer to measure, layout, render.

**Trace signal:** handler creation and layout frames dominating startup.

**Fixes:**
- Minimize elements on first page
- `CollectionView` with virtualization over `StackLayout` with many children
- Defer below-fold content
- `Grid` over deeply nested `StackLayout`

[MS Learn ref](https://learn.microsoft.com/dotnet/maui/deployment/performance#reduce-the-number-of-elements-on-a-page)

## Trimming

Untrimmed apps load more assemblies than needed.

**Trace signal:** assembly loading and type resolution in Runtime category.

**Fixes:**
- `PublishTrimmed=true` for Release
- `TrimMode=full` for aggressive (test thoroughly)
- MAUI SDK enables trimming by default for Release Android/iOS

[MS Learn ref](https://learn.microsoft.com/dotnet/maui/deployment/performance#reduce-the-size-of-the-app)

## Image/Resource Loading

Large images or many resources at startup block rendering.

**Trace signal:** image decoding or resource resolution frames.

**Fixes:**
- Right-sized images
- Lazy image loading
- Split resources into page-level, not all in `App.xaml`

[MS Learn ref](https://learn.microsoft.com/dotnet/maui/deployment/performance#optimize-image-resources)
