using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

public class AndroidDialogWindowSafetyTests
{
    [Fact]
    public void IsUnobscuredDialog_TargetWindowOwnerMatches_ReturnsTrue()
    {
        Assert.True(IsSafe(TargetWindow()));
    }

    [Fact]
    public void IsUnobscuredDialog_FocusedAppMatchesButWindowUidDoesNot_ReturnsFalse()
    {
        var state = TargetWindow().Replace("mOwnerUid=10042", "mOwnerUid=10099")
            + "\nmFocusedApp=ActivityRecord{123 u0 com.example.app/.MainActivity t42}";

        Assert.False(IsSafe(state));
    }

    [Fact]
    public void IsUnobscuredDialog_FocusedAppMatchesButHierarchyPackageDoesNot_ReturnsFalse()
    {
        var dialog = Dialog() with { SourceProcessName = "com.other.app" };
        Assert.False(AndroidDialogWindowSafety.IsUnobscuredDialog(
            TargetWindow(), dialog, "com.example.app", 10042));
    }

    [Theory]
    [InlineData("mOwnerUid=10042", "")]
    [InlineData("mDisplayId=0", "")]
    [InlineData("mDisplayId=0", "mDisplayId=1")]
    [InlineData("isOnScreen=true", "isOnScreen=false")]
    [InlineData("isOnScreen=true", "")]
    [InlineData("frame=[0,0][1080,2400]", "")]
    [InlineData("mCurrentFocus=Window{abc123", "mCurrentFocus=Window{def456")]
    public void IsUnobscuredDialog_OwnershipOrGeometryUnverifiable_ReturnsFalse(string from, string to)
    {
        Assert.False(IsSafe(TargetWindow().Replace(from, to)));
    }

    [Fact]
    public void IsUnobscuredDialog_NonFocusableOverlayCoversButton_ReturnsFalse()
    {
        var overlay = Window(0, "def456", "com.other.app", 10099, "[50,100][400,300]")
            + "\n    fl=NOT_FOCUSABLE NOT_TOUCH_MODAL";
        Assert.False(IsSafe(overlay + TargetWindow(1)));
    }

    [Fact]
    public void IsUnobscuredDialog_OverlayOutsideButton_ReturnsTrue()
    {
        var overlay = Window(0, "def456", "com.android.systemui", 10099, "[0,0][1080,80]");
        Assert.True(IsSafe(overlay + TargetWindow(1)));
    }

    [Fact]
    public void IsUnobscuredDialog_OverlayVisibilityUnknown_ReturnsFalse()
    {
        var overlay = Window(0, "def456", "com.other.app", 10099, "[50,100][400,300]")
            .Replace("isOnScreen=true", "");
        Assert.False(IsSafe(overlay + TargetWindow(1)));
    }

    [Fact]
    public void IsUnobscuredDialog_HiddenOverlay_ReturnsTrue()
    {
        var overlay = Window(0, "def456", "com.other.app", 10099, "[0,0][1080,2400]")
            .Replace("isOnScreen=true", "isOnScreen=false");
        Assert.True(IsSafe(overlay + TargetWindow(1)));
    }

    [Fact]
    public void IsUnobscuredDialog_OverlayWithUnknownBounds_ReturnsFalse()
    {
        var overlay = Window(0, "def456", "com.other.app", 10099, "[0,0][1080,2400]")
            .Replace("frame=[0,0][1080,2400]", "");
        Assert.False(IsSafe(overlay + TargetWindow(1)));
    }

