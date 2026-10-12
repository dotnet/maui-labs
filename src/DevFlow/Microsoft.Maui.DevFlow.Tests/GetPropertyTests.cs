using System.Reflection;
using System.Text.Json;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.Handlers;

namespace Microsoft.Maui.DevFlow.Tests;

public class GetPropertyTests
{
    public static TheoryData<string, bool> PlatformBooleanPaths
    {
        get
        {
            var data = new TheoryData<string, bool>();
            foreach (var property in new[]
            {
                "IsActive", "IsKeyboardFocusWithin", "ShowActivated", "ShowInTaskbar"
            })
            {
                foreach (var value in new[] { false, true })
                {
                    data.Add($"Handler.PlatformView.{property}", value);
                    data.Add($"hAnDlEr.pLaTfOrMvIeW.{property.ToLowerInvariant()}", value);
                }
            }
            return data;
        }
    }

    [Fact]
    public void GenericHandler_PlatformViewReflection_IsAmbiguousButInterfaceIsUnambiguous()
    {
        var window = new Window();
        var platform = new TestPlatformWindow(false);
        var handler = new TestWindowHandler(platform);
        handler.SetVirtualView(window);

        Assert.Same(handler, window.Handler);
        Assert.Same(platform, ((IElementHandler)handler).PlatformView);
        Assert.Throws<AmbiguousMatchException>(() => handler.GetType().GetProperty(
            nameof(IElementHandler.PlatformView),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));
    }

    [Theory]
    [MemberData(nameof(PlatformBooleanPaths))]
    public async Task HandleProperty_GenericHandlerPlatformBoolean_ReturnsFormattedValue(
        string path, bool value)
    {
        var window = new Window();
        var handler = new TestWindowHandler(new TestPlatformWindow(value));
        handler.SetVirtualView(window);
        using var service = new PropertyAgentService(window);

        var response = await service.ReadAsync(path);

        AssertValue(response, service.ElementId, path, value.ToString());
        Assert.Equal(1, service.DispatchCount);
    }

    [Theory]
    [InlineData("Handler")]
    [InlineData("Handler.PlatformView.IsActive")]
    [InlineData("Missing")]
    [InlineData("Missing.Child")]
    [InlineData("Title.Length")]
    public async Task HandleProperty_NullOrMissingOrdinaryPath_ReturnsNotFound(string path)
    {
        using var service = new PropertyAgentService(new Window());

        var response = await service.ReadAsync(path);

        AssertNotFound(response, service.ElementId, path);
    }

    [Theory]
    [InlineData("Handler.PlatformView.Missing")]
    [InlineData("Handler.PlatformView.Missing.Child")]
    [InlineData("Handler.PlatformView.OptionalText")]
    [InlineData("Handler.PlatformView.OptionalText.Length")]
    public async Task HandleProperty_NullOrMissingPlatformProperty_ReturnsNotFound(string path)
    {
        var window = new Window();
        new TestWindowHandler(new TestPlatformWindow(false)).SetVirtualView(window);
        using var service = new PropertyAgentService(window);

        var response = await service.ReadAsync(path);

        AssertNotFound(response, service.ElementId, path);
    }

    [Theory]
    [InlineData("BindingContext.PlatformView")]
    [InlineData("BindingContext.PlatformView.IsActive")]
    [InlineData("bInDiNgCoNtExT.pLaTfOrMvIeW.iSaCtIvE")]
    public async Task HandleProperty_UnconnectedGenericHandlerPlatformView_ReturnsNotFound(string path)
    {
        var handler = new TestWindowHandler(new TestPlatformWindow(false));
        Assert.Null(((IElementHandler)handler).PlatformView);
        var window = new Window { BindingContext = handler };
        using var service = new PropertyAgentService(window);

        var response = await service.ReadAsync(path);

        AssertNotFound(response, service.ElementId, path);
    }

    [Fact]
    public async Task HandleProperty_MissingElement_ReturnsNotFound()
    {
        using var service = new PropertyAgentService(new Window());

        var response = await service.ReadAsync("Title", "missing-element");

        AssertNotFound(response, "missing-element", "Title");
    }

    [Theory]
    [InlineData("tExT", "visible")]
    [InlineData("Text.Length", "7")]
    [InlineData("iSeNaBlEd", "False")]
    [InlineData("HorizontalTextAlignment", "Center")]
    [InlineData("BindingContext.PlatformView.IsActive", "True")]
    public async Task HandleProperty_OrdinaryProperties_PreserveTraversalAndFormatting(
        string path, string expected)
    {
        var label = new Label
        {
            Text = "visible",
            IsEnabled = false,
            HorizontalTextAlignment = TextAlignment.Center,
            BindingContext = new OrdinaryPlatformOwner()
        };
        using var service = new PropertyAgentService(label);

        var response = await service.ReadAsync(path);

        AssertValue(response, service.ElementId, path, expected);
    }

