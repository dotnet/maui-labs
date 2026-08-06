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
        var control = new CopilotChatView();

        Assert.Null(control.EmptyViewTemplate);
    }

    [Fact]
    public void EmptyViewTemplate_CanBeSet_AndRoundTrips()
    {
        var control = new CopilotChatView();

        var template = new DataTemplate(() => new Label { Text = "Nothing here yet" });
        control.EmptyViewTemplate = template;

        Assert.Same(template, control.EmptyViewTemplate);
    }

    [Fact]
    public void EmptyViewTemplate_SettingDoesNotThrow_WhenTemplateNotApplied()
    {
        var control = new CopilotChatView();

        // No control template applied in the unit-test host, so PART_EmptyView is null.
        // Setting the property must not throw even though there is no host to fill.
        var ex = Record.Exception(() =>
            control.EmptyViewTemplate = new DataTemplate(() => new Label { Text = "Empty" }));

        Assert.Null(ex);
    }

    [Fact]
    public void EmptyViewTemplate_ClearingBackToNull_DoesNotThrow()
    {
        var control = new CopilotChatView();

        control.EmptyViewTemplate = new DataTemplate(() => new Label());
        var ex = Record.Exception(() => control.EmptyViewTemplate = null);

        Assert.Null(ex);
        Assert.Null(control.EmptyViewTemplate);
    }

    [Fact]
    public void SettingBindingContext_WithEmptyViewTemplate_DoesNotThrow()
    {
        var control = new CopilotChatView();

        // Custom empty content binds against the control's data context (not the
        // templated parent). Changing the BindingContext must re-propagate safely,
        // even when the control template has not been applied (parts are null).
        control.EmptyViewTemplate = new DataTemplate(() => new Label { Text = "Empty" });

        var ex = Record.Exception(() => control.BindingContext = new { Name = "vm" });

        Assert.Null(ex);
    }

}
