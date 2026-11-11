using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

/// <summary>
/// The built-in icons are glyphs of the AIChatIcons font embedded in the library, chosen by <c>AIChat.Icon.*</c> keys,
/// and registered with the app's fonts by the view itself.
/// </summary>
public sealed class IconTests : IDisposable
{
    private const string IconFont = "AIChatIcons";

    public IconTests() => Application.Current = null;

    public void Dispose() => Application.Current = null;

    public static IReadOnlyList<string> IconKeys { get; } =
        typeof(AIChatThemeKeys).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static field => field.IsLiteral)
            .Select(static field => (string)field.GetRawConstantValue()!)
            .Where(static key => key.StartsWith("AIChat.Icon.", StringComparison.Ordinal))
            .ToList();

    /// <summary>Gets the default glyph for an icon key.</summary>
    public static string Glyph(string key) => (string)new AIChatTheme()[key];

    [Fact]
    public void IconFont_IsEmbeddedInTheLibrary()
    {
        var font = ReadEmbeddedFont();

        Assert.Equal(0x00010000u, BinaryPrimitives.ReadUInt32BigEndian(font));
        Assert.Equal(IconFont, ReadFamilyName(font));
        Assert.Equal(IconFont, new AIChatTheme()[AIChatThemeKeys.IconFontFamily]);
    }

    [Fact]
    public void EveryIconKey_IsAGlyphOfTheIconFont()
    {
        var glyphs = ReadCodepoints(ReadEmbeddedFont());
        var theme = new AIChatTheme();

        Assert.Equal(
            ["Attach", "Audio", "Collapse", "Document", "Expand", "Image", "Link", "LiveVoice", "Microphone", "Remove", "Send", "Stop", "Transcribing"],
            IconKeys.Select(static key => key["AIChat.Icon.".Length..]).Order());
        Assert.All(IconKeys, key =>
        {
            var glyph = Assert.IsType<string>(theme[key]);
            Assert.True(glyph.Length == 1 && glyphs.Contains(glyph[0]), $"{key} is not a glyph of {IconFont}.");
        });
        Assert.Equal(IconKeys.Count, IconKeys.Select(static key => Glyph(key)).Distinct().Count());
    }

    [Fact]
    public void IconFont_IsRegisteredOncePerAppWhenTheViewIsConnected()
    {
        var loader = new RecordingFontLoader();
        var registrar = new CountingFontRegistrar(new FontRegistrar(loader));
        var context = new MauiContext(new ServiceCollection().AddSingleton<IFontRegistrar>(registrar).BuildServiceProvider());

        var first = new AIChatView();
        registrar.OnRegister = () => registrar.HandlerAtRegistration = first.Handler;
        Connect(first, context);
        first.Handler = null;
        Connect(first, context);
        Connect(new AIChatView(), context);

        var registration = Assert.Single(registrar.Registrations);
        Assert.Equal(("AIChatIcons.ttf", IconFont, typeof(AIChatView).Assembly), registration);

        // Registered while the handler was changing: before it mapped the view's parts, whose fonts it looks up.
        Assert.Null(registrar.HandlerAtRegistration);

        // MAUI finds the embedded font by that file name.
        Assert.Equal(IconFont, registrar.GetFont(IconFont));
        Assert.Equal("AIChatIcons.ttf", loader.Loaded?.FontName);
        Assert.Equal(ReadEmbeddedFont(), loader.LoadedBytes);

        // Another app has its own fonts.
        var other = new CountingFontRegistrar(new FontRegistrar(new RecordingFontLoader()));
        Connect(new AIChatView(), new MauiContext(new ServiceCollection().AddSingleton<IFontRegistrar>(other).BuildServiceProvider()));
        Assert.Single(other.Registrations);
        Assert.Single(registrar.Registrations);
    }

    [Fact]
    public void ComposerButtons_ShowIconsFromTheIconFont() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient();
        var stream = client.ReplyLive();
        var view = ChatHost.Create(ChatHost.Session(client), v =>
        {
            v.AllowAttachments = true;
            v.AllowAudioCapture = true;
            v.AudioRecorder = new FakeAudioRecorder();
            v.AllowLiveSpeech = true;
            v.SpeechRecognizer = new FakeSpeechRecognizer();
        });

        AssertIcon(view.ByAutomationId<Button>("AIChatAttachButton"), AIChatThemeKeys.AttachIcon);
        AssertIcon(view.ByAutomationId<Button>("AIChatAudioButton"), AIChatThemeKeys.MicrophoneIcon);
        AssertIcon(view.ByAutomationId<Button>("AIChatLiveSpeechButton"), AIChatThemeKeys.LiveVoiceIcon);
        AssertIcon(view.ByAutomationId<Button>("AIChatSendButton"), AIChatThemeKeys.SendIcon);
        AssertIcon(view.ByAutomationId<Button>("AIChatStopButton"), AIChatThemeKeys.StopIcon);
        Assert.Equal(FontAttributes.None, view.ByAutomationId<Button>("AIChatSendButton").FontAttributes);

        view.Text = "Hello";
        var send = view.SendAsync();
        await UIThread.Until(() => view.CanStop);
        Assert.True(view.ByAutomationId<Button>("AIChatStopButton").IsVisible);
        Assert.False(view.ByAutomationId<Button>("AIChatSendButton").IsVisible);

        stream.Complete();
        await send;
    });

    [Fact]
    public void AppIcon_ReplacesTheBuiltInGlyph()
    {
        var app = new Application();
        Application.Current = app;
        app.Resources[AIChatThemeKeys.SendIcon] = "\u27A4";
        var view = ChatHost.Create();
        view.Window!.Parent = app;
        var send = view.ByAutomationId<Button>("AIChatSendButton");

        Assert.Equal("\u27A4", send.Text);

        app.Resources[AIChatThemeKeys.SendIcon] = "Send";
        Assert.Equal("Send", send.Text);
    }

    [Fact]
    public void Chips_ShowIconsFromTheIconFont()
    {
        AssertChipIcon(ChatVisuals.CreateFileChip(Blocks.Pdf()), AIChatThemeKeys.DocumentIcon);
        AssertChipIcon(ChatVisuals.CreateFileChip(FakeAudioRecorder.Wav()), AIChatThemeKeys.AudioIcon);
        AssertChipIcon(ChatVisuals.CreateFileChip(new DataContent(Array.Empty<byte>(), "image/png") { Name = "empty.png" }), AIChatThemeKeys.ImageIcon);
        AssertChipIcon(ChatVisuals.CreateLinkChip(new UriContent("https://example.com/guide.pdf", "application/pdf")), AIChatThemeKeys.LinkIcon);
        AssertChipIcon(ChatVisuals.CreateImageChip("attachment://seat-map.png", "Seat map"), AIChatThemeKeys.ImageIcon);
        AssertChipIcon(ChatVisuals.CreateImageChip("https://example.com/cat.png", "cat"), AIChatThemeKeys.ImageIcon);

        static void AssertChipIcon(View chip, string key)
        {
            Host(chip);
            var icon = Assert.Single(chip.Descendants<Label>(), static label => label.FontFamily == IconFont);
            Assert.Equal(Glyph(key), icon.Text);
            Assert.True(AutomationProperties.GetExcludedWithChildren(icon), "The chip's description names it; the glyph is decoration.");
        }
    }

    [Fact]
    public void StagedAttachments_ShowAThumbnailForImagesAndAnIconForOtherFiles()
    {
        var view = ChatHost.Create(v => v.AllowAttachments = true);
        view.AddAttachment(Blocks.Png("photo.png"));
        view.AddAttachment(Blocks.Pdf("report.pdf"));
        view.AddAttachment(FakeAudioRecorder.Wav());

        var chips = view.Descendants<Button>()
            .Where(static button => button.AutomationId == "AIChatRemoveAttachment")
            .Select(static button => Chip(button))
            .ToList();
        Assert.Equal(3, chips.Count);

        var thumbnail = Assert.Single(chips[0].Descendants<Image>());
        Assert.IsType<StreamImageSource>(thumbnail.Source);
        Assert.Equal(Aspect.AspectFill, thumbnail.Aspect);
        Assert.DoesNotContain(chips[0].Descendants<Label>(), static label => label.FontFamily == IconFont);

        Assert.Equal(Glyph(AIChatThemeKeys.DocumentIcon), Assert.Single(chips[1].Descendants<Label>(), static label => label.FontFamily == IconFont).Text);
        Assert.Equal(Glyph(AIChatThemeKeys.AudioIcon), Assert.Single(chips[2].Descendants<Label>(), static label => label.FontFamily == IconFont).Text);
        Assert.Empty(chips[1].Descendants<Image>());

        Assert.All(chips, chip => AssertIcon(chip.Descendants<Button>().Single(), AIChatThemeKeys.RemoveIcon));
    }

    private static Border Chip(Element element)
    {
        while (element is not Border)
            element = element.Parent ?? throw new InvalidOperationException("Not in a chip.");
        return (Border)element;
    }

    private static void AssertIcon(Button button, string key)
    {
        Assert.Equal(IconFont, button.FontFamily);
        Assert.Equal(Glyph(key), button.Text);
    }

    private static void Host(View view)
    {
        var host = new ContentView { Content = view };
        host.Resources.MergedDictionaries.Add(new AIChatTheme());
    }

    private static void Connect(AIChatView view, IMauiContext context)
    {
        var handler = new ContextHandler();
        handler.SetMauiContext(context);
        view.Handler = handler;
    }

    // ── The font file ───────────────────────────────────────────────────────────────────────────────────

    private static byte[] ReadEmbeddedFont()
    {
        var assembly = typeof(AIChatView).Assembly;
        var name = Assert.Single(assembly.GetManifestResourceNames(), static name => name.EndsWith(".AIChatIcons.ttf", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static int FindTable(byte[] font, string tag)
    {
        var count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        for (var i = 0; i < count; i++)
        {
            var record = font.AsSpan(12 + (16 * i));
            if (Encoding.ASCII.GetString(record[..4]) == tag)
                return (int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
        }

        throw new InvalidOperationException($"The font has no '{tag}' table.");
    }

    private static string ReadFamilyName(byte[] font)
    {
        var table = FindTable(font, "name");
        var count = U16(font, table + 2);
        var strings = table + U16(font, table + 4);
        for (var i = 0; i < count; i++)
        {
            var record = table + 6 + (12 * i);
            if (U16(font, record) == 3 && U16(font, record + 6) == 1)
                return Encoding.BigEndianUnicode.GetString(font, strings + U16(font, record + 10), U16(font, record + 8));
        }

        throw new InvalidOperationException("The font has no Windows family name.");
    }

    /// <summary>Reads the characters the font has glyphs for, from its Unicode (format 4) character map.</summary>
    private static HashSet<char> ReadCodepoints(byte[] font)
    {
        var cmap = FindTable(font, "cmap");
        var characters = new HashSet<char>();
        for (var i = 0; i < U16(font, cmap + 2); i++)
        {
            var subtable = cmap + (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(cmap + 4 + (8 * i) + 4));
            if (U16(font, subtable) != 4)
                continue;

            var segments = U16(font, subtable + 6) / 2;
            var ends = subtable + 14;
            var starts = ends + (2 * segments) + 2;
            var deltas = starts + (2 * segments);
            var offsets = deltas + (2 * segments);
            for (var segment = 0; segment < segments; segment++)
            {
                var start = U16(font, starts + (2 * segment));
                var end = U16(font, ends + (2 * segment));
                var delta = U16(font, deltas + (2 * segment));
                var offset = U16(font, offsets + (2 * segment));
                for (var c = start; c <= end && c != 0xFFFF; c++)
                {
                    var glyph = offset == 0
                        ? (c + delta) & 0xFFFF
                        : U16(font, offsets + (2 * segment) + offset + (2 * (c - start)));
                    if (glyph != 0)
                        characters.Add((char)c);
                }
            }
        }

        return characters;
    }

    private static int U16(byte[] font, int offset) => BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(offset));

    // ── Fakes ───────────────────────────────────────────────────────────────────────────────────────────

    private sealed class ContextHandler() : Microsoft.Maui.Handlers.ViewHandler<IView, object>(new PropertyMapper<IView, ContextHandler>())
    {
        protected override object CreatePlatformView() => new();
    }

    private sealed class CountingFontRegistrar(IFontRegistrar inner) : IFontRegistrar
    {
        public List<(string FileName, string? Alias, Assembly Assembly)> Registrations { get; } = [];

        public Action? OnRegister { get; set; }

        public IElementHandler? HandlerAtRegistration { get; set; }

        public void Register(string filename, string? alias, Assembly assembly)
        {
            Registrations.Add((filename, alias, assembly));
            OnRegister?.Invoke();
            inner.Register(filename, alias, assembly);
        }

        public void Register(string filename, string? alias) => inner.Register(filename, alias);

        public string? GetFont(string font) => inner.GetFont(font);
    }

    private sealed class RecordingFontLoader : IEmbeddedFontLoader
    {
        public EmbeddedFont? Loaded { get; private set; }

        public byte[]? LoadedBytes { get; private set; }

        public string? LoadFont(EmbeddedFont font)
        {
            Loaded = font;
            using var copy = new MemoryStream();
            font.ResourceStream!.CopyTo(copy);
            LoadedBytes = copy.ToArray();
            return IconFont;
        }
    }
}