    [Theory]
    [InlineData("package:com.example.app uid:10042", 10042)]
    [InlineData("package:com.example.app.beta uid:10042", null)]
    [InlineData("package:com.example.app", null)]
    [InlineData("package:com.example.app uid:9999999999999999", null)]
    [InlineData("package:com.example.app uid:10042\npackage:com.example.app uid:10043", null)]
    public void GetPackageUid_RequiresUniqueExactPackage(string output, int? expected)
    {
        Assert.Equal(expected, AndroidDialogWindowSafety.GetPackageUid(output, "com.example.app"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PressAlertButtonSafelyAsync_RevalidatesWindowBeforeTap(bool overlayAppears)
    {
        using var driver = new WindowDriver();
        var reviewed = await driver.DetectAlertAsync();
        Assert.NotNull(reviewed);
        Assert.Equal("abc123", reviewed.InstanceId);
        if (overlayAppears)
        {
            driver.WindowState = Window(0, "def456", "com.other.app", 10099, "[0,0][1080,2400]")
                + TargetWindow(1);
        }

        var result = await driver.PressAlertButtonSafelyAsync(
            reviewed, "Allow", "com.example.app", "Sample App");

        Assert.Equal(!overlayAppears, result.Success);
        Assert.Equal(overlayAppears ? 0 : 1, driver.Taps.Count);
        if (!overlayAppears)
            Assert.Equal(["shell", "input", "tap", "200", "200"], Assert.Single(driver.Taps));
    }

    [Fact]
    public async Task PressAlertButtonSafelyAsync_ChangedWindowOwner_RefusesTap()
    {
        using var driver = new WindowDriver();
        var reviewed = await driver.DetectAlertAsync();
        Assert.NotNull(reviewed);
        driver.WindowState = TargetWindow().Replace("mOwnerUid=10042", "mOwnerUid=10099");

        var result = await driver.PressAlertButtonSafelyAsync(
            reviewed, "Allow", "com.example.app", "Sample App");

        Assert.False(result.Success);
        Assert.Empty(driver.Taps);
    }

    [Fact]
    public async Task PressAlertButtonSafelyAsync_ChangedDialogText_RefusesTap()
    {
        using var driver = new WindowDriver();
        var reviewed = await driver.DetectAlertAsync();
        Assert.NotNull(reviewed);
        driver.Title = "Different permission";

        var result = await driver.PressAlertButtonSafelyAsync(
            reviewed, "Allow", "com.example.app", "Sample App");

        Assert.False(result.Success);
        Assert.Empty(driver.Taps);
    }

    [Fact]
    public async Task DetectAlertAsync_PermissionDialog_IgnoresUnrelatedAppTextInFingerprint()
    {
        using var driver = new WindowDriver
        {
            WindowState = TargetWindow().Replace("com.example.app", "com.google.android.permissioncontroller"),
            Title = "Allow Sample App to access your contacts?",
            BackgroundText = "Before"
        };
        var reviewed = await driver.DetectAlertAsync();
        Assert.NotNull(reviewed);
        Assert.Equal(["Allow Sample App to access your contacts?", "Allow"], reviewed.Text);

        driver.BackgroundText = "After";
        var current = await driver.DetectAlertAsync();
        Assert.NotNull(current);
        Assert.Equal(
            NativeDialogIdentity.CreateFingerprint(driver.Platform, driver.Serial ?? string.Empty, reviewed),
            NativeDialogIdentity.CreateFingerprint(driver.Platform, driver.Serial ?? string.Empty, current));

        driver.Title = "Allow Other App to access your contacts?";
        var changed = await driver.DetectAlertAsync();
        Assert.NotNull(changed);
        Assert.NotEqual(
            NativeDialogIdentity.CreateFingerprint(driver.Platform, driver.Serial ?? string.Empty, reviewed),
            NativeDialogIdentity.CreateFingerprint(driver.Platform, driver.Serial ?? string.Empty, changed));
    }

    [Fact]
    public async Task DetectAlertAsync_PermissionDialog_RetriesUntilWindowIdentityIsStable()
    {
        var appWindow = TargetWindow();
        var permissionWindow = appWindow.Replace(
            "com.example.app",
            "com.google.android.permissioncontroller");
        using var driver = new WindowDriver
        {
            BackgroundText = "Background",
            Title = "Allow Sample App to access your contacts?"
        };
        driver.WindowStates.Enqueue(appWindow);
        driver.WindowStates.Enqueue(permissionWindow);
        driver.WindowStates.Enqueue(permissionWindow);
        driver.WindowStates.Enqueue(permissionWindow);

        var dialog = await driver.DetectAlertAsync();

        Assert.NotNull(dialog);
        Assert.Equal("abc123", dialog.InstanceId);
        Assert.Empty(driver.WindowStates);
    }

    [Theory]
    [InlineData(10042, "Sample App", true)]
    [InlineData(10099, "Sample App", false)]
    [InlineData(10042, "Other App", false)]
    public async Task IsDialogAttributedToTargetAsync_PermissionDialog_VerifiesControllerUidAndAppName(
        int uid, string appName, bool expected)
    {
        using var driver = new WindowDriver();
        var state = TargetWindow().Replace("com.example.app", "com.google.android.permissioncontroller")
            .Replace("mOwnerUid=10042", $"mOwnerUid={uid}");
        var dialog = Dialog() with
        {
            Title = "Allow Sample App to access your contacts?",
            SourceProcessName = "com.google.android.permissioncontroller",
            IsSystemDialog = true
        };
        driver.WindowState = state;

        Assert.Equal(expected, await driver.IsDialogAttributedToTargetAsync(dialog, "com.example.app", appName));
    }

    [Fact]
    public async Task PressAlertButtonSafelyAsync_CanceledDuringOwnerVerification_RefusesTap()
    {
        using var driver = new WindowDriver();
        var reviewed = await driver.DetectAlertAsync();
        Assert.NotNull(reviewed);
        using var canceled = new CancellationTokenSource();
        driver.AfterUidLookup = canceled.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => driver.PressAlertButtonSafelyAsync(
            reviewed, "Allow", "com.example.app", "Sample App", canceled.Token));
        Assert.Empty(driver.Taps);
    }

    private static bool IsSafe(string state)
        => AndroidDialogWindowSafety.IsUnobscuredDialog(state, Dialog(), "com.example.app", 10042);

    private static AlertInfo Dialog()
        => new("Permission", [new AlertButton("Allow", 100, 150, 200, 100)])
        {
            InstanceId = "abc123",
            SourceProcessName = "com.example.app"
        };

    private static string TargetWindow(int index = 0)
        => Window(index, "abc123", "com.example.app", 10042, "[0,0][1080,2400]")
            + "\n  mCurrentFocus=Window{abc123 u0 com.example.app/.MainActivity}\n";

    private static string Window(int index, string token, string package, int uid, string frame)
        => $"\n  Window #{index} Window{{{token} u0 {package}/.MainActivity}}:\n"
            + $"    mDisplayId=0\n    mOwnerUid={uid} package={package}\n"
            + $"    Frames: parent=[0,0][1080,2400] frame={frame} last={frame}\n"
            + "    isOnScreen=true\n";

    private sealed class WindowDriver : AndroidAppDriver
    {
        public string WindowState { get; set; } = TargetWindow();
        public string Title { get; set; } = "Permission";
        public string? BackgroundText { get; set; }
        public Queue<string> WindowStates { get; } = [];
        public List<string[]> Taps { get; } = [];
        public Action? AfterUidLookup { get; set; }

        internal override Task<string> RunAdbWithOutputAsync(params string[] arguments)
        {
            if (arguments.SequenceEqual(["shell", "dumpsys", "window", "-a"]))
                return Task.FromResult(WindowStates.TryDequeue(out var state) ? state : WindowState);
            if (arguments.SequenceEqual(["shell", "cmd", "package", "list", "packages", "-U", "com.example.app"]))
            {
                AfterUidLookup?.Invoke();
                return Task.FromResult("package:com.example.app uid:10042");
            }
            if (arguments.SequenceEqual(["shell", "cmd", "package", "list", "packages", "-U", "com.google.android.permissioncontroller"]))
                return Task.FromResult("package:com.google.android.permissioncontroller uid:10042");
            if (arguments.SequenceEqual(["shell", "uiautomator", "dump", "/sdcard/window_dump.xml"]))
                return Task.FromResult(string.Empty);
            if (arguments.SequenceEqual(["shell", "cat", "/sdcard/window_dump.xml"]))
            {
                if (BackgroundText is not null)
                {
                    return Task.FromResult($"""
                        <hierarchy>
                          <node package="com.example.app" text="{BackgroundText}" />
                          <node package="com.google.android.permissioncontroller" class="android.widget.TextView" text="{Title}" />
                          <node package="com.google.android.permissioncontroller" class="android.widget.Button" clickable="true" text="Allow" bounds="[100,150][300,250]" />
                        </hierarchy>
                        """);
                }
                return Task.FromResult($"""
                    <hierarchy>
                      <node resource-id="android:id/parentPanel" package="com.example.app">
                        <node resource-id="android:id/alertTitle" package="com.example.app" text="{Title}" />
                        <node resource-id="android:id/buttonPanel" package="com.example.app">
                          <node class="android.widget.Button" package="com.example.app" text="Allow" bounds="[100,150][300,250]" />
                        </node>
                      </node>
                    </hierarchy>
                    """);
            }
            if (arguments.Length == 5 && arguments.Take(3).SequenceEqual(["shell", "input", "tap"]))
            {
                Taps.Add(arguments);
                return Task.FromResult(string.Empty);
            }

            throw new InvalidOperationException($"Unexpected adb command: {string.Join(" ", arguments)}");
        }
    }
}
