using OpenQA.Selenium;

namespace Microsoft.Maui.DevFlow.Appium.SmokeTests;

public static class AppiumSmokeWaiter
{
    public static IWebElement UntilVisible(IWebDriver driver, By locator, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(locator);

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var element = driver.FindElement(locator);
                if (element.Displayed)
                {
                    return element;
                }
            }
            catch (NoSuchElementException)
            {
            }
            catch (StaleElementReferenceException)
            {
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(200));
        }

        throw new WebDriverTimeoutException($"Element did not become visible within {timeout.TotalSeconds:0} seconds: {locator}");
    }
}
