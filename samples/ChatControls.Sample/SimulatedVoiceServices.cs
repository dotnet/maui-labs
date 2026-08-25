using System.Globalization;
using Microsoft.Maui.Chat.Controls;

namespace ChatControls.Sample;

internal sealed class SimulatedChatAudioRecorder : IChatAudioRecorder
{
    public bool IsSupported => true;

    public bool IsRecording { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsRecording = true;
        return Task.CompletedTask;
    }

    public Task<ChatAttachment?> StopAsync(
        long maximumBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        cancellationToken.ThrowIfCancellationRequested();
        IsRecording = false;
        return Task.FromResult<ChatAttachment?>(
            new ChatAttachment(
                "simulated-recording.wav",
                "audio/wav",
                CreateSilentWav()));
    }

    public Task CancelAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsRecording = false;
        return Task.CompletedTask;
    }

    private static byte[] CreateSilentWav()
    {
        const int sampleRate = 8000;
        const short channels = 1;
        const short bitsPerSample = 16;
        const int sampleCount = sampleRate;
        var dataLength = sampleCount * channels * bitsPerSample / 8;

        using var stream = new MemoryStream(44 + dataLength);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataLength);
        writer.Write(new byte[dataLength]);
        return stream.ToArray();
    }
}

internal sealed class SimulatedChatSpeechRecognizer : IChatSpeechRecognizer
{
    private CancellationTokenSource? _recognitionCts;
    private bool _hasEmitted;

    public event EventHandler<ChatSpeechRecognitionEventArgs>? RecognitionChanged;

    public bool IsSupported => true;

    public bool IsListening { get; private set; }

    public Task<bool> RequestPermissionsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }

    public Task StartAsync(
        CultureInfo culture,
        bool reportPartialResults,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _recognitionCts?.Cancel();
        _recognitionCts?.Dispose();
        var operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _recognitionCts = operationCts;
        IsListening = true;
        if (!_hasEmitted)
            _ = EmitRecognitionAsync(operationCts, reportPartialResults);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _recognitionCts?.Cancel();
        _recognitionCts?.Dispose();
        _recognitionCts = null;
        IsListening = false;
        return Task.CompletedTask;
    }

    public void Reset() => _hasEmitted = false;

    private async Task EmitRecognitionAsync(
        CancellationTokenSource operationCts,
        bool reportPartialResults)
    {
        try
        {
            if (reportPartialResults)
            {
                await Task.Delay(350, operationCts.Token);
                RecognitionChanged?.Invoke(
                    this,
                    new ChatSpeechRecognitionEventArgs(
                        "This is a simulated",
                        isFinal: false));
            }

            await Task.Delay(450, operationCts.Token);
            if (!ReferenceEquals(_recognitionCts, operationCts))
                return;

            _hasEmitted = true;
            IsListening = false;
            RecognitionChanged?.Invoke(
                this,
                new ChatSpeechRecognitionEventArgs(
                    "This is a simulated voice message.",
                    isFinal: true));
        }
        catch (OperationCanceledException)
        {
        }
    }
}
