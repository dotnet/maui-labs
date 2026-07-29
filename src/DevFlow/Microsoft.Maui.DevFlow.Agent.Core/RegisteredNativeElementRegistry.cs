using System.Runtime.CompilerServices;
using System.Reflection;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// Tracks platform-owned elements registered through MAUI diagnostics.
/// </summary>
/// <remarks>
/// Recently observed registrations are held strongly so Apple managed peers cannot be collected
/// while their native objects remain on screen. The strong set is bounded and stale entries are
/// evicted only after they have been absent from both the current and previous visual-tree walk.
/// </remarks>
internal sealed class RegisteredNativeElementRegistry
{
    private const int MaxTrackedElements = 512;

    private sealed class Entry
    {
        public required string Id { get; init; }
        public required object Owner { get; set; }
        public required object NativeElement { get; set; }
        public required string Role { get; set; }
        public string? Discriminator { get; set; }
        public string? StableKey { get; init; }
        public long LastSeenWalk { get; set; }
    }

    private readonly object _gate = new();
    private readonly ConditionalWeakTable<object, NativeElementIdentity> _identities = new();
    private readonly Dictionary<string, string> _idsByStableKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> _registrations = new(StringComparer.Ordinal);
    private readonly Func<object, string?> _stableKeySelector;
    private long _generation;
    private long _walk;

    public RegisteredNativeElementRegistry()
        : this(GetStableKey)
    {
    }

    internal RegisteredNativeElementRegistry(Func<object, string?> stableKeySelector)
        => _stableKeySelector = stableKeySelector;

    public long Generation
    {
        get
        {
            lock (_gate)
                return _generation;
        }
    }

    public void BeginWalk()
    {
        lock (_gate)
        {
            _walk++;
            if (_registrations.Count > MaxTrackedElements)
                Evict();
        }
    }

    public void MarkSeen(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        lock (_gate)
        {
            if (_registrations.TryGetValue(id, out var entry))
                entry.LastSeenWalk = _walk;
        }
    }

    public string Register(object owner, object nativeElement, string role, string? discriminator = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(nativeElement);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        lock (_gate)
        {
            var stableKey = _stableKeySelector(nativeElement);
            var id = GetOrCreateId(nativeElement, stableKey);

            if (_registrations.TryGetValue(id, out var existingRegistration))
            {
                if (ReferenceEquals(existingRegistration.Owner, owner))
                {
                    var changed = !existingRegistration.Role.Equals(role, StringComparison.Ordinal)
                        || !string.Equals(existingRegistration.Discriminator, discriminator, StringComparison.Ordinal);
                    existingRegistration.NativeElement = nativeElement;
                    existingRegistration.Role = role;
                    existingRegistration.Discriminator = discriminator;
                    existingRegistration.LastSeenWalk = _walk;
                    if (changed)
                        _generation++;
                    return id;
                }

                RemoveEntry(id);
                id = CreateId(nativeElement, stableKey);
            }

            _registrations[id] = new Entry
            {
                Id = id,
                Owner = owner,
                NativeElement = nativeElement,
                Role = role,
                Discriminator = discriminator,
                StableKey = stableKey,
                LastSeenWalk = _walk
            };
            _generation++;
            return id;
        }
    }

    public bool Unregister(object nativeElement)
    {
        ArgumentNullException.ThrowIfNull(nativeElement);

        lock (_gate)
        {
            var stableKey = _stableKeySelector(nativeElement);
            string? id = null;
            if (stableKey is not null)
                _idsByStableKey.TryGetValue(stableKey, out id);
            if (id is null && _identities.TryGetValue(nativeElement, out var identity))
                id = identity.Id;
            if (id is null || !_registrations.ContainsKey(id))
                return false;

            RemoveEntry(id);
            _identities.Remove(nativeElement);
            _generation++;
            return true;
        }
    }

