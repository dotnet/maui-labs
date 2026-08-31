using OpenQA.Selenium;

namespace Microsoft.Maui.DevFlow.Appium.SmokeTests;

public sealed class AppiumSmokeContractTests
{
    private const string OpenModalAutomationId = "ShowModalButton";
    private const string ModalTitleAutomationId = "ModalTitle";

    [AppiumSmokeFact]
    [Trait("Category", "AppiumSmoke")]
    public void LaunchOrAttach_LocatesStableAutomationId_InteractsAndVerifiesVisibleResult()
    {
        var configuration = AppiumSmokeEnvironment.Evaluate().RequireReady();
        var artifacts = AppiumSmokeArtifactSink.Create(configuration);
        IWebDriver? driver = null;

        try
        {
            driver = AppiumSmokeDriverFactory.Create(configuration);
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.Zero;

            var openModal = AppiumSmokeWaiter.UntilVisible(
                driver,
                AppiumSmokeLocators.ForAppAutomationId(configuration.Platform, OpenModalAutomationId).By,
                configuration.ElementTimeout);
            openModal.Click();

            var modalTitle = AppiumSmokeWaiter.UntilVisible(
                driver,
                AppiumSmokeLocators.ForAppAutomationId(configuration.Platform, ModalTitleAutomationId).By,
                configuration.ElementTimeout);
            Assert.True(modalTitle.Displayed);
            Assert.Contains("Modal Page", modalTitle.Text);

            if (configuration.Permission is not null)
            {
                ExerciseDeclaredPermissionSmoke(driver, configuration);
            }
        }
        catch (Exception exception)
        {
            artifacts.CaptureFailure(driver, exception);
            throw;
        }
        finally
        {
            if (driver is not null)
            {
                try
                {
                    driver.Quit();
                }
                catch (WebDriverException)
                {
                }

                try
                {
                    driver.Dispose();
                }
                catch (WebDriverException)
                {
                }
            }
        }
    }

    private static void ExerciseDeclaredPermissionSmoke(IWebDriver driver, AppiumSmokeOptions configuration)
    {
        var permission = configuration.Permission!;
        if (!string.IsNullOrWhiteSpace(permission.NavigationAccessibilityId))
        {
            AppiumSmokeWaiter.UntilVisible(
                driver,
                AppiumSmokeLocators.ForAccessibilityId(permission.NavigationAccessibilityId),
                configuration.ElementTimeout).Click();
        }

        AppiumSmokeWaiter.UntilVisible(
            driver,
            AppiumSmokeLocators.ForAppAutomationId(configuration.Platform, permission.TriggerAutomationId).By,
            configuration.ElementTimeout).Click();

        AppiumSmokeWaiter.UntilVisible(
            driver,
            AppiumSmokeLocators.ForAccessibilityId(permission.AllowAccessibilityId),
            configuration.ElementTimeout).Click();

        if (!string.IsNullOrWhiteSpace(permission.ResultAutomationId))
        {
            Assert.True(AppiumSmokeWaiter.UntilVisible(
                driver,
                AppiumSmokeLocators.ForAppAutomationId(configuration.Platform, permission.ResultAutomationId).By,
                configuration.ElementTimeout).Displayed);
        }
    }
}
