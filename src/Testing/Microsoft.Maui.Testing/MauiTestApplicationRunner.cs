using Microsoft.Testing.Platform.Builder;

namespace Microsoft.Maui.Testing;

public delegate Task<int> MauiTestApplicationRunner(
    string[] args,
    Action<ITestApplicationBuilder, string[]> configure);