    public bool TryGet(string id, out NativeElementRegistrationSnapshot registration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        lock (_gate)
        {
            if (_registrations.TryGetValue(id, out var entry))
            {
                entry.LastSeenWalk = _walk;
                registration = new NativeElementRegistrationSnapshot(
                    entry.Id,
                    entry.Owner,
                    entry.NativeElement,
                    entry.Role,
                    entry.Discriminator);
                return true;
            }

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

            return _registrations.Values
                .Select(entry => new NativeElementRegistrationSnapshot(
                    entry.Id,
                    entry.Owner,
                    entry.NativeElement,
                    entry.Role,
                    entry.Discriminator))
                .ToList();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (_registrations.Count == 0)
                return;

            foreach (var entry in _registrations.Values)
                _identities.Remove(entry.NativeElement);
            _registrations.Clear();
            _idsByStableKey.Clear();
            _generation++;
        }
    }

    private string GetOrCreateId(object nativeElement, string? stableKey)
    {
        if (stableKey is not null)
        {
            if (_idsByStableKey.TryGetValue(stableKey, out var stableId))
            {
                SetIdentity(nativeElement, stableId);
                return stableId;
            }

            return CreateId(nativeElement, stableKey);
        }

        if (_identities.TryGetValue(nativeElement, out var identity))
            return identity.Id;

        return CreateId(nativeElement, stableKey);
    }

    private string CreateId(object nativeElement, string? stableKey)
    {
        var id = $"native:registered:{Guid.NewGuid():N}";
        SetIdentity(nativeElement, id);
        if (stableKey is not null)
            _idsByStableKey[stableKey] = id;
        return id;
    }

    private void SetIdentity(object nativeElement, string id)
    {
        _identities.Remove(nativeElement);
        _identities.Add(nativeElement, new NativeElementIdentity(id));
    }

    private void Evict()
    {
        var protectedFrom = _walk - 1;
        var staleIds = _registrations
            .Where(pair => pair.Value.LastSeenWalk < protectedFrom)
            .OrderBy(pair => pair.Value.LastSeenWalk)
            .Take(_registrations.Count - MaxTrackedElements)
            .Select(pair => pair.Key)
            .ToList();

        if (staleIds.Count == 0)
            return;

        foreach (var id in staleIds)
            RemoveEntry(id);
        _generation++;
    }

    private void RemoveEntry(string id)
    {
        if (!_registrations.Remove(id, out var entry))
            return;

        _identities.Remove(entry.NativeElement);
        if (entry.StableKey is not null
            && _idsByStableKey.TryGetValue(entry.StableKey, out var mappedId)
            && mappedId == id)
        {
            _idsByStableKey.Remove(entry.StableKey);
        }
    }

    private static string? GetStableKey(object nativeElement)
    {
        if (!OperatingSystem.IsIOS()
            && !OperatingSystem.IsMacCatalyst()
            && !OperatingSystem.IsMacOS())
        {
            return null;
        }

        var handle = nativeElement.GetType()
            .GetProperty("Handle", BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(nativeElement);
        return TryGetHandleValue(handle, out var value) && value != 0
            ? $"objc:{value:x}"
            : null;
    }

    internal static bool TryGetHandleValue(object? handle, out long value)
    {
        switch (handle)
        {
            case IntPtr pointer:
                value = pointer.ToInt64();
                return true;
            case UIntPtr pointer:
                value = unchecked((long)pointer.ToUInt64());
                return true;
            case long signed:
                value = signed;
                return true;
            case ulong unsigned:
                value = unchecked((long)unsigned);
                return true;
        }

        if (handle is null)
        {
            value = 0;
            return false;
        }

        var handleType = handle.GetType();
        var conversion = handleType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method =>
            {
                if (method.Name is not ("op_Implicit" or "op_Explicit")
                    || method.ReturnType != typeof(IntPtr))
                {
                    return false;
                }

                var parameters = method.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType == handleType;
            });
        if (conversion?.Invoke(null, [handle]) is IntPtr converted)
        {
            value = converted.ToInt64();
            return true;
        }

        var toInt64 = handleType.GetMethod(
            "ToInt64",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        if (toInt64?.Invoke(handle, null) is long convertedValue)
        {
            value = convertedValue;
            return true;
        }

        value = 0;
        return false;
    }

    private sealed class NativeElementIdentity(string id)
    {
        public string Id { get; } = id;
    }
}

internal readonly record struct NativeElementRegistrationSnapshot(
    string Id,
    object Owner,
    object NativeElement,
    string Role,
    string? Discriminator);
