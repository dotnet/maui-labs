using System.Runtime.CompilerServices;

namespace Microsoft.Maui.DevFlow.Agent.Core;

internal sealed class NativeElementRegistrationRegistry
{
    private readonly object _gate = new();
    private readonly ConditionalWeakTable<object, NativeElementIdentity> _identities = new();
    private readonly Dictionary<string, NativeElementRegistration> _registrations = new(StringComparer.Ordinal);
    private long _generation;

    public long Generation
    {
        get
        {
            lock (_gate)
                return _generation;
        }
    }

    public string Register(object owner, object nativeElement, string role, string? discriminator = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(nativeElement);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        lock (_gate)
        {
            NativeElementIdentity identity;
            if (_identities.TryGetValue(nativeElement, out var existingIdentity))
            {
                if (_registrations.TryGetValue(existingIdentity.Id, out var existingRegistration)
                    && existingRegistration.Owner.TryGetTarget(out var existingOwner)
                    && ReferenceEquals(existingOwner, owner))
                {
                    if (existingRegistration.Role.Equals(role, StringComparison.Ordinal)
                        && string.Equals(existingRegistration.Discriminator, discriminator, StringComparison.Ordinal))
                    {
                        return existingIdentity.Id;
                    }

                    _registrations[existingIdentity.Id] = new NativeElementRegistration(
                        existingIdentity.Id,
                        new WeakReference<object>(owner),
                        new WeakReference<object>(nativeElement),
                        role,
                        discriminator);
                    _generation++;
                    return existingIdentity.Id;
                }

                _identities.Remove(nativeElement);
                _registrations.Remove(existingIdentity.Id);
            }

            identity = _identities.GetValue(nativeElement, static _ => new NativeElementIdentity());
            _registrations[identity.Id] = new NativeElementRegistration(
                identity.Id,
                new WeakReference<object>(owner),
                new WeakReference<object>(nativeElement),
                role,
                discriminator);
            _generation++;
            return identity.Id;
        }
    }

    public bool Unregister(object nativeElement)
    {
        ArgumentNullException.ThrowIfNull(nativeElement);

        lock (_gate)
        {
            if (!_identities.TryGetValue(nativeElement, out var identity))
                return false;

            _identities.Remove(nativeElement);
            if (!_registrations.Remove(identity.Id))
                return false;

            _generation++;
            return true;
        }
    }

    public bool TryGet(string id, out NativeElementRegistrationSnapshot registration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        lock (_gate)
        {
            if (_registrations.TryGetValue(id, out var entry)
                && entry.Owner.TryGetTarget(out var owner)
                && entry.NativeElement.TryGetTarget(out var nativeElement))
            {
                registration = new NativeElementRegistrationSnapshot(
                    entry.Id,
                    owner,
                    nativeElement,
                    entry.Role,
                    entry.Discriminator);
                return true;
            }

            if (_registrations.Remove(id))
                _generation++;
            registration = default;
            return false;
        }
    }

    public IReadOnlyList<NativeElementRegistrationSnapshot> GetSnapshot()
    {
        lock (_gate)
        {
            if (_registrations.Count == 0)
                return [];

            var registrations = new List<NativeElementRegistrationSnapshot>(_registrations.Count);
            List<string>? expiredIds = null;
            foreach (var entry in _registrations.Values)
            {
                if (entry.Owner.TryGetTarget(out var owner)
                    && entry.NativeElement.TryGetTarget(out var nativeElement))
                {
                    registrations.Add(new NativeElementRegistrationSnapshot(
                        entry.Id,
                        owner,
                        nativeElement,
                        entry.Role,
                        entry.Discriminator));
                }
                else
                {
                    (expiredIds ??= []).Add(entry.Id);
                }
            }

            if (expiredIds is not null)
            {
                foreach (var id in expiredIds)
                    _registrations.Remove(id);
                _generation++;
            }

            return registrations;
        }
    }

    private sealed class NativeElementIdentity
    {
        public string Id { get; } = $"native:registered:{Guid.NewGuid():N}";
    }

    private sealed record NativeElementRegistration(
        string Id,
        WeakReference<object> Owner,
        WeakReference<object> NativeElement,
        string Role,
        string? Discriminator);
}

internal readonly record struct NativeElementRegistrationSnapshot(
    string Id,
    object Owner,
    object NativeElement,
    string Role,
    string? Discriminator);
