namespace Microsoft.Maui.DevFlow.Appium.SmokeTests;

public sealed record AppiumSmokePermissionOptions(
    string? NavigationAccessibilityId,
    string TriggerAutomationId,
    string AllowAccessibilityId,
    string? ResultAutomationId);

public sealed record AppiumSmokeOptions(
    AppiumSmokePlatform Platform,
    Uri ServerUri,
    string? DeviceName,
    string? App,
    string? AppPackage,
    string? AppActivity,
    string? BundleId,
    string? AppTopLevelWindow,
    string? Udid,
    string? PlatformVersion,
    TimeSpan CommandTimeout,
    TimeSpan ElementTimeout,
    string ArtifactRoot,
    bool CaptureScreenshots,
    AppiumSmokePermissionOptions? Permission)
{
    public IReadOnlyList<string> Validate(AppiumSmokeHost host)
    {
        var errors = new List<string>();

        if (!AppiumSmokePlatformSupport.IsHostSupported(Platform, host))
        {
            errors.Add($"{Platform} Appium smoke tests require {AppiumSmokePlatformSupport.HostRequirement(Platform)}.");
        }

        if (!ServerUri.IsAbsoluteUri
            || (!string.Equals(ServerUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ServerUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("DEVFLOW_APPIUM_SERVER_URL must be an absolute HTTP or HTTPS URL.");
        }

        if (!string.IsNullOrEmpty(ServerUri.UserInfo))
        {
            errors.Add("DEVFLOW_APPIUM_SERVER_URL must not contain user info; provide no credentials to this lane.");
        }

        if (string.IsNullOrWhiteSpace(DeviceName))
        {
            errors.Add("DEVFLOW_APPIUM_DEVICE_NAME is required.");
        }

        if (CommandTimeout <= TimeSpan.Zero)
        {
            errors.Add("DEVFLOW_APPIUM_COMMAND_TIMEOUT_SECONDS must be greater than zero.");
        }

        if (ElementTimeout <= TimeSpan.Zero)
        {
            errors.Add("DEVFLOW_APPIUM_ELEMENT_TIMEOUT_SECONDS must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(ArtifactRoot))
        {
            errors.Add("DEVFLOW_APPIUM_ARTIFACT_ROOT must not be empty.");
        }

        switch (Platform)
        {
            case AppiumSmokePlatform.Android:
                if (string.IsNullOrWhiteSpace(App) && string.IsNullOrWhiteSpace(AppPackage))
                {
                    errors.Add("Android requires DEVFLOW_APPIUM_APP or DEVFLOW_APPIUM_APP_PACKAGE to launch or attach the app.");
                }

                if (!string.IsNullOrWhiteSpace(AppActivity) && string.IsNullOrWhiteSpace(AppPackage))
                {
                    errors.Add("DEVFLOW_APPIUM_APP_ACTIVITY requires DEVFLOW_APPIUM_APP_PACKAGE.");
                }

                break;

            case AppiumSmokePlatform.IOS:
            case AppiumSmokePlatform.Mac2:
                if (string.IsNullOrWhiteSpace(App) && string.IsNullOrWhiteSpace(BundleId))
                {
                    errors.Add($"{Platform} requires DEVFLOW_APPIUM_APP or DEVFLOW_APPIUM_BUNDLE_ID to launch or attach the app.");
                }

                break;

            case AppiumSmokePlatform.Windows:
                if (string.IsNullOrWhiteSpace(App) && string.IsNullOrWhiteSpace(AppTopLevelWindow))
                {
                    errors.Add("Windows requires DEVFLOW_APPIUM_APP or DEVFLOW_APPIUM_APP_TOP_LEVEL_WINDOW to launch or attach the app.");
                }

                break;
        }

        if (Permission is not null)
        {
            if (!AppiumSmokePlatformSupport.SupportsSystemPermissionDialog(Platform))
            {
                errors.Add($"Optional OS permission smoke is not supported for {Platform}; use Android or iOS.");
            }

            if (string.IsNullOrWhiteSpace(Permission.TriggerAutomationId))
            {
                errors.Add("DEVFLOW_APPIUM_PERMISSION_TRIGGER_ID is required when DEVFLOW_APPIUM_PERMISSION_SMOKE=1.");
            }

            if (string.IsNullOrWhiteSpace(Permission.AllowAccessibilityId))
            {
                errors.Add("DEVFLOW_APPIUM_PERMISSION_ALLOW_ID is required when DEVFLOW_APPIUM_PERMISSION_SMOKE=1.");
            }
        }

        return errors;
    }
}

public enum AppiumSmokeReadinessState
{
    Disabled,
    Invalid,
    Ready,
}

public sealed record AppiumSmokeReadiness(
    AppiumSmokeReadinessState State,
    AppiumSmokeOptions? Options,
    IReadOnlyList<string> Errors)
{
    public bool IsEnabled => State is not AppiumSmokeReadinessState.Disabled;

    public bool IsReady => State is AppiumSmokeReadinessState.Ready;

    public string Reason => State switch
    {
        AppiumSmokeReadinessState.Disabled =>
            "Appium black-box smoke is disabled. Set DEVFLOW_APPIUM_SMOKE=1 and supply the documented device configuration to enable it.",
        AppiumSmokeReadinessState.Invalid =>
            $"Appium black-box smoke was explicitly enabled but is not configured: {string.Join(" ", Errors)}",
        _ => "Appium black-box smoke is ready.",
    };

    public AppiumSmokeOptions RequireReady()
    {
        if (!IsReady || Options is null)
        {
            throw new InvalidOperationException(Reason);
        }

        return Options;
    }
}

public static class AppiumSmokeEnvironment
{
    public const string EnableVariable = "DEVFLOW_APPIUM_SMOKE";
    public const string PlatformVariable = "DEVFLOW_APPIUM_PLATFORM";
    public const string ServerUrlVariable = "DEVFLOW_APPIUM_SERVER_URL";
    public const string DeviceNameVariable = "DEVFLOW_APPIUM_DEVICE_NAME";
    public const string AppVariable = "DEVFLOW_APPIUM_APP";
    public const string AppPackageVariable = "DEVFLOW_APPIUM_APP_PACKAGE";
    public const string AppActivityVariable = "DEVFLOW_APPIUM_APP_ACTIVITY";
    public const string BundleIdVariable = "DEVFLOW_APPIUM_BUNDLE_ID";
    public const string AppTopLevelWindowVariable = "DEVFLOW_APPIUM_APP_TOP_LEVEL_WINDOW";
    public const string UdidVariable = "DEVFLOW_APPIUM_UDID";
    public const string PlatformVersionVariable = "DEVFLOW_APPIUM_PLATFORM_VERSION";
    public const string ArtifactRootVariable = "DEVFLOW_APPIUM_ARTIFACT_ROOT";
    public const string CommandTimeoutVariable = "DEVFLOW_APPIUM_COMMAND_TIMEOUT_SECONDS";
    public const string ElementTimeoutVariable = "DEVFLOW_APPIUM_ELEMENT_TIMEOUT_SECONDS";
    public const string CaptureScreenshotsVariable = "DEVFLOW_APPIUM_CAPTURE_SCREENSHOTS";
    public const string PermissionSmokeVariable = "DEVFLOW_APPIUM_PERMISSION_SMOKE";
    public const string PermissionNavigationIdVariable = "DEVFLOW_APPIUM_PERMISSION_NAVIGATION_ID";
    public const string PermissionTriggerIdVariable = "DEVFLOW_APPIUM_PERMISSION_TRIGGER_ID";
    public const string PermissionAllowIdVariable = "DEVFLOW_APPIUM_PERMISSION_ALLOW_ID";
    public const string PermissionResultIdVariable = "DEVFLOW_APPIUM_PERMISSION_RESULT_ID";

    private const string DefaultServerUrl = "http://127.0.0.1:4723/";

    public static AppiumSmokeReadiness Evaluate(
        Func<string, string?>? readEnvironment = null,
        AppiumSmokeHost? host = null)
    {
        readEnvironment ??= Environment.GetEnvironmentVariable;

        if (!IsEnabled(readEnvironment(EnableVariable)))
        {
            return new AppiumSmokeReadiness(AppiumSmokeReadinessState.Disabled, null, Array.Empty<string>());
        }

        var errors = new List<string>();
        if (!AppiumSmokePlatformSupport.TryParse(readEnvironment(PlatformVariable), out var platform))
        {
            errors.Add("DEVFLOW_APPIUM_PLATFORM must be one of android, ios, mac2, or windows.");
        }

        var serverUri = ReadServerUri(readEnvironment(ServerUrlVariable), errors);
        var commandTimeout = ReadTimeout(readEnvironment(CommandTimeoutVariable), CommandTimeoutVariable, 120, errors);
        var elementTimeout = ReadTimeout(readEnvironment(ElementTimeoutVariable), ElementTimeoutVariable, 20, errors);
        var captureScreenshots = ReadBoolean(
            readEnvironment(CaptureScreenshotsVariable),
            CaptureScreenshotsVariable,
            defaultValue: true,
            errors);
        var permissionEnabled = ReadBoolean(
            readEnvironment(PermissionSmokeVariable),
            PermissionSmokeVariable,
            defaultValue: false,
            errors);

        if (errors.Count > 0)
        {
            return new AppiumSmokeReadiness(AppiumSmokeReadinessState.Invalid, null, errors);
        }

        AppiumSmokePermissionOptions? permission = null;
        if (permissionEnabled)
        {
            permission = new AppiumSmokePermissionOptions(
                ReadOptional(readEnvironment(PermissionNavigationIdVariable)),
                ReadOptional(readEnvironment(PermissionTriggerIdVariable)) ?? string.Empty,
                ReadOptional(readEnvironment(PermissionAllowIdVariable)) ?? string.Empty,
                ReadOptional(readEnvironment(PermissionResultIdVariable)));
        }

        var options = new AppiumSmokeOptions(
            platform,
            serverUri,
            ReadOptional(readEnvironment(DeviceNameVariable)),
            ReadOptional(readEnvironment(AppVariable)),
            ReadOptional(readEnvironment(AppPackageVariable)),
            ReadOptional(readEnvironment(AppActivityVariable)),
            ReadOptional(readEnvironment(BundleIdVariable)),
            ReadOptional(readEnvironment(AppTopLevelWindowVariable)),
            ReadOptional(readEnvironment(UdidVariable)),
            ReadOptional(readEnvironment(PlatformVersionVariable)),
            commandTimeout,
            elementTimeout,
            ReadOptional(readEnvironment(ArtifactRootVariable)) ?? Path.Combine("artifacts", "TestResults", "appium"),
            captureScreenshots,
            permission);

        errors.AddRange(options.Validate(host ?? AppiumSmokePlatformSupport.CurrentHost));
        return new AppiumSmokeReadiness(
            errors.Count == 0 ? AppiumSmokeReadinessState.Ready : AppiumSmokeReadinessState.Invalid,
            options,
            errors);
    }

    private static Uri ReadServerUri(string? value, List<string> errors)
    {
        var candidate = ReadOptional(value) ?? DefaultServerUrl;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            errors.Add("DEVFLOW_APPIUM_SERVER_URL must be an absolute HTTP or HTTPS URL.");
            return new Uri(DefaultServerUrl);
        }

        return uri;
    }

    private static TimeSpan ReadTimeout(string? value, string variableName, int defaultSeconds, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return TimeSpan.FromSeconds(defaultSeconds);
        }

        if (!int.TryParse(value, out var seconds) || seconds <= 0 || seconds > 600)
        {
            errors.Add($"{variableName} must be an integer between 1 and 600.");
            return TimeSpan.FromSeconds(defaultSeconds);
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static bool ReadBoolean(string? value, string variableName, bool defaultValue, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (IsEnabled(value))
        {
            return true;
        }

        if (value.Trim() == "0"
            || string.Equals(value.Trim(), "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value.Trim(), "no", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        errors.Add($"{variableName} must be 1, true, 0, false, yes, or no.");
        return defaultValue;
    }

    private static bool IsEnabled(string? value) =>
        value?.Trim() == "1"
        || string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);

    private static string? ReadOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
