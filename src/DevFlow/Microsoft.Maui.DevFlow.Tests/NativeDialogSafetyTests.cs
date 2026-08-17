using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

public class NativeDialogSafetyTests
{
    [Fact]
    public void CreatePromptId_SameSemanticDialog_IsStable()
    {
        var first = CreateDialog();
        var second = CreateDialog();

        var firstId = NativeDialogIdentity.CreateFingerprint("macOS", "42:CoreServicesUIAgent", first);
        var secondId = NativeDialogIdentity.CreateFingerprint("macOS", "42:CoreServicesUIAgent", second);

        Assert.Equal(firstId, secondId);
    }

    [Fact]
    public void CreatePromptId_ChangedDialogText_ChangesFingerprint()
    {
        var first = CreateDialog();
        var second = CreateDialog() with
        {
            Text = ["Other App would like to find devices on your local network."]
        };

        var firstId = NativeDialogIdentity.CreateFingerprint("macOS", "42:CoreServicesUIAgent", first);
        var secondId = NativeDialogIdentity.CreateFingerprint("macOS", "42:CoreServicesUIAgent", second);

        Assert.NotEqual(firstId, secondId);
    }

    [Fact]
    public void CreatePromptId_DifferentPlatform_ChangesFingerprint()
    {
        var dialog = CreateDialog();

        var macId = NativeDialogIdentity.CreateFingerprint("macOS", "target", dialog);
        var windowsId = NativeDialogIdentity.CreateFingerprint("Windows", "target", dialog);

        Assert.NotEqual(macId, windowsId);
    }

    [Fact]
    public void CreatePromptId_DifferentNativeInstance_ChangesFingerprint()
    {
        var first = CreateDialog() with { InstanceId = "window-a" };
        var second = CreateDialog() with { InstanceId = "window-b" };

        var firstId = NativeDialogIdentity.CreateFingerprint("Android", "target", first);
        var secondId = NativeDialogIdentity.CreateFingerprint("Android", "target", second);

        Assert.NotEqual(firstId, secondId);
    }

    [Fact]
    public void IsSystemDialogForTarget_MatchingAppName_ReturnsTrue()
    {
        Assert.True(NativeDialogSafety.IsSystemDialogForTarget(CreateDialog(), "Sample App"));
    }

    [Fact]
    public void IsSystemDialogForTarget_UnrelatedApp_ReturnsFalse()
    {
        Assert.False(NativeDialogSafety.IsSystemDialogForTarget(CreateDialog(), "Other App"));
    }

    [Fact]
    public void IsSystemDialogForTarget_NameInsideAnotherWord_ReturnsFalse()
    {
        var dialog = CreateDialog() with
        {
            Title = "An application would like network access",
            Text = ["An application would like network access."]
        };

        Assert.False(NativeDialogSafety.IsSystemDialogForTarget(dialog, "App"));
    }

    [Fact]
    public void AndroidFocusedPackage_ExactComponent_ReturnsTrue()
    {
        const string state = "mFocusedApp=ActivityRecord{123 u0 com.example.app/.MainActivity t42}";

        Assert.True(AndroidAppDriver.IsFocusedPackage(state, "com.example.app"));
    }

    [Fact]
    public void AndroidFocusedPackage_PackagePrefix_ReturnsFalse()
    {
        const string state = "mFocusedApp=ActivityRecord{123 u0 com.example.app.beta/.MainActivity t42}";

        Assert.False(AndroidAppDriver.IsFocusedPackage(state, "com.example.app"));
    }

    private static AlertInfo CreateDialog()
        => new(
            "\"Sample App\" Would Like to Find Devices",
            [
                new AlertButton("Don't Allow", 0, 0, 0, 0) { Identifier = "deny" },
                new AlertButton("Allow", 0, 0, 0, 0) { Identifier = "allow" }
            ])
        {
            Text = ["\"Sample App\" would like to find devices on your local network."]
        };
}
