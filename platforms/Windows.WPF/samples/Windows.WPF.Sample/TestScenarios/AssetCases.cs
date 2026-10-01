namespace Microsoft.Maui.Platforms.Windows.WPF.Sample.TestScenarios;

public static class AssetCases
{
    public static IEnumerable<object[]> All =>
    [
        ["AboutAssets.txt", "top-level asset"],
        ["Data/sample.txt", "nested asset"],
        [@"Data\sample.txt", "nested asset"],
        ["Other/sample.txt", "same basename, different folder"],
        ["Config/settings.txt", "renamed logical asset"],
        ["Fallback.txt", "default filename"],
        ["Nested/fallback.txt", "default recursive path"],
        ["Linked/asset.txt", "linked asset"],
    ];
}
