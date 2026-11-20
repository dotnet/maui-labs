#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CometBaristaNotes.Services;

public static class ProfileFeedbackContract
{
    public const int DurationMilliseconds = 2000;
    public const float Height = 56;
    public const float HorizontalMargin = 16;
    public const float GapAboveActionRow = 16;
    public const float BottomClearance = 88;
}

public sealed class ProfileFeedbackController
{
    readonly Func<int, Task> _delay;
    int _version;

    public ProfileFeedbackController(Func<int, Task>? delay = null) =>
        _delay = delay ?? Task.Delay;

    public async Task ShowAsync(string message, Action<string?> publish)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(publish);

        var version = Interlocked.Increment(ref _version);
        publish(message);
        await _delay(ProfileFeedbackContract.DurationMilliseconds);
        if (version == Volatile.Read(ref _version))
            publish(null);
    }

    public void Cancel() => Interlocked.Increment(ref _version);
}
