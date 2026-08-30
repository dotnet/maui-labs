using OpenQA.Selenium.Appium;

namespace Microsoft.Maui.DevFlow.Appium.SmokeTests;

public static class AppiumSmokeCapabilities
{
    public static AppiumOptions Create(AppiumSmokeOptions configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new AppiumOptions
        {
            PlatformName = AppiumSmokePlatformSupport.PlatformName(configuration.Platform),
            AutomationName = AppiumSmokePlatformSupport.AutomationName(configuration.Platform),
        };

        SetIfPresent(configuration.DeviceName, value => options.DeviceName = value);
        SetIfPresent(configuration.PlatformVersion, value => options.PlatformVersion = value);
        SetIfPresent(configuration.Udid, value => options.AddAdditionalAppiumOption("udid", value));

        switch (configuration.Platform)
        {
            case AppiumSmokePlatform.Android:
                SetIfPresent(configuration.App, value => options.App = value);
                SetIfPresent(configuration.AppPackage, value => options.AddAdditionalAppiumOption("appPackage", value));
                SetIfPresent(configuration.AppActivity, value => options.AddAdditionalAppiumOption("appActivity", value));
                break;

            case AppiumSmokePlatform.IOS:
            case AppiumSmokePlatform.Mac2:
                SetIfPresent(configuration.App, value => options.App = value);
                SetIfPresent(configuration.BundleId, value => options.AddAdditionalAppiumOption("bundleId", value));
                break;

            case AppiumSmokePlatform.Windows:
                SetIfPresent(configuration.App, value => options.App = value);
                SetIfPresent(configuration.AppTopLevelWindow, value => options.AddAdditionalAppiumOption("appTopLevelWindow", value));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(configuration));
        }

        options.AddAdditionalAppiumOption("noReset", true);
        options.AddAdditionalAppiumOption("newCommandTimeout", (int)configuration.CommandTimeout.TotalSeconds);
        return options;
    }

    public static IReadOnlyDictionary<string, object> ToDictionary(AppiumSmokeOptions configuration) =>
        new Dictionary<string, object>(Create(configuration).ToDictionary(), StringComparer.OrdinalIgnoreCase);

    private static void SetIfPresent(string? value, Action<string> set)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            set(value);
        }
    }
}
