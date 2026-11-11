using Plugin.Maui.Audio;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>The default <see cref="IChatAudioRecorder"/>: mono WAV through Plugin.Maui.Audio.</summary>
internal sealed class MauiChatAudioRecorder : IChatAudioRecorder
{
    private static readonly TimeSpan MinimumRecordingDuration = TimeSpan.FromMilliseconds(600);

    private IAudioRecorder? _recorder;
    private DateTimeOffset _startedAt;
    private bool? _isSupported;

    public bool IsSupported
    {
        get
        {
            try
            {
                return _isSupported ??= AudioManager.Current.CreateRecorder().CanRecordAudio;
            }
            catch
            {
                // The plugin throws different exception types per platform (and outside an app).
                return _isSupported ??= false;
            }
        }
    }

    public bool IsRecording => _recorder?.IsRecording == true;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var permission = await Permissions.RequestAsync<Permissions.Microphone>();
        cancellationToken.ThrowIfCancellationRequested();
        if (permission != PermissionStatus.Granted)
            throw new PermissionException("Microphone permission was denied.");

        var recorder = AudioManager.Current.CreateRecorder(new AudioRecorderOptions
        {
            Channels = ChannelType.Mono,
            Encoding = Plugin.Maui.Audio.Encoding.Wav,
        });
        if (!recorder.CanRecordAudio)
            throw new FeatureNotSupportedException("Audio recording is not supported.");

        _recorder = recorder;
        try
        {
            await recorder.StartAsync();
            cancellationToken.ThrowIfCancellationRequested();
            _startedAt = DateTimeOffset.UtcNow;
        }
        catch
        {
            await DiscardAsync(recorder);
            if (ReferenceEquals(_recorder, recorder))
                _recorder = null;
            throw;
        }
    }

    public async Task<DataContent?> StopAsync(long maximumBytes, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        var recorder = _recorder;
        if (recorder is null)
            return null;

        try
        {
            // Very short recordings produce empty or truncated files on some platforms.
            var remaining = MinimumRecordingDuration - (DateTimeOffset.UtcNow - _startedAt);
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining, CancellationToken.None);

            if (!ReferenceEquals(_recorder, recorder))
                return null;

            var source = await recorder.StopAsync();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var stream = source.GetAudioStream();
                if (ReferenceEquals(stream, Stream.Null))
                    return null;

                var bytes = await StreamLimits.ReadAsync(stream, maximumBytes, cancellationToken)
                    ?? throw new AudioRecordingTooLargeException();
                if (bytes.Length == 0)
                    return null;

                return new DataContent(bytes, "audio/wav")
                {
                    Name = $"recording-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.wav",
                };
            }
            finally
            {
                DeleteTemporaryFile(source);
            }
        }
        finally
        {
            if (ReferenceEquals(_recorder, recorder))
            {
                await DiscardAsync(recorder);
                _recorder = null;
            }
        }
    }

    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        var recorder = _recorder;
        if (recorder is null)
            return;

        try
        {
            await DiscardAsync(recorder);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            if (ReferenceEquals(_recorder, recorder))
                _recorder = null;
        }
    }

    private static async Task DiscardAsync(IAudioRecorder recorder)
    {
        if (recorder.IsRecording)
            DeleteTemporaryFile(await recorder.StopAsync());
    }

    private static void DeleteTemporaryFile(IAudioSource source)
    {
        if (source is FileAudioSource fileSource && fileSource.GetFilePath() is { } path && File.Exists(path))
            File.Delete(path);
    }
}

/// <summary>Thrown by the default recorder when a recording exceeds the size limit.</summary>
internal sealed class AudioRecordingTooLargeException() : Exception("The recording is larger than the attachment limit.")
{
}
