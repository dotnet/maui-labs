// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Maui.Chat.Controls;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Chat.Controls.Blazor.Tests;

/// <summary>
/// Verifies that <see cref="AppBuilderExtensions.UseChatControls"/> registers the neutral
/// multimodal service defaults so DI-driven Blazor composers get them out of the box, and
/// that consumer-supplied registrations still win.
/// </summary>
public class ChatControlsServiceRegistrationTests
{
    [Fact]
    public void UseChatControls_RegistersDefaultAttachmentPicker()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseChatControls();

        var services = builder.Services.BuildServiceProvider();
        var picker = services.GetService<IChatAttachmentPicker>();

        Assert.NotNull(picker);
    }

    [Fact]
    public void UseChatControls_RegistersDefaultAudioRecorder()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseChatControls();

        var services = builder.Services.BuildServiceProvider();
        var recorder = services.GetService<IChatAudioRecorder>();

        Assert.NotNull(recorder);
    }

    [Fact]
    public void UseChatControls_RegistersDefaultSpeechRecognizer()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseChatControls();

        var services = builder.Services.BuildServiceProvider();
        var recognizer = services.GetService<IChatSpeechRecognizer>();

        Assert.NotNull(recognizer);
    }

    [Fact]
    public void UseChatControls_UsesTryAdd_SoAppSuppliedRecorderWins()
    {
        var builder = MauiApp.CreateBuilder();
        builder.Services.AddSingleton<IChatAudioRecorder, StubRecorder>();
        builder.UseChatControls();

        var services = builder.Services.BuildServiceProvider();
        var recorder = services.GetService<IChatAudioRecorder>();

        Assert.IsType<StubRecorder>(recorder);
    }

    private sealed class StubRecorder : IChatAudioRecorder
    {
        public bool IsSupported => true;
        public bool IsRecording => false;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ChatAttachment?> StopAsync(long maximumBytes, CancellationToken cancellationToken = default) =>
            Task.FromResult<ChatAttachment?>(null);
        public Task CancelAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
