# Microsoft.Maui.Testing

`Microsoft.Maui.Testing` provides a MAUI builder and native test lifecycle for MTP-based tests running on Android, iOS, and Mac Catalyst. The generated project references MAUI Controls, so tests can construct and exercise controls directly.

```console
dotnet new install Microsoft.Maui.Testing.Templates --prerelease
dotnet new mauitest -n MyApp.Tests
cd MyApp.Tests
dotnet test
```

The runtime package is referenced automatically by the `mauitest` template. Configure dependencies through `MauiTestApp.CreateBuilder().Services`; MSTest is the template default, and framework adapters are configured in `MauiProgram`.

See the [full documentation](https://github.com/dotnet/maui-labs/tree/main/src/Testing) for Apple commands and framework conversion instructions.

> This package is experimental and requires .NET 11.