    [Theory]
    [InlineData("Text", true, "[REDACTED]")]
    [InlineData("tExT", true, "[REDACTED]")]
    [InlineData("Text", false, "secret")]
    [InlineData("tExT", false, "secret")]
    public async Task HandleProperty_EntryText_PreservesPasswordRedaction(
        string path, bool isPassword, string expected)
    {
        var entry = new Entry { Text = "secret", IsPassword = isPassword };
        using var service = new PropertyAgentService(entry);

        var response = await service.ReadAsync(path);

        AssertValue(response, service.ElementId, path, expected);
    }

    [Theory]
    [InlineData("Handler.PlatformView.Text", true, "secret")]
    [InlineData("hAnDlEr.pLaTfOrMvIeW.tExT", true, "secret")]
    [InlineData("Handler.PlatformView.Text", true, null)]
    [InlineData("hAnDlEr.pLaTfOrMvIeW.tExT", true, null)]
    [InlineData("Handler.PlatformView.Text", false, "visible")]
    [InlineData("hAnDlEr.pLaTfOrMvIeW.tExT", false, "visible")]
    [InlineData("Handler.PlatformView.Text", false, null)]
    [InlineData("hAnDlEr.pLaTfOrMvIeW.tExT", false, null)]
    public async Task HandleProperty_NativeEntryText_DoesNotExposePassword(
        string path, bool isPassword, string? text)
    {
        var entry = new Entry { Text = text, IsPassword = isPassword };
        var platform = new TestPlatformEntry(text);
        var handler = new TestEntryHandler(platform);
        handler.SetVirtualView(entry);
        Assert.Same(handler, entry.Handler);
        Assert.Same(platform, ((IElementHandler)handler).PlatformView);
        Assert.Throws<AmbiguousMatchException>(() => handler.GetType().GetProperty(
            nameof(IElementHandler.PlatformView),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));
        using var service = new PropertyAgentService(entry);
        Assert.Equal(0, platform.GetterCalls);
        Assert.Equal(0, platform.ToStringCalls);

        var response = await service.ReadAsync(path);

        if (isPassword)
            AssertValue(response, service.ElementId, path, "[REDACTED]");
        else if (text is null)
            AssertNotFound(response, service.ElementId, path);
        else
            AssertValue(response, service.ElementId, path, text);
        Assert.Equal(isPassword ? 0 : 1, platform.GetterCalls);
        Assert.Equal(0, platform.ToStringCalls);
    }

    [Theory]
    [InlineData("Handler.PlatformView", "native:secret", true)]
    [InlineData("hAnDlEr.pLaTfOrMvIeW", "native:secret", true)]
    [InlineData("Handler.PlatformView.AttributedText", "secret", false)]
    [InlineData("Handler.PlatformView.EditableText", "secret", false)]
    [InlineData("hAnDlEr.pLaTfOrMvIeW.aTtRiBuTeDtExT", "secret", false)]
    [InlineData("Handler.PlatformView.Text.Length", "6", false)]
    [InlineData("Handler.PlatformView.IsEnabled", "True", false)]
    public async Task HandleProperty_PasswordNativeSubtree_DoesNotInvokeGettersOrFormatting(
        string path, string unmaskedValue, bool formatsPlatform)
    {
        var entry = new Entry { Text = "secret", IsPassword = true };
        var platform = new TestPlatformEntry(entry.Text);
        new TestEntryHandler(platform).SetVirtualView(entry);
        using var service = new PropertyAgentService(entry);
        Assert.Equal(0, platform.GetterCalls);
        Assert.Equal(0, platform.ToStringCalls);

        var masked = await service.ReadAsync(path);

        AssertValue(masked, service.ElementId, path, "[REDACTED]");
        Assert.Equal(0, platform.GetterCalls);
        Assert.Equal(0, platform.ToStringCalls);

        // The same live handler must expose the value when it is no longer a password entry.
        entry.IsPassword = false;
        var unmasked = await service.ReadAsync(path);

        AssertValue(unmasked, service.ElementId, path, unmaskedValue);
        Assert.Equal(formatsPlatform ? 0 : 1, platform.GetterCalls);
        Assert.Equal(formatsPlatform ? 1 : 0, platform.ToStringCalls);
    }

    [Theory]
    [InlineData("BindingContext.PlatformView")]
    [InlineData("bInDiNgCoNtExT.pLaTfOrMvIeW.aTtRiBuTeDtExT")]
    public async Task HandleProperty_PasswordHandlerReachedThroughAnotherElement_IsRedacted(string path)
    {
        var entry = new Entry { Text = "secret", IsPassword = true };
        var platform = new TestPlatformEntry(entry.Text);
        var handler = new TestEntryHandler(platform);
        handler.SetVirtualView(entry);
        using var service = new PropertyAgentService(new Label { BindingContext = handler });

        var response = await service.ReadAsync(path);

        AssertValue(response, service.ElementId, path, "[REDACTED]");
        Assert.Equal(0, platform.GetterCalls);
        Assert.Equal(0, platform.ToStringCalls);
    }

