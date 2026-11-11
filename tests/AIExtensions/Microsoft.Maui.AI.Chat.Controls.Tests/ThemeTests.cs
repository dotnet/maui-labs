using System.Reflection;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public sealed class ThemeTests : IDisposable
{
    public ThemeTests() => Application.Current = null;

    public void Dispose() => Application.Current = null;

    [Fact]
    public void Theme_DefinesEveryDocumentedKey()
    {
        var theme = new AIChatTheme();
        var keys = typeof(AIChatThemeKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && !field.Name.EndsWith("PalettePrefix", StringComparison.Ordinal))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(keys);
        Assert.All(keys, key => Assert.True(theme.TryGetValue(key, out _), $"Missing resource '{key}'."));
        Assert.All(AIChatTheme.ColorNames, name =>
        {
            Assert.IsType<Color>(theme[AIChatThemeKeys.LightPalettePrefix + name]);
            Assert.IsType<Color>(theme[AIChatThemeKeys.DarkPalettePrefix + name]);
        });
    }

    [Fact]
    public void Theme_KeysAreNamespaced()
    {
        var theme = new AIChatTheme();

        // Besides those, the theme holds the implicit styles of the view's internal element types.
        Assert.All(theme.Keys, key => Assert.True(
            key.StartsWith("AIChat.", StringComparison.Ordinal) ||
            (theme[key] is Style { TargetType: { IsVisible: false } target } && key == target.FullName),
            key));
    }

    [Fact]
    public void Theme_WithoutAnApplication_TravelsWithTheView()
    {
        var view = new AIChatView();

        Assert.Single(view.Resources.MergedDictionaries.OfType<AIChatTheme>());
        Assert.NotNull(view.ControlTemplate);
    }

    [Fact]
    public void Theme_CreatedBeforeTheApplication_MovesToTheApplicationWhenLoaded()
    {
        var view = new AIChatView();
        var app = CreateApplication();
        app.Resources[AIChatThemeKeys.AccentColor] = Colors.Red;

        Attach(app, Load(view));

        Assert.Empty(view.Resources.MergedDictionaries.OfType<AIChatTheme>());
        Assert.IsType<AIChatTheme>(app.Resources.MergedDictionaries.First());
        Assert.Equal(Colors.Red, view.ByAutomationId<Button>("AIChatSendButton").BackgroundColor);
    }

    [Fact]
    public void Theme_IsMergedOnceAtTheFrontOfTheApplicationResources()
    {
        var app = CreateApplication();
        var appStyles = new ResourceDictionary { ["AppKey"] = "value" };
        app.Resources.MergedDictionaries.Add(appStyles);

        var first = ChatHost.Create();
        var second = ChatHost.Create();

        var merged = app.Resources.MergedDictionaries.ToList();
        Assert.IsType<AIChatTheme>(merged[0]);
        Assert.Same(appStyles, merged[1]);
        Assert.Single(merged.OfType<AIChatTheme>());
        Assert.Empty(first.Resources.MergedDictionaries.OfType<AIChatTheme>());
        Assert.Empty(second.Resources.MergedDictionaries.OfType<AIChatTheme>());
    }

    [Fact]
    public void Theme_AlreadyMergedByTheApp_IsReused()
    {
        var app = CreateApplication();
        var mine = new AIChatTheme();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { MergedDictionaries = { mine } });

        ChatHost.Create();

        Assert.DoesNotContain(app.Resources.MergedDictionaries, dictionary => dictionary is AIChatTheme && !ReferenceEquals(dictionary, mine));
    }

    [Fact]
    public void AppResources_OverrideTheDefaults()
    {
        var app = CreateApplication();
        app.Resources[AIChatThemeKeys.AccentColor] = Colors.Red;
        var view = Attach(app, ChatHost.Create());

        Assert.Equal(Colors.Red, view.ByAutomationId<Button>("AIChatSendButton").BackgroundColor);
    }

    [Fact]
    public void Restyling_AtRuntime_UpdatesLiveViews()
    {
        var app = CreateApplication();
        var view = Attach(app, ChatHost.Create());
        var title = view.Descendants<Label>().Single(label => label.Text == view.WelcomeTitle);
        var send = view.ByAutomationId<Button>("AIChatSendButton");

        app.Resources[AIChatThemeKeys.TextColor] = Colors.Purple;
        app.Resources[AIChatThemeKeys.SendButtonStyle] = new Style(typeof(Button))
        {
            Setters = { new Setter { Property = Button.CornerRadiusProperty, Value = 3 } },
        };

        Assert.Equal(Colors.Purple, title.TextColor);
        Assert.Equal(3, send.CornerRadius);
    }

    [Fact]
    public void DarkMode_SwapsThePaletteForEveryVisual()
    {
        var app = CreateApplication();
        var view = Attach(app, ChatHost.Create());
        var theme = Assert.IsType<AIChatTheme>(app.Resources.MergedDictionaries.First());
        var title = view.Descendants<Label>().Single(label => label.Text == view.WelcomeTitle);
        var lightText = (Color)theme[AIChatThemeKeys.LightPalettePrefix + "Text"];
        var darkText = (Color)theme[AIChatThemeKeys.DarkPalettePrefix + "Text"];
        Assert.Equal(lightText, title.TextColor);

        app.UserAppTheme = AppTheme.Dark;

        Assert.All(AIChatTheme.ColorNames, name =>
            Assert.Equal(theme[AIChatThemeKeys.DarkPalettePrefix + name], theme["AIChat.Color." + name]));
        Assert.Equal(darkText, title.TextColor);

        app.UserAppTheme = AppTheme.Light;
        Assert.Equal(lightText, title.TextColor);
    }

    [Fact]
    public void DarkMode_UsesTheAppsPaletteOverrides()
    {
        var app = CreateApplication();
        app.Resources[AIChatThemeKeys.DarkPalettePrefix + "Accent"] = Colors.Orange;
        var view = Attach(app, ChatHost.Create());
        var send = view.ByAutomationId<Button>("AIChatSendButton");

        app.UserAppTheme = AppTheme.Dark;

        Assert.Equal(Colors.Orange, send.BackgroundColor);
    }

    [Fact]
    public void DarkMode_LeavesAnAppOverrideOfTheActiveColorAlone()
    {
        var app = CreateApplication();
        app.Resources[AIChatThemeKeys.AccentColor] = Colors.Green;
        var view = Attach(app, ChatHost.Create());
        var send = view.ByAutomationId<Button>("AIChatSendButton");

        app.UserAppTheme = AppTheme.Dark;

        Assert.Equal(Colors.Green, send.BackgroundColor);
    }

    [Fact]
    public void BuiltInViews_FollowTheTheme()
    {
        var app = CreateApplication();
        var view = Attach(app, ChatHost.Create());
        var card = new ToolCallView { Block = Blocks.ToolCall("search") };
        view.InputLeadingContent = card;

        var theme = Assert.IsType<AIChatTheme>(app.Resources.MergedDictionaries.First());
        Assert.Equal(theme[AIChatThemeKeys.SurfaceColor], card.Descendants<Border>().First().BackgroundColor);

        app.UserAppTheme = AppTheme.Dark;
        Assert.Equal(theme[AIChatThemeKeys.DarkPalettePrefix + "Surface"], card.Descendants<Border>().First().BackgroundColor);
    }

    [Fact]
    public void ControlTemplate_CanBeReplacedThroughResources()
    {
        var app = CreateApplication();
        app.Resources[AIChatThemeKeys.ChatViewTemplate] = new ControlTemplate(() => new Label { Text = "Custom shell" });

        var view = Attach(app, ChatHost.Create());

        Assert.Contains(view.Descendants<Label>(), label => label.Text == "Custom shell");
        Assert.Empty(view.Descendants<Editor>());
    }

    [Fact]
    public void CustomControlTemplate_WithoutParts_StillSends() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("Hi");
        var view = ChatHost.Create(ChatHost.Session(client), v => v.ControlTemplate = new ControlTemplate(() =>
        {
            var send = new Button { AutomationId = "MySend" };
            send.SetBinding(Button.CommandProperty, new Binding(nameof(AIChatView.SendCommand), source: RelativeBindingSource.TemplatedParent));
            return send;
        }));

        view.Text = "Hello";
        view.ByAutomationId<Button>("MySend").Command.Execute(null);
        await UIThread.Until(() => client.Requests.Count == 1 && !view.IsBusy);

        Assert.Equal("Hello", client.LastUserText);
        Assert.False(view.FocusInput());
    });

    [Fact]
    public void Composer_ButtonsAndMessageBoxShareTheBottom()
    {
        // One line of text is no taller than the buttons and sits on their centre; as it grows, its last line stays level
        // with them. The platform margin does the centring (measured on the devices, see GetInputMargin).
        var view = Load(new AIChatView { AllowAttachments = true });
        var input = view.ByAutomationId<Editor>("AIChatInput");
        var buttons = new[] { "AIChatAttachButton", "AIChatSendButton" }.Select(id => view.ByAutomationId<Button>(id)).ToList();

        Assert.Equal(LayoutOptions.End, input.VerticalOptions);
        Assert.True(input.MinimumHeightRequest <= 36, $"A one-line message box is {input.MinimumHeightRequest} high.");
        Assert.Equal(AIChatTheme.GetInputMargin(AIChatTheme.GetPlatform()), input.Margin);
        Assert.All(buttons, button =>
        {
            Assert.Equal(36, button.HeightRequest);
            var stack = Assert.IsAssignableFrom<HorizontalStackLayout>(button.Parent);
            Assert.Equal(LayoutOptions.End, stack.VerticalOptions);
            Assert.Equal(default, stack.Margin);
        });
    }

    [Fact]
    public void InputMargin_CentresALineOnTheButtonsOnEachPlatform()
    {
        Assert.Equal(new Thickness(0, 0, 0, 1), AIChatTheme.GetInputMargin(DevicePlatform.iOS));
        Assert.Equal(new Thickness(0, 0, 0, 1), AIChatTheme.GetInputMargin(DevicePlatform.MacCatalyst));
        Assert.Equal(new Thickness(0, -3.5, 0, -1.8), AIChatTheme.GetInputMargin(DevicePlatform.Android));
        Assert.Equal(default, AIChatTheme.GetInputMargin(DevicePlatform.WinUI));
    }

    private static Application CreateApplication()
    {
        var app = new Application();
        Application.Current = app;
        return app;
    }

    private static AIChatView Load(AIChatView view)
    {
        ChatHost.Load(view);
        return view;
    }

    private static AIChatView Attach(Application app, AIChatView view)
    {
        view.Window!.Parent = app;
        return view;
    }
}
