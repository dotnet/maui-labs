using OpenQA.Selenium;
using OpenQA.Selenium.Appium;

namespace Microsoft.Maui.DevFlow.Appium.SmokeTests;

public sealed record AppiumSmokeLocator(
    By By,
    AppiumSmokeNativeIdentifierKind NativeIdentifierKind,
    string Identifier);

public static class AppiumSmokeLocators
{
    public static AppiumSmokeLocator ForAppAutomationId(AppiumSmokePlatform platform, string automationId)
    {
        if (string.IsNullOrWhiteSpace(automationId)
            || automationId.Length > 128
            || automationId.Any(character => !(char.IsLetterOrDigit(character) || character is '.' or '_' or '-')))
        {
            throw new ArgumentException("App AutomationId values must be stable identifier tokens.", nameof(automationId));
        }

        return new AppiumSmokeLocator(
            MobileBy.AccessibilityId(automationId),
            AppiumSmokePlatformSupport.NativeIdentifierKind(platform),
            automationId);
    }

    public static By ForAccessibilityId(string accessibilityId)
    {
        if (string.IsNullOrWhiteSpace(accessibilityId))
        {
            throw new ArgumentException("An accessibility identifier is required.", nameof(accessibilityId));
        }

        return MobileBy.AccessibilityId(accessibilityId);
    }
}
