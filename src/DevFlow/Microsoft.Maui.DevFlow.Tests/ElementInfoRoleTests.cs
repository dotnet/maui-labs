using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Covers <see cref="ElementInfo.Role"/> inference for the native backends. The AppKit arm is the
/// interesting one: AppKit models push buttons, checkboxes and radios all as <c>NSButton</c>, so the
/// backend reports the exact runtime class name and the role switch can only resolve the shared
/// "button" role from a plain <c>NSButton</c>.
/// </summary>
public class ElementInfoRoleTests
{
    static ElementInfo Element(string type) => new() { Type = type, IsVisible = true, IsEnabled = true };

    [Fact]
    public void NSButton_InfersButtonRole_WithInteractiveTraits()
    {
        var element = Element("NSButton");

        Assert.Equal("button", element.Role);
        Assert.NotNull(element.Traits);
        Assert.Contains("interactive", element.Traits!);
        Assert.Contains("focusable", element.Traits!);
    }

    [Fact]
    public void NSSwitch_InfersSwitchRole()
    {
        var element = Element("NSSwitch");

        Assert.Equal("switch", element.Role);
        Assert.Contains("interactive", element.Traits!);
    }

    [Fact]
    public void DeclaredCapabilities_AddTraitsWhenRoleCannotInferThem()
    {
        var element = Element("CustomNativeControl");
        element.Capabilities = ["invoke", "focus", "scroll"];

        Assert.Contains("interactive", element.Traits!);
        Assert.Contains("focusable", element.Traits!);
        Assert.Contains("scrollable", element.Traits!);
    }

    [Fact]
    public void SliderRole_RemainsInteractiveWithoutDeclaredCapabilities()
    {
        var element = Element("NSSlider");

        Assert.Equal("slider", element.Role);
        Assert.Contains("interactive", element.Traits!);
    }

    [Fact]
    public void DeclaredCapabilities_TakePrecedenceOverRoleInference()
    {
        var element = Element("NSButton");
        element.Capabilities = ["select"];

        Assert.DoesNotContain("interactive", element.Traits ?? []);
        Assert.DoesNotContain("focusable", element.Traits ?? []);
    }

    [Theory]
    [InlineData("NSButton", "button")]      // AppKit
    [InlineData("UIButton", "button")]      // UIKit
    [InlineData("MaterialButton", "button")] // Android
    [InlineData("Button", "button")]        // MAUI
    public void ButtonRole_IsConsistentAcrossPlatforms(string type, string expectedRole)
        => Assert.Equal(expectedRole, Element(type).Role);

    [Theory]
    [InlineData("NSSecureTextField", "textbox")]
    [InlineData("NSSearchField", "textbox")]
    [InlineData("NSTextField", "text")]
    [InlineData("NSSlider", "slider")]
    [InlineData("NSWindow", "window")]
    public void AppKit_ExistingRoleMappings_AreUnchanged(string type, string expectedRole)
        => Assert.Equal(expectedRole, Element(type).Role);
}
