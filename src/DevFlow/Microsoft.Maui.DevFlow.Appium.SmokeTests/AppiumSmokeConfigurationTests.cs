using OpenQA.Selenium.Appium;

namespace Microsoft.Maui.DevFlow.Appium.SmokeTests;

public sealed class AppiumSmokeCapabilitiesTests
{
    [Fact]
    public void Create_AndroidPackageAttach_UsesUiAutomator2Capabilities()
    {
        var capabilities = AppiumSmokeCapabilities.ToDictionary(CreateOptions(AppiumSmokePlatform.Android) with
        {
            App = null,
            AppPackage = "com.companyname.mauitodo",
            AppActivity = ".MainActivity",
        });

        Assert.Equal("Android", Capability(capabilities, "platformName")?.ToString());
        Assert.Equal("UiAutomator2", Capability(capabilities, "automationName")?.ToString());
        Assert.Equal("com.companyname.mauitodo", Capability(capabilities, "appPackage")?.ToString());
        Assert.Equal(".MainActivity", Capability(capabilities, "appActivity")?.ToString());
    }

    [Fact]
    public void Create_IosBundleAttach_UsesXcuiTestCapabilities()
    {
        var capabilities = AppiumSmokeCapabilities.ToDictionary(CreateOptions(AppiumSmokePlatform.IOS) with
        {
            App = null,
            BundleId = "com.companyname.mauitodo",
        });

        Assert.Equal("iOS", Capability(capabilities, "platformName")?.ToString());
        Assert.Equal("XCUITest", Capability(capabilities, "automationName")?.ToString());
        Assert.Equal("com.companyname.mauitodo", Capability(capabilities, "bundleId")?.ToString());
    }

    [Fact]
    public void Create_Mac2BundleAttach_UsesMac2Capabilities()
    {
        var capabilities = AppiumSmokeCapabilities.ToDictionary(CreateOptions(AppiumSmokePlatform.Mac2) with
        {
            App = null,
            BundleId = "com.companyname.mauitodo",
        });

        Assert.Equal("mac", Capability(capabilities, "platformName")?.ToString());
        Assert.Equal("Mac2", Capability(capabilities, "automationName")?.ToString());
        Assert.Equal("com.companyname.mauitodo", Capability(capabilities, "bundleId")?.ToString());
    }

    [Fact]
    public void Create_WindowsWindowAttach_UsesWindowsCapabilities()
    {
        var capabilities = AppiumSmokeCapabilities.ToDictionary(CreateOptions(AppiumSmokePlatform.Windows) with
        {
            App = null,
            AppTopLevelWindow = "0000000000012345",
        });

        Assert.Equal("Windows", Capability(capabilities, "platformName")?.ToString());
        Assert.Equal("Windows", Capability(capabilities, "automationName")?.ToString());
        Assert.Equal("0000000000012345", Capability(capabilities, "appTopLevelWindow")?.ToString());
    }

    private static object? Capability(IReadOnlyDictionary<string, object> capabilities, string name)
    {
        return capabilities.TryGetValue(name, out var value)
            ? value
            : capabilities.TryGetValue($"appium:{name}", out value)
                ? value
                : null;
    }

    private static AppiumSmokeOptions CreateOptions(AppiumSmokePlatform platform) => new(
        platform,
        new Uri("http://127.0.0.1:4723/"),
        "DevFlow smoke device",
        "C:\\apps\\DevFlow.Sample.app",
        null,
        null,
        null,
        null,
        null,
        "1.0",
        TimeSpan.FromSeconds(120),
        TimeSpan.FromSeconds(20),
        Path.Combine("artifacts", "TestResults", "appium"),
        CaptureScreenshots: true,
        Permission: null);
}

public sealed class AppiumSmokeEnvironmentTests
{
    [Fact]
    public void Evaluate_WhenDisabled_ReturnsClearSkipReason()
    {
        var readiness = AppiumSmokeEnvironment.Evaluate(_ => null, AppiumSmokeHost.Windows);

        Assert.False(readiness.IsEnabled);
        Assert.False(readiness.IsReady);
        Assert.Contains("DEVFLOW_APPIUM_SMOKE=1", readiness.Reason);
    }

