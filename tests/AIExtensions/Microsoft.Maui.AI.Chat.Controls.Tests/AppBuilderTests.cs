using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public class AppBuilderTests
{
    [Fact]
    public void UseAIChat_RegistersTheDefaultServicesOnce()
    {
        var builder = MauiApp.CreateBuilder(useDefaults: false);

        builder.UseAIChat().UseAIChat();

        Assert.Single(builder.Services, d => d.ServiceType == typeof(IChatAttachmentPicker));
        Assert.Single(builder.Services, d => d.ServiceType == typeof(IChatAudioRecorder));
        Assert.Single(builder.Services, d => d.ServiceType == typeof(IChatSpeechRecognizer));
        Assert.DoesNotContain(builder.Services, d => d.ServiceType == typeof(IChatAudioTranscriber));

        using var services = builder.Services.BuildServiceProvider();
        Assert.IsType<FileChatAttachmentPicker>(services.GetRequiredService<IChatAttachmentPicker>());
        Assert.IsType<MauiChatAudioRecorder>(services.GetRequiredService<IChatAudioRecorder>());
        Assert.IsType<MauiChatSpeechRecognizer>(services.GetRequiredService<IChatSpeechRecognizer>());
    }

    [Fact]
    public void UseAIChat_KeepsServicesTheAppRegistered()
    {
        var before = new FakeAttachmentPicker();
        var after = new FakeSpeechRecognizer();
        var builder = MauiApp.CreateBuilder(useDefaults: false);
        builder.Services.AddSingleton<IChatAttachmentPicker>(before);

        builder.UseAIChat();
        builder.Services.AddSingleton<IChatSpeechRecognizer>(after);

        using var services = builder.Services.BuildServiceProvider();
        Assert.Same(before, services.GetRequiredService<IChatAttachmentPicker>());
        Assert.Same(after, services.GetRequiredService<IChatSpeechRecognizer>());
    }

    [Fact]
    public void UseAIChat_WithNullBuilder_Throws() =>
        Assert.Throws<ArgumentNullException>(() => AppBuilderExtensions.UseAIChat(null!));

    [Fact]
    public void RegisteredServices_AreUsedByTheView()
    {
        var picker = new FakeAttachmentPicker();
        var recorder = new FakeAudioRecorder();
        var recognizer = new FakeSpeechRecognizer();
        var transcriber = new FakeTranscriber("text");
        var services = new ServiceCollection()
            .AddSingleton<IChatAttachmentPicker>(picker)
            .AddSingleton<IChatAudioRecorder>(recorder)
            .AddSingleton<IChatSpeechRecognizer>(recognizer)
            .AddSingleton<IChatAudioTranscriber>(transcriber)
            .BuildServiceProvider();

        var view = new AIChatView { ServiceProviderOverride = services, AllowAudioCapture = true, AllowLiveSpeech = true };

        Assert.Same(picker, view.GetAttachmentPicker());
        Assert.Same(recorder, view.GetAudioRecorder());
        Assert.Same(recognizer, view.GetSpeechRecognizer());
        Assert.Same(transcriber, view.GetAudioTranscriber());
        Assert.True(view.CanCaptureAudio);
        Assert.True(view.CanUseLiveSpeech);
    }

    [Fact]
    public void WithoutServices_TheViewFallsBackToItsDefaults()
    {
        var view = new AIChatView();

        Assert.Same(FileChatAttachmentPicker.Default, view.GetAttachmentPicker());
        Assert.IsType<MauiChatAudioRecorder>(view.GetAudioRecorder());
        Assert.Same(view.GetAudioRecorder(), view.GetAudioRecorder());
        Assert.IsType<MauiChatSpeechRecognizer>(view.GetSpeechRecognizer());
        Assert.Null(view.GetAudioTranscriber());
    }
}
