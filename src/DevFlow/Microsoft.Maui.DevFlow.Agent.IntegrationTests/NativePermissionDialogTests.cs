using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Microsoft.Maui.DevFlow.Driver;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Collection("AgentIntegration")]
[Trait("Category", "NativeDialogs")]
public class NativePermissionDialogTests : IntegrationTestBase
{
    public NativePermissionDialogTests(AppFixture app, ITestOutputHelper output)
        : base(app, output) { }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Android_ContactsPermissionDialog_CanConfirmExactChoice(bool allow)
    {
        if (!Platform.Equals("android", StringComparison.OrdinalIgnoreCase))
        {
            Output.WriteLine("Android permission dialog test skipped on non-Android platform.");
            return;
        }

        await App.ResetPermissionAsync(PermissionService.Contacts);
        await NavigateToPageAsync("//dialogs", "RequestContactsBtn", timeoutMs: 15000);

        var trigger = await FindElementAsync("RequestContactsBtn");
        Assert.True(await Client.TapAsync(trigger.Id));

        using var driver = new AndroidAppDriver
        {
            Serial = App.DeviceId
                ?? throw new InvalidOperationException("Android fixture did not report its device serial.")
        };
        var dialog = await WaitForDialogAsync(
            driver.DetectAlertAsync,
            driver.GetAccessibilityTreeAsync);
        LogDialog(dialog);

        var button = allow
            ? FindButton(dialog, label =>
                label.Contains("Allow", StringComparison.OrdinalIgnoreCase)
                && !label.Contains("Don't", StringComparison.OrdinalIgnoreCase)
                && !label.Contains("Don\u2019t", StringComparison.OrdinalIgnoreCase))
            : FindButton(dialog, label =>
                label.Contains("Don't", StringComparison.OrdinalIgnoreCase)
                || label.Contains("Don\u2019t", StringComparison.OrdinalIgnoreCase));

        var result = await driver.PressAlertButtonSafelyAsync(
            dialog,
            button.Label,
            App.AppIdentifier
                ?? throw new InvalidOperationException("Android fixture did not report its package identifier."),
            (await Client.GetStatusAsync())?.AppName
                ?? throw new InvalidOperationException("Android agent did not report its app name."));
        Assert.True(result.Success, result.Message);

        await WaitForStatusAsync(
            allow ? "contacts: Granted" : "contacts: Denied",
            timeoutMs: 15000);
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task Ios_ContactsPermissionDialog_OffersAndAcceptsDenyChoice()
    {
        // iOS 18+ follows Continue with a system contact-selection surface that idb does not
        // expose as an actionable AX tree. This test covers the permission sheet itself;
        // the location test below covers a successful iOS allow response.
        if (!Platform.Equals("ios", StringComparison.OrdinalIgnoreCase))
        {
            Output.WriteLine("iOS contacts permission dialog test skipped on non-iOS platform.");
            return;
        }

        await App.ResetPermissionAsync(PermissionService.Contacts);
        var driver = await CreateIosDriverAsync();
        using (driver)
        {
            await NavigateToPageAsync("//dialogs", "RequestContactsBtn", timeoutMs: 15000);
            var trigger = await FindElementAsync("RequestContactsBtn");
            Assert.True(await Client.TapAsync(trigger.Id));

            var dialog = await WaitForDialogAsync(
                driver.DetectAlertAsync,
                driver.GetAccessibilityTreeAsync);
            LogDialog(dialog);
            Assert.Contains(dialog.Buttons, button =>
                button.Label.Equals("Continue", StringComparison.OrdinalIgnoreCase));
            var deny = FindButton(dialog, label =>
                label.Contains("Don't", StringComparison.OrdinalIgnoreCase)
                || label.Contains("Don\u2019t", StringComparison.OrdinalIgnoreCase));

            var result = await driver.PressAlertButtonSafelyAsync(dialog, deny.Label);
            Assert.True(result.Success, result.Message);
        }

        await WaitForStatusAsync("contacts: Denied", timeoutMs: 15000);
    }

    [Trait(TestFramework.Trait, TestFramework.Maui)]
    [Fact]
    public async Task Ios_LocationPermissionDialog_OffersAndAcceptsWhileUsingChoice()
    {
        if (!Platform.Equals("ios", StringComparison.OrdinalIgnoreCase))
        {
            Output.WriteLine("iOS location permission dialog test skipped on non-iOS platform.");
            return;
        }

        await App.ResetPermissionAsync(PermissionService.Location);
        var driver = await CreateIosDriverAsync();
        using (driver)
        {
            await NavigateToPageAsync("//dialogs", "RequestLocationBtn", timeoutMs: 15000);
            var trigger = await FindElementAsync("RequestLocationBtn");
            Assert.True(await Client.TapAsync(trigger.Id));

            var dialog = await WaitForDialogAsync(
                driver.DetectAlertAsync,
                driver.GetAccessibilityTreeAsync);
            LogDialog(dialog);
            var whileUsing = FindButton(dialog, label =>
                label.Contains("While Using", StringComparison.OrdinalIgnoreCase));

            var result = await driver.PressAlertButtonSafelyAsync(dialog, whileUsing.Label);
            Assert.True(result.Success, result.Message);
        }

        await WaitForStatusAsync("location: Granted", timeoutMs: 15000);
    }

    async Task<iOSSimulatorAppDriver> CreateIosDriverAsync()
    {
        var status = await Client.GetStatusAsync()
            ?? throw new InvalidOperationException("Agent status was unavailable.");
        return new iOSSimulatorAppDriver
        {
            DeviceUdid = App.DeviceId ?? status.Device?.Id
                ?? throw new InvalidOperationException("iOS fixture did not report its simulator UDID."),
            BundleId = App.AppIdentifier ?? status.App?.PackageId,
            ExpectedAppName = status.AppName
        };
    }

    async Task<AlertInfo> WaitForDialogAsync(
        Func<Task<AlertInfo?>> detect,
        Func<Task<string>>? dumpAccessibility = null,
        int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        AlertInfo? dialog = null;
        while (DateTime.UtcNow < deadline)
        {
            dialog = await detect();
            if (dialog is not null)
                return dialog;
            await Task.Delay(250);
        }

        if (dumpAccessibility is not null)
        {
            try
            {
                Output.WriteLine("Accessibility tree at timeout:");
                Output.WriteLine(await dumpAccessibility());
            }
            catch (Exception ex)
            {
                Output.WriteLine($"Accessibility tree dump failed: {ex.Message}");
            }
        }

        throw new TimeoutException($"Native permission dialog was not detected within {timeoutMs}ms.");
    }

    async Task WaitForStatusAsync(string expected, int timeoutMs)
    {
        await WaitForAsync(async () =>
        {
            try
            {
                var results = await Client.QueryAsync(automationId: "DialogStatusLabel");
                return results.Any(element =>
                    element.Text?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true);
            }
            catch
            {
                return false;
            }
        }, timeoutMs);
    }

    static AlertButton FindButton(AlertInfo dialog, Func<string, bool> predicate)
        => dialog.Buttons.FirstOrDefault(button => predicate(button.Label))
            ?? throw new InvalidOperationException(
                $"Expected permission choice was not present. Available: {string.Join(", ", dialog.Buttons.Select(button => button.Label))}");

    void LogDialog(AlertInfo dialog)
        => Output.WriteLine(
            $"Detected permission dialog '{dialog.Title}' with buttons: {string.Join(", ", dialog.Buttons.Select(button => button.Label))}");
}
