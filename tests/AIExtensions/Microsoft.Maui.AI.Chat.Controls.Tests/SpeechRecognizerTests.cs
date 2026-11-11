using System.Globalization;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

/// <summary>
/// The built-in recognizer over a toolkit service that raises updates the way CommunityToolkit.Maui.Core 14.0.1
/// does on each platform.
/// </summary>
public class SpeechRecognizerTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly TimeSpan Pause = MauiChatSpeechRecognizer.DefaultEndOfSpeechTimeout;

    [Fact]
    public void Apple_WordsAddUpAndAPauseEndsThePass() => UIThread.Run(async () =>
    {
        var (toolkit, time, recognizer, results) = Create(SpeechUpdateKind.Words);

        await recognizer.StartAsync(English, reportPartialResults: true);
        toolkit.SayOnApple("what's the weather in Paris");

        Assert.Equal(
            ["what's", "what's the", "what's the weather", "what's the weather in", "what's the weather in Paris"],
            results.Select(result => result.Text));
        Assert.DoesNotContain(results, result => result.IsFinal);
        Assert.True(recognizer.IsListening);

        time.Advance(Pause);
        await UIThread.Drain();

        var final = results[^1];
        Assert.True(final.IsFinal);
        Assert.Equal("what's the weather in Paris", final.Text);
        Assert.Equal(ChatSpeechRecognitionErrorKind.None, final.ErrorKind);
        Assert.False(recognizer.IsListening);
        Assert.False(toolkit.IsListening);
        Assert.Equal(0, toolkit.HandlerCount);
    });

    [Fact]
    public void Apple_EachWordRestartsThePause() => UIThread.Run(async () =>
    {
        var (toolkit, time, recognizer, results) = Create(SpeechUpdateKind.Words);

        await recognizer.StartAsync(English, reportPartialResults: true);
        time.Advance(Pause * 3);
        toolkit.SayOnApple("what's the");
        time.Advance(Pause * 0.75);
        toolkit.SayOnApple("weather");
        time.Advance(Pause * 0.75);
        await UIThread.Drain();
        Assert.DoesNotContain(results, result => result.IsFinal);

        time.Advance(Pause * 0.25);
        await UIThread.Drain();
        Assert.Equal("what's the weather", Assert.Single(results, result => result.IsFinal).Text);
    });

    [Fact]
    public void Windows_PhrasesAddUpIncludingOnePassedOnWhileStopping() => UIThread.Run(async () =>
    {
        var (toolkit, time, recognizer, results) = Create(SpeechUpdateKind.Phrases);

        await recognizer.StartAsync(English, reportPartialResults: true);
        toolkit.SayOnWindows("What's the weather");
        toolkit.SayOnWindows("in Paris?");
        Assert.Equal(["What's the weather", "What's the weather in Paris?"], results.Select(result => result.Text));

        // Stopping a Windows session passes on the phrase it was still recognizing.
        toolkit.Stopping = () => toolkit.SayOnWindows("Thanks.");
        time.Advance(Pause);
        await UIThread.Drain();

        var final = results[^1];
        Assert.True(final.IsFinal);
        Assert.Equal("What's the weather in Paris? Thanks.", final.Text);
        Assert.Equal(1, toolkit.StopCount);
    });

    [Fact]
    public void Windows_APauseEndsThePassOnlyWhileThePlatformHearsSilence() => UIThread.Run(async () =>
    {
        var (toolkit, time, recognizer, results) = Create(SpeechUpdateKind.Phrases);

        await recognizer.StartAsync(English, reportPartialResults: true);
        toolkit.SayOnWindows("What's the weather");

        // A long phrase is reported only once it ends, so no update arrives while it is spoken.
        toolkit.HearSound();
        time.Advance(Pause * 3);
        await UIThread.Drain();
        Assert.DoesNotContain(results, result => result.IsFinal);
        Assert.True(recognizer.IsListening);

        toolkit.SayOnWindows("going to be like in Paris tomorrow afternoon?");
        time.Advance(Pause);
        await UIThread.Drain();

        Assert.Equal(
            "What's the weather going to be like in Paris tomorrow afternoon?",
            Assert.Single(results, result => result.IsFinal).Text);
        Assert.False(toolkit.IsListening);
    });

    [Fact]
    public void Android_TextIsNotRepeatedAndThePlatformEndsThePass() => UIThread.Run(async () =>
    {
        var (toolkit, time, recognizer, results) = Create(SpeechUpdateKind.Cumulative);

        await recognizer.StartAsync(English, reportPartialResults: true);
        toolkit.SayOnAndroid("what's the weather in Paris");

        Assert.Equal(
            ["what's", "what's the", "what's the weather", "what's the weather in", "what's the weather in Paris"],
            results.Select(result => result.Text));

        // Android ends the pass itself when the speaker pauses.
        time.Advance(Pause * 5);
        await UIThread.Drain();
        Assert.DoesNotContain(results, result => result.IsFinal);

        toolkit.Complete("What's the weather in Paris?");
        await UIThread.Drain();

        var final = results[^1];
        Assert.True(final.IsFinal);
        Assert.Equal("What's the weather in Paris?", final.Text);
        Assert.False(recognizer.IsListening);
        Assert.Equal(0, toolkit.HandlerCount);
    });

    [Fact]
    public void InterimResults_CanBeTurnedOff() => UIThread.Run(async () =>
    {
        var (toolkit, time, recognizer, results) = Create(SpeechUpdateKind.Words);

        await recognizer.StartAsync(English, reportPartialResults: false);
        toolkit.SayOnApple("good morning");
        time.Advance(Pause);
        await UIThread.Drain();

        Assert.True(toolkit.LastOptions!.ShouldReportPartialResults);
        var final = Assert.Single(results);
        Assert.True(final.IsFinal);
        Assert.Equal("good morning", final.Text);
    });

    [Fact]
    public void Stopping_ReleasesTheMicrophoneWithoutAFinalResult() => UIThread.Run(async () =>
    {
        var (toolkit, time, recognizer, results) = Create(SpeechUpdateKind.Words);

        await recognizer.StartAsync(English, reportPartialResults: true);
        toolkit.SayOnApple("what's the");
        await recognizer.StopAsync();
        time.Advance(Pause);
        await UIThread.Drain();

        Assert.DoesNotContain(results, result => result.IsFinal);
        Assert.Equal(1, toolkit.StopCount);
        Assert.False(toolkit.IsListening);
        Assert.False(recognizer.IsListening);
        Assert.Equal(0, toolkit.HandlerCount);
    });

    [Fact]
    public void PlatformFailure_EndsThePassWithItsKind() => UIThread.Run(async () =>
    {
        var (toolkit, _, recognizer, results) = Create(SpeechUpdateKind.Words);

        await recognizer.StartAsync(English, reportPartialResults: true);
        toolkit.Complete(text: null, new HttpRequestException("offline"));
        await UIThread.Drain();

        var final = Assert.Single(results);
        Assert.True(final.IsFinal);
        Assert.Equal(ChatSpeechRecognitionErrorKind.Transient, final.ErrorKind);
        Assert.False(recognizer.IsListening);
    });

    [Fact]
    public void FailedStart_ReleasesTheMicrophone() => UIThread.Run(async () =>
    {
        var (toolkit, _, recognizer, _) = Create(SpeechUpdateKind.Words);
        toolkit.StartError = new InvalidOperationException("audio engine failed");

        await Assert.ThrowsAsync<InvalidOperationException>(() => recognizer.StartAsync(English, reportPartialResults: true));

        Assert.False(toolkit.IsListening);
        Assert.False(recognizer.IsListening);
        Assert.Equal(0, toolkit.HandlerCount);
    });

    [Fact]
    public void StopDuringAStart_ReleasesWhatTheStartOpened() => UIThread.Run(async () =>
    {
        var (toolkit, _, recognizer, _) = Create(SpeechUpdateKind.Words);
        toolkit.StartGate = Waiters.Signal();

        var start = recognizer.StartAsync(English, reportPartialResults: true);
        var stop = recognizer.StopAsync();
        Assert.False(recognizer.IsListening);

        toolkit.StartGate.SetResult();
        await start;
        await stop;

        Assert.False(toolkit.IsListening);
        Assert.False(recognizer.IsListening);
        Assert.Equal(0, toolkit.HandlerCount);
    });

    [Fact]
    public void NextPass_WaitsUntilTheMicrophoneIsReleased() => UIThread.Run(async () =>
    {
        var (toolkit, time, recognizer, results) = Create(SpeechUpdateKind.Words);
        toolkit.StopGate = Waiters.Signal();

        await recognizer.StartAsync(English, reportPartialResults: true);
        toolkit.SayOnApple("hello");
        time.Advance(Pause);
        await UIThread.Drain();
        Assert.False(recognizer.IsListening);

        var next = recognizer.StartAsync(English, reportPartialResults: true);
        await UIThread.Drain();
        Assert.Equal(1, toolkit.StartCount);

        toolkit.StopGate.SetResult();
        await next;

        Assert.Equal(2, toolkit.StartCount);
        Assert.Equal("hello", Assert.Single(results, result => result.IsFinal).Text);
        Assert.True(recognizer.IsListening);
        await recognizer.StopAsync();
    });

    private static (FakeSpeechToText Toolkit, ManualTimeProvider Time, MauiChatSpeechRecognizer Recognizer, List<ChatSpeechRecognitionEventArgs> Results) Create(
        SpeechUpdateKind kind)
    {
        var toolkit = new FakeSpeechToText();
        var time = new ManualTimeProvider();
        var recognizer = new MauiChatSpeechRecognizer(toolkit, kind, time);
        var results = new List<ChatSpeechRecognitionEventArgs>();
        recognizer.RecognitionChanged += (_, e) => results.Add(e);
        return (toolkit, time, recognizer, results);
    }
}
