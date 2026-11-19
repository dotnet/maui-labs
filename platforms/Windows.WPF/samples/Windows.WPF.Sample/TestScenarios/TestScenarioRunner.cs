namespace Microsoft.Maui.Platforms.Windows.WPF.Sample.TestScenarios;

public static class TestScenarioRunner
{
    public static int Run(string[] args)
    {
        if (args.Length != 2 || args[0] != "--test-scenario")
        {
            Console.Error.WriteLine("Usage: --test-scenario <name>");
            return 2;
        }

        switch (args[1])
        {
#if WPF_ASSET_TEST_SCENARIO
            case "packaged-assets":
                return PackagedAssetsScenario.Run();
#endif
#if WPF_FONT_TEST_SCENARIO
            case "registered-fonts":
                return RegisteredFontsScenario.Run();
#endif
            default:
                Console.Error.WriteLine($"Unknown test scenario: {args[1]}");
                return 2;
        }
    }
}
