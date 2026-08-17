using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

public class NativeDialogSafetyTests
{
    [Fact]
    public void CreatePromptId_SameSemanticDialog_IsStable()
    {
        var first = CreateDialog();
        var second = CreateDialog();

        var firstId = NativeDialogSafety.CreateFingerprint(42, "CoreServicesUIAgent", first);
        var secondId = NativeDialogSafety.CreateFingerprint(42, "CoreServicesUIAgent", second);

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

        var firstId = NativeDialogSafety.CreateFingerprint(42, "CoreServicesUIAgent", first);
        var secondId = NativeDialogSafety.CreateFingerprint(42, "CoreServicesUIAgent", second);

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
