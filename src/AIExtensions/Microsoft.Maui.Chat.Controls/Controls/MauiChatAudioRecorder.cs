using Plugin.Maui.Audio;

namespace Microsoft.Maui.Chat.Controls;

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
                return _isSupported ??=
                    AudioManager.Current.CreateRecorder().CanRecordAudio;
            }
            catch (FeatureNotSupportedException)
            {
                return false;
            }
        }
    }

    public bool IsRecording => _recorder?.IsRecording == true;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var permission = await Permissions.RequestAsync<Permissions.Microphone>()
            .ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        if (permission != PermissionStatus.Granted)
            throw new MicrophonePermissionDeniedException();

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
            await recorder.StartAsync().ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            _startedAt = DateTimeOffset.UtcNow;
        }
        catch
        {
            if (recorder.IsRecording)
            {
                var discarded = await recorder.StopAsync().ConfigureAwait(true);
                DeleteTemporaryFile(discarded);
            }
            if (ReferenceEquals(_recorder, recorder))
                _recorder = null;
            throw;
        }
    }

    public async Task<ChatAttachment?> StopAsync(
        long maximumBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        var recorder = _recorder;
        if (recorder is null)
            return null;

        try
        {
            var remaining = MinimumRecordingDuration - (DateTimeOffset.UtcNow - _startedAt);
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining, CancellationToken.None).ConfigureAwait(true);

            if (!ReferenceEquals(_recorder, recorder))
                return null;

            var source = await recorder.StopAsync().ConfigureAwait(true);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var stream = source.GetAudioStream();
                if (ReferenceEquals(stream, Stream.Null))
                    return null;

                using var buffer = new MemoryStream();
                await CopyWithLimitAsync(
                    stream,
                    buffer,
                    maximumBytes,
                    cancellationToken).ConfigureAwait(true);
                if (buffer.Length == 0)
                    return null;

                return new ChatAttachment(
                    $"recording-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}.wav",
                    "audio/wav",
                    buffer.ToArray());
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
                if (recorder.IsRecording)
                {
                    var discarded = await recorder.StopAsync().ConfigureAwait(true);
                    DeleteTemporaryFile(discarded);
                }
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
            if (recorder.IsRecording)
            {
                var discarded = await recorder.StopAsync().ConfigureAwait(true);
                DeleteTemporaryFile(discarded);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            if (ReferenceEquals(_recorder, recorder))
                _recorder = null;
        }
    }

    private static void DeleteTemporaryFile(IAudioSource source)
    {
        if (source is FileAudioSource fileSource)
        {
            var path = fileSource.GetFilePath();
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static async Task CopyWithLimitAsync(
        Stream source,
        Stream destination,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var copyBuffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await source
                .ReadAsync(copyBuffer, cancellationToken)
                .ConfigureAwait(true);
            if (read == 0)
                return;

            total += read;
            if (total > maximumBytes)
                throw new AudioRecordingTooLargeException();

            await destination
                .WriteAsync(copyBuffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(true);
        }
    }
}

internal sealed class AudioRecordingTooLargeException : Exception
{
}

internal sealed class MicrophonePermissionDeniedException : Exception
{
}
