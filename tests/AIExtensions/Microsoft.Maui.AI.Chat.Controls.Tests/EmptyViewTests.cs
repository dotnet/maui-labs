using Microsoft.Maui.Controls.Xaml;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

/// <summary>
/// Tests for the bindable <see cref="CopilotChatView.EmptyViewTemplate"/> that lets a host
/// supply a custom empty-state view (welcome slot) shown while the conversation is empty.
/// </summary>
public class EmptyViewTests
{
    [Fact]
    public void EmptyViewTemplate_NullByDefault()
    {
        var control = CreateControl();
        if (control == null)
            return; // Skip if MAUI XAML runtime unavailable in test host

        Assert.Null(control.EmptyViewTemplate);
    }

    [Fact]
    public void EmptyViewTemplate_CanBeSet_AndRoundTrips()
    {
        var control = CreateControl();
        if (control == null)
            return;

        var template = new DataTemplate(() => new Label { Text = "Nothing here yet" });
        control.EmptyViewTemplate = template;

        Assert.Same(template, control.EmptyViewTemplate);
    }

    [Fact]
    public void EmptyViewTemplate_SettingDoesNotThrow_WhenTemplateNotApplied()
    {
        var control = CreateControl();
        if (control == null)
            return;

        // No control template applied in the unit-test host, so PART_EmptyView is null.
        // Setting the property must not throw even though there is no host to fill.
        var ex = Record.Exception(() =>
            control.EmptyViewTemplate = new DataTemplate(() => new Label { Text = "Empty" }));

        Assert.Null(ex);
    }

    [Fact]
    public void EmptyViewTemplate_ClearingBackToNull_DoesNotThrow()
    {
        var control = CreateControl();
        if (control == null)
            return;

        control.EmptyViewTemplate = new DataTemplate(() => new Label());
        var ex = Record.Exception(() => control.EmptyViewTemplate = null);

        Assert.Null(ex);
        Assert.Null(control.EmptyViewTemplate);
    }

    /// <summary>
    /// Creates a CopilotChatView, returning null if MAUI XAML runtime is unavailable
    /// (InitializeComponent requires the full MAUI platform host).
    /// </summary>
    private static CopilotChatView? CreateControl()
    {
        try
        {
            return new CopilotChatView();
        }
        catch (Exception ex) when (ex is XamlParseException or InvalidOperationException)
        {
            return null;
        }
    }
}
