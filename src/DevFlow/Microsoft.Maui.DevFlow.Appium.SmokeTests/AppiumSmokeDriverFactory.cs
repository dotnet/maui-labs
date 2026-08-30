using OpenQA.Selenium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Appium.iOS;
using OpenQA.Selenium.Appium.Mac;
using OpenQA.Selenium.Appium.Windows;

namespace Microsoft.Maui.DevFlow.Appium.SmokeTests;

public static class AppiumSmokeDriverFactory
{
    public static IWebDriver Create(AppiumSmokeOptions configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var capabilities = AppiumSmokeCapabilities.Create(configuration);
        return configuration.Platform switch
        {
            AppiumSmokePlatform.Android => new AndroidDriver(configuration.ServerUri, capabilities, configuration.CommandTimeout),
            AppiumSmokePlatform.IOS => new IOSDriver(configuration.ServerUri, capabilities, configuration.CommandTimeout),
            AppiumSmokePlatform.Mac2 => new MacDriver(configuration.ServerUri, capabilities, configuration.CommandTimeout),
            AppiumSmokePlatform.Windows => new WindowsDriver(configuration.ServerUri, capabilities, configuration.CommandTimeout),
            _ => throw new ArgumentOutOfRangeException(nameof(configuration)),
        };
    }
}
