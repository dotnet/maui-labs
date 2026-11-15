using AppKit;
using Microsoft.Maui.Media;

#pragma warning disable CA1422 // NSSpeechSynthesizer remains the AppKit backend until AVSpeechSynthesizer parity is implemented.

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

class TextToSpeechImplementation : ITextToSpeech
{
    readonly Lazy<NSSpeechSynthesizer> _synthesizer = new(() =>
        new NSSpeechSynthesizer { Delegate = new SpeechDelegate() });

    public Task<IEnumerable<Locale>> GetLocalesAsync() =>
        Task.FromResult(NSSpeechSynthesizer.AvailableVoices
            .Select(voice => NSSpeechSynthesizer.AttributesForVoice(voice))
            .Select(attr => new Locale(
                attr["VoiceLanguage"]?.ToString() ?? "",
                null,
                attr["VoiceName"]?.ToString() ?? "",
                attr["VoiceIdentifier"]?.ToString() ?? "")));

    public async Task SpeakAsync(string text, SpeechOptions? options = default, CancellationToken cancelToken = default)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var ss = _synthesizer.Value;
        var del = (SpeechDelegate)ss.Delegate;
        var tcs = new TaskCompletionSource<bool>();

        try
        {
            if (options is not null)
            {
                if (options.Volume.HasValue)
                    ss.Volume = Math.Clamp(options.Volume.Value, 0f, 1f);
                if (options.Locale?.Id is not null)
                    ss.Voice = options.Locale.Id;
                if (options.Rate.HasValue)
                    ss.Rate = options.Rate.Value;
            }

            del.FinishedSpeaking += OnFinished;
            del.EncounteredError += OnError;

            ss.StartSpeakingString(text);

            using (cancelToken.Register(() =>
            {
                ss.StopSpeaking();
                tcs.TrySetResult(true);
            }))
            {
                await tcs.Task;
            }
        }
        finally
        {
            del.FinishedSpeaking -= OnFinished;
            del.EncounteredError -= OnError;
        }

        void OnFinished(bool completed) => tcs.TrySetResult(completed);
        void OnError(string msg) => tcs.TrySetException(new Exception(msg));
    }

    class SpeechDelegate : NSSpeechSynthesizerDelegate
    {
        public event Action<bool>? FinishedSpeaking;
        public event Action<string>? EncounteredError;

        public override void DidFinishSpeaking(NSSpeechSynthesizer sender, bool finishedSpeaking) =>
            FinishedSpeaking?.Invoke(finishedSpeaking);

        public override void DidEncounterError(NSSpeechSynthesizer sender, nuint characterIndex, string theString, string message) =>
            EncounteredError?.Invoke(message);
    }
}
