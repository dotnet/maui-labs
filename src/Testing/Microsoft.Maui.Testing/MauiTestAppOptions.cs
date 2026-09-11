namespace Microsoft.Maui.Testing;

public sealed class MauiTestAppOptions
{
    public string ResultsDirectoryName { get; init; } = "TestResults";

    public bool GenerateTrxReport { get; init; } = true;
}
