using System.Diagnostics;

namespace Microsoft.Maui.DevFlow.Agent.Core;

internal sealed class MauiNativeElementDiagnosticSubscriber :
    IObserver<DiagnosticListener>,
    IObserver<KeyValuePair<string, object?>>,
    IDisposable
{
    internal const string ListenerName = "Microsoft.Maui.NativeElements";
    internal const int ContractVersion = 1;
    internal const string RegisteredEventName = "Microsoft.Maui.NativeElements.Registered.v1";
    internal const string UnregisteredEventName = "Microsoft.Maui.NativeElements.Unregistered.v1";
    internal const string LegacyRegisteredEventName = "Microsoft.Maui.NativeElements.Registered";
    internal const string LegacyUnregisteredEventName = "Microsoft.Maui.NativeElements.Unregistered";

    private readonly object _gate = new();
    private readonly NativeElementRegistrationRegistry _registry;
    private readonly IDisposable _allListenersSubscription;
    private readonly List<IDisposable> _listenerSubscriptions = [];
    private bool _disposed;

    public MauiNativeElementDiagnosticSubscriber(NativeElementRegistrationRegistry registry)
    {
        _registry = registry;
        _allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(this);
    }

    public void OnNext(DiagnosticListener listener)
    {
        if (_disposed || !listener.Name.Equals(ListenerName, StringComparison.Ordinal))
            return;

        var subscription = listener.Subscribe(this);
        lock (_gate)
        {
            if (_disposed)
            {
                subscription.Dispose();
                return;
            }

            _listenerSubscriptions.Add(subscription);
        }
    }

    public void OnNext(KeyValuePair<string, object?> diagnosticEvent)
    {
        if (_disposed)
            return;

        try
        {
            if (diagnosticEvent.Key.Equals(RegisteredEventName, StringComparison.Ordinal)
                && diagnosticEvent.Value is object?[] { Length: >= 4 } versionedPayload
                && versionedPayload[0] is int version
                && version == ContractVersion
                && versionedPayload[1] is { } versionedOwner
                && versionedPayload[2] is { } versionedNativeElement
                && versionedPayload[3] is string versionedRole)
            {
                _registry.Register(
                    versionedOwner,
                    versionedNativeElement,
                    versionedRole,
                    versionedPayload.Length > 4 ? versionedPayload[4] as string : null);
                return;
            }

            if (diagnosticEvent.Key.Equals(LegacyRegisteredEventName, StringComparison.Ordinal)
                && diagnosticEvent.Value is object?[] { Length: >= 3 } legacyPayload
                && legacyPayload[0] is { } legacyOwner
                && legacyPayload[1] is { } legacyNativeElement
                && legacyPayload[2] is string legacyRole)
            {
                _registry.Register(
                    legacyOwner,
                    legacyNativeElement,
                    legacyRole,
                    legacyPayload.Length > 3 ? legacyPayload[3] as string : null);
                return;
            }

            if (diagnosticEvent.Key.Equals(UnregisteredEventName, StringComparison.Ordinal)
                && diagnosticEvent.Value is object?[] { Length: >= 2 } versionedUnregisterPayload
                && versionedUnregisterPayload[0] is int unregisterVersion
                && unregisterVersion == ContractVersion
                && versionedUnregisterPayload[1] is { } versionedUnregisteredNativeElement)
            {
                _registry.Unregister(versionedUnregisteredNativeElement);
                return;
            }

            if (diagnosticEvent.Key.Equals(LegacyUnregisteredEventName, StringComparison.Ordinal)
                && diagnosticEvent.Value is { } legacyUnregisteredNativeElement)
            {
                if (legacyUnregisteredNativeElement is object?[] { Length: > 0 } unregisterPayload
                    && unregisterPayload[0] is { } payloadNativeElement)
                {
                    _registry.Unregister(payloadNativeElement);
                }
                else
                {
                    _registry.Unregister(legacyUnregisteredNativeElement);
                }
            }
        }
        catch (ArgumentException ex)
        {
            Debug.WriteLine(
                $"[Microsoft.Maui.DevFlow] Ignored malformed native-element diagnostic event '{diagnosticEvent.Key}': {ex.Message}");
        }
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            foreach (var subscription in _listenerSubscriptions)
                subscription.Dispose();
            _listenerSubscriptions.Clear();
        }

        _allListenersSubscription.Dispose();
    }
}