    [Fact]
    public void Evaluate_WhenExplicitlyEnabledWithIosOnWindows_ReportsHostRequirement()
    {
        var readiness = AppiumSmokeEnvironment.Evaluate(
            EnvironmentWith(
                (AppiumSmokeEnvironment.EnableVariable, "1"),
                (AppiumSmokeEnvironment.PlatformVariable, "ios"),
                (AppiumSmokeEnvironment.DeviceNameVariable, "iPhone"),
                (AppiumSmokeEnvironment.BundleIdVariable, "com.companyname.mauitodo")),
            AppiumSmokeHost.Windows);

        Assert.True(readiness.IsEnabled);
        Assert.False(readiness.IsReady);
        Assert.Contains(readiness.Errors, error => error.Contains("macOS host", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_WhenAndroidConfigurationIsComplete_ReturnsReadyOptions()
    {
        var readiness = AppiumSmokeEnvironment.Evaluate(
            EnvironmentWith(
                (AppiumSmokeEnvironment.EnableVariable, "true"),
                (AppiumSmokeEnvironment.PlatformVariable, "android"),
                (AppiumSmokeEnvironment.DeviceNameVariable, "Android Emulator"),
                (AppiumSmokeEnvironment.AppPackageVariable, "com.companyname.mauitodo"),
                (AppiumSmokeEnvironment.AppActivityVariable, ".MainActivity")),
            AppiumSmokeHost.Linux);

        Assert.True(readiness.IsReady, readiness.Reason);
        Assert.Equal(AppiumSmokePlatform.Android, readiness.RequireReady().Platform);
    }

    [Fact]
    public void Evaluate_WhenServerUrlContainsUserInfo_RejectsCredentials()
    {
        var readiness = AppiumSmokeEnvironment.Evaluate(
            EnvironmentWith(
                (AppiumSmokeEnvironment.EnableVariable, "1"),
                (AppiumSmokeEnvironment.PlatformVariable, "android"),
                (AppiumSmokeEnvironment.DeviceNameVariable, "Android Emulator"),
                (AppiumSmokeEnvironment.AppPackageVariable, "com.companyname.mauitodo"),
                (AppiumSmokeEnvironment.ServerUrlVariable, "http://user:password@127.0.0.1:4723/")),
            AppiumSmokeHost.Windows);

        Assert.False(readiness.IsReady);
        Assert.Contains(readiness.Errors, error => error.Contains("must not contain user info", StringComparison.Ordinal));
        Assert.DoesNotContain("password", readiness.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static Func<string, string?> EnvironmentWith(params (string Name, string Value)[] values)
    {
        var environment = values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);
        return name => environment.TryGetValue(name, out var value) ? value : null;
    }
}

public sealed class AppiumSmokeLocatorTests
{
    [Fact]
    public void ForAppAutomationId_MapsWindowsToUiAutomationAndAndroidToContentDescription()
    {
        var android = AppiumSmokeLocators.ForAppAutomationId(AppiumSmokePlatform.Android, "ShowModalButton");
        var windows = AppiumSmokeLocators.ForAppAutomationId(AppiumSmokePlatform.Windows, "ShowModalButton");

        Assert.IsType<ByAccessibilityId>(android.By);
        Assert.IsType<ByAccessibilityId>(windows.By);
        Assert.Equal(AppiumSmokeNativeIdentifierKind.AndroidContentDescription, android.NativeIdentifierKind);
        Assert.Equal(AppiumSmokeNativeIdentifierKind.WindowsAutomationId, windows.NativeIdentifierKind);
    }
}

public sealed class AppiumSmokeArtifactTests
{
    [Fact]
    public void GetSafeRunDirectory_StaysBelowConfiguredArtifactRoot()
    {
        var root = Path.Combine("artifacts", "TestResults", "appium");
        var runDirectory = AppiumSmokeArtifactPaths.GetSafeRunDirectory(root, "smoke-123");
        var artifact = AppiumSmokeArtifactPaths.GetSafeArtifactPath(runDirectory, "failure.txt");

        Assert.StartsWith(
            Path.GetFullPath(root) + Path.DirectorySeparatorChar,
            runDirectory,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        Assert.Equal(Path.Combine(runDirectory, "failure.txt"), artifact);
    }

    [Fact]
    public void GetSafeRunDirectory_RejectsTraversalAndUnapprovedArtifactNames()
    {
        var root = Path.Combine("artifacts", "TestResults", "appium");

        Assert.Throws<ArgumentException>(() => AppiumSmokeArtifactPaths.GetSafeRunDirectory(root, "..\\outside"));
        Assert.Throws<ArgumentException>(() => AppiumSmokeArtifactPaths.GetSafeArtifactPath("artifacts", "..\\page-source.xml"));
    }

    [Fact]
    public void Redact_RemovesSensitiveTextFromPageSourceAndLogs()
    {
        const string content = "password=\"hunter2\" token=abc123\nAuthorization: Bearer private-value";

        var redacted = AppiumSmokeRedactor.Redact(content);

        Assert.DoesNotContain("hunter2", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("private-value", redacted, StringComparison.Ordinal);
        Assert.Contains("***", redacted, StringComparison.Ordinal);
    }
}

public sealed class AppiumSmokeBoundaryTests
{
    [Fact]
    public void SmokeLane_HasNoDevFlowRuntimeOrRepairQualificationAssemblyReference()
    {
        var references = typeof(AppiumSmokeBoundaryTests).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null)
            .ToArray();

        Assert.DoesNotContain("Microsoft.Maui.DevFlow.Agent", references, StringComparer.Ordinal);
        Assert.DoesNotContain("Microsoft.Maui.DevFlow.Cli", references, StringComparer.Ordinal);
        Assert.DoesNotContain("Microsoft.Maui.DevFlow.Driver", references, StringComparer.Ordinal);
        Assert.DoesNotContain("Microsoft.Maui.DevFlow.Testing", references, StringComparer.Ordinal);
    }
}
