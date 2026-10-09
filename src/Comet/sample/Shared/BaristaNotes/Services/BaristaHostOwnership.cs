using System;
using System.Threading;
using System.Threading.Tasks;

namespace CometBaristaNotes.Services;

public sealed class BaristaHostOwnership
{
    readonly SemaphoreSlim _gate = new(1, 1);
    object? _owner;
    Func<Task>? _release;

    public bool IsOwner(object owner) => ReferenceEquals(Volatile.Read(ref _owner), owner);

    public async Task AcquireAsync(object owner, Func<Task> release)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(release);
        await _gate.WaitAsync();
        try
        {
            if (ReferenceEquals(_owner, owner))
                return;
            if (_release is not null)
                await _release();
            _owner = owner;
            _release = release;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReleaseAsync(object owner)
    {
        await _gate.WaitAsync();
        try
        {
            if (!ReferenceEquals(_owner, owner))
                return;
            if (_release is not null)
                await _release();
            _owner = null;
            _release = null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
