#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CometBaristaNotes.Services.Voice;

public static class BaristaVoiceIntegration
{
    static readonly object Sync = new();
    static readonly SemaphoreSlim LifecycleGate = new(1, 1);
    static IPlatformSpeechRecognizer? _platform;
    static IBaristaVoiceCallbacks? _callbacks;
    static bool _activateOnNextNewDrinkMount;

    public static PushToTalkVoiceService? Session { get; private set; }

    public static VoiceOverlayState State =>
        Session?.CurrentState ?? PushToTalkVoiceService.UnconfiguredState;

    public static bool HasPendingNewDrinkActivation
    {
        get
        {
            lock (Sync)
                return _activateOnNextNewDrinkMount;
        }
    }

    public static void ConfigurePlatform(IPlatformSpeechRecognizer platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        if (!LifecycleGate.Wait(0))
            throw new InvalidOperationException(
                "Voice lifecycle work is active. Use ConfigurePlatformAsync to replace the platform.");
        try
        {
            if (ReferenceEquals(_platform, platform))
                return;
            if (Session is not null || _platform is not null)
                throw new InvalidOperationException(
                    "Voice is already configured. Use ConfigurePlatformAsync to replace the platform.");
            _platform = platform;
        }
        finally
        {
            LifecycleGate.Release();
        }
    }

    public static async Task ConfigurePlatformAsync(IPlatformSpeechRecognizer platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        await LifecycleGate.WaitAsync();
        try
        {
            if (ReferenceEquals(_platform, platform))
                return;

            var previousSession = Session;
            var previousPlatform = _platform;
            Session = null;
            lock (Sync)
            {
                _callbacks = null;
                _activateOnNextNewDrinkMount = false;
            }
            if (previousSession is not null)
                await previousSession.DisposeAsync();
            if (previousPlatform is not null)
                await previousPlatform.DisposeAsync();
            _platform = platform;
        }
        finally
        {
            LifecycleGate.Release();
        }
    }

    public static PushToTalkVoiceService Bind(
        IBaristaVoiceCallbacks callbacks,
        IShotService shots,
        IBeanService beans,
        IBagService bags,
        IEquipmentService equipment,
        IUserProfileService profiles)
    {
        ArgumentNullException.ThrowIfNull(callbacks);
        LifecycleGate.Wait();
        try
        {
            if (_platform is null)
                throw new InvalidOperationException(
                    "The probe host must call ConfigurePlatform before binding BaristaNotes voice.");
            if (Session is not null)
                throw new InvalidOperationException(
                    "BaristaNotes voice is already bound. Reuse the app-scoped session.");

            lock (Sync)
                _callbacks = callbacks;
            var speech = new NativeSpeechRecognitionService(_platform, ownsPlatform: false);
            var commands = new BaristaVoiceCommandService(
                new VoiceCommandParser(),
                callbacks,
                shots,
                beans,
                bags,
                equipment,
                profiles);
            Session = new PushToTalkVoiceService(
                speech,
                commands,
                callbacks,
                Comet.ThreadHelper.RunOnMainThread);
            return Session;
        }
        finally
        {
            LifecycleGate.Release();
        }
    }

    public static Task OnPageDeactivatedAsync() => DeactivateAsync();

    public static Task OnAppBackgroundedAsync() => DeactivateAsync();

    static async Task DeactivateAsync()
    {
        var session = Session;
        if (session is null)
            return;
        try
        {
            await session.DeactivateAsync();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public static async Task ShutdownAsync()
    {
        await LifecycleGate.WaitAsync();
        try
        {
            var session = Session;
            var platform = _platform;
            Session = null;
            _platform = null;
            lock (Sync)
            {
                _callbacks = null;
                _activateOnNextNewDrinkMount = false;
            }

            if (session is not null)
                await session.DisposeAsync();
            if (platform is not null)
                await platform.DisposeAsync();
        }
        finally
        {
            LifecycleGate.Release();
        }
    }

    public static async Task UnbindAsync(IBaristaVoiceCallbacks callbacks)
    {
        ArgumentNullException.ThrowIfNull(callbacks);
        await LifecycleGate.WaitAsync();
        try
        {
            PushToTalkVoiceService? session;
            lock (Sync)
            {
                if (!ReferenceEquals(_callbacks, callbacks))
                    return;

                _callbacks = null;
                _activateOnNextNewDrinkMount = false;
                session = Session;
                Session = null;
            }

            if (session is not null)
                await session.DisposeAsync();
        }
        finally
        {
            LifecycleGate.Release();
        }
    }

    public static void RequestNewDrinkActivation()
    {
        lock (Sync)
            _activateOnNextNewDrinkMount = true;
    }

    public static void CancelPendingNewDrinkActivation()
    {
        lock (Sync)
            _activateOnNextNewDrinkMount = false;
    }

    public static bool NotifyNewDrinkMounted()
    {
        IBaristaVoiceCallbacks? callbacks;
        lock (Sync)
        {
            if (!_activateOnNextNewDrinkMount)
                return false;
            _activateOnNextNewDrinkMount = false;
            callbacks = _callbacks;
        }
        callbacks?.ActivateNewDrinkVoice();
        return true;
    }
}