    [Theory]
    [InlineData("BindingContext.PlatformView")]
    [InlineData("bInDiNgCoNtExT.pLaTfOrMvIeW.tExT")]
    public async Task HandleProperty_PasswordEntryWithNullPlatform_ReturnsNotFound(string path)
    {
        var handler = new TestEntryHandler(new TestPlatformEntry("secret"));
        Assert.Null(((IElementHandler)handler).PlatformView);
        using var service = new PropertyAgentService(new Entry
        {
            IsPassword = true,
            Text = "secret",
            BindingContext = handler
        });

        var response = await service.ReadAsync(path);

        AssertNotFound(response, service.ElementId, path);
    }

    [Theory]
    [InlineData("Handler.PlatformView.Text")]
    [InlineData("hAnDlEr.pLaTfOrMvIeW.tExT")]
    public async Task HandleProperty_PasswordEntryWithNullHandler_ReturnsNotFound(string path)
    {
        using var service = new PropertyAgentService(new Entry { IsPassword = true, Text = "secret" });

        var response = await service.ReadAsync(path);

        AssertNotFound(response, service.ElementId, path);
    }

    private static void AssertValue(HttpResponse response, string id, string property, string expected)
    {
        Assert.Equal(200, response.StatusCode);
        using var body = JsonDocument.Parse(Assert.IsType<string>(response.Body));
        Assert.Equal(id, body.RootElement.GetProperty("id").GetString());
        Assert.Equal(property, body.RootElement.GetProperty("property").GetString());
        Assert.Equal(expected, body.RootElement.GetProperty("value").GetString());
    }

    private static void AssertNotFound(HttpResponse response, string id, string property)
    {
        Assert.Equal(404, response.StatusCode);
        using var body = JsonDocument.Parse(Assert.IsType<string>(response.Body));
        Assert.Equal($"Property '{property}' not found on element '{id}'",
            body.RootElement.GetProperty("error").GetString());
    }

    private sealed class PropertyAgentService : MauiDevFlowAgentService
    {
        public string ElementId { get; }
        public int DispatchCount { get; private set; }

        public PropertyAgentService(Element element)
            : base(new AgentOptions { EnableNetworkMonitoring = false })
        {
            _app = new TestApplication(element);
            var walker = new VisualTreeWalker();
            walker.WalkTree(_app);
            ElementId = Assert.IsType<string>(walker.GetIdForElement(element));
        }

        public Task<HttpResponse> ReadAsync(string property, string? id = null)
            => HandleProperty(new HttpRequest
            {
                Method = "GET",
                Path = $"/api/v1/ui/elements/{id ?? ElementId}/properties/{property}",
                RouteParams =
                {
                    ["id"] = id ?? ElementId,
                    ["name"] = property
                }
            });

        protected override bool IsMainThreadDispatchRequired() => true;

        protected override Task<T> DispatchViaMainThreadAsync<T>(Func<T> func)
        {
            DispatchCount++;
            return Task.FromResult(func());
        }
    }

    private sealed class TestApplication(Element element) : Application, IVisualTreeElement
    {
        IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => [element];
        IVisualTreeElement? IVisualTreeElement.GetVisualParent() => null;
    }

    private sealed class TestWindowHandler(TestPlatformWindow platform)
        : ElementHandler<IWindow, TestPlatformWindow>(new PropertyMapper<IWindow, TestWindowHandler>())
    {
        protected override TestPlatformWindow CreatePlatformElement() => platform;
    }

    // These are managed test values, not native WPF activation or focus evidence.
    private sealed class TestPlatformWindow(bool value)
    {
        public bool IsActive => value;
        public bool IsKeyboardFocusWithin => value;
        public bool ShowActivated => value;
        public bool ShowInTaskbar => value;
        public string? OptionalText => null;
    }

    private sealed class OrdinaryPlatformOwner
    {
        public TestPlatformWindow PlatformView { get; } = new(true);
    }

    private sealed class TestEntryHandler(TestPlatformEntry platform)
        : ViewHandler<IEntry, TestPlatformEntry>(new PropertyMapper<IEntry, TestEntryHandler>())
    {
        protected override TestPlatformEntry CreatePlatformView() => platform;
    }

    private sealed class TestPlatformEntry(string? text)
    {
        public int GetterCalls { get; private set; }
        public int ToStringCalls { get; private set; }

        public string? Text => ReadText();
        public string? AttributedText => ReadText();
        public string? EditableText => ReadText();
        public bool IsEnabled
        {
            get
            {
                GetterCalls++;
                return true;
            }
        }

        private string? ReadText()
        {
            GetterCalls++;
            return text;
        }

        public override string ToString()
        {
            ToStringCalls++;
            return $"native:{text}";
        }
    }
}
