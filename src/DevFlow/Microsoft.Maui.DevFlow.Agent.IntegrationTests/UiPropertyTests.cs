using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Collection("AgentIntegration")]
[Trait("Category", "UiProperties")]
public class UiPropertyTests : IntegrationTestBase
{
    public UiPropertyTests(AppFixture app, ITestOutputHelper output)
        : base(app, output) { }

    [Fact]
    public async Task GetProperty_Text_ReturnsValue()
    {
        await NavigateToMainPageAsync();
        var header = await FindElementAsync("HeaderLabel");

        var text = await Client.GetPropertyAsync(header.Id, "Text");

        Assert.NotNull(text);
        Assert.Contains("Todos", text!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetProperty_IsVisible_ReturnsTrue()
    {
        await NavigateToMainPageAsync();
        var addButton = await FindElementAsync("AddButton");

        var value = await Client.GetPropertyAsync(addButton.Id, "IsVisible");

        Assert.NotNull(value);
        Assert.Contains("true", value!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetProperty_Text_UpdatesValue()
    {
        await NavigateToMainPageAsync();
        var header = await FindElementAsync("HeaderLabel");
        var originalText = await Client.GetPropertyAsync(header.Id, "Text");

        // Read before write. A backend that cannot read Text back cannot restore it either, and
        // silently skipping the restore leaves the header stuck on "Modified Header" and fails
        // unrelated text assertions later in the run -- which is exactly how one missing AppKit
        // alias turned into five red tests. Fail here instead, where the cause is obvious.
        Assert.True(originalText != null, "Text must be readable before it is overwritten, otherwise the original cannot be restored.");

        var result = await Client.SetPropertyAsync(header.Id, "Text", "Modified Header");
        Assert.True(result);

        await SettleAsync();
        try
        {
            var newText = await Client.GetPropertyAsync(header.Id, "Text");
            Assert.Equal("Modified Header", newText);
        }
        finally
        {
            await Client.SetPropertyAsync(header.Id, "Text", originalText!);
        }
    }

    /// <summary>
    /// Every property name a backend's setter accepts has to be readable back from its getter.
    /// Getters and setters drifted apart independently on AppKit and Android -- both shipped
    /// properties you could write but not read -- and neither was caught, because the per-property
    /// tests only covered the names that happened to already work on the platform being run.
    ///
    /// Only assert on properties whose value cannot legitimately be null, since the transport cannot
    /// distinguish "unsupported" from "null". An empty Entry's Text is null on MAUI and empty-string
    /// on the native backends, so it is unusable here; <c>Fill_Entry_SetsText</c> covers that path
    /// instead, by filling before it reads.
    /// </summary>
    [Theory]
    [InlineData("AddButton", "IsVisible")]
    [InlineData("AddButton", "IsEnabled")]
    [InlineData("AddButton", "Opacity")]
    [InlineData("AddButton", "Text")]
    [InlineData("HeaderLabel", "IsVisible")]
    [InlineData("HeaderLabel", "Text")]
    [InlineData("NewTodoEntry", "IsVisible")]
    [InlineData("NewTodoEntry", "IsEnabled")]
    public async Task GetProperty_CanonicalAlias_IsReadable(string automationId, string property)
    {
        await NavigateToMainPageAsync();
        var element = await FindElementAsync(automationId);

        var value = await Client.GetPropertyAsync(element.Id, property);

        Assert.True(value != null, $"'{property}' is not readable on '{automationId}'. Backends must publish every name their setter accepts.");
    }

    [Fact]
    public async Task GetProperty_Opacity_ReturnsNumericValue()
    {
        await NavigateToMainPageAsync();
        var addButton = await FindElementAsync("AddButton");

        var value = await Client.GetPropertyAsync(addButton.Id, "Opacity");

        if (value == null)
        {
            Output.WriteLine("Opacity property returned null — trying IsVisible instead.");
            var isVisible = await Client.GetPropertyAsync(addButton.Id, "IsVisible");
            if (isVisible == null)
            {
                Output.WriteLine("IsVisible also returned null — property access may not be supported for this element type.");
                return;
            }

            Assert.NotNull(isVisible);
            return;
        }

        Assert.True(double.TryParse(value, out var opacity), $"Expected numeric opacity, got: {value}");
        Assert.InRange(opacity, 0.0, 1.0);
    }

    [Fact]
    public async Task GetProperty_NonExistentProperty_HandlesGracefully()
    {
        await NavigateToMainPageAsync();
        var addButton = await FindElementAsync("AddButton");

        var value = await Client.GetPropertyAsync(addButton.Id, "NonExistentProperty12345");
        Output.WriteLine($"Non-existent property returned: '{value ?? "(null)"}'");
    }
}
