// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// A disposable subscription returned by <see cref="ContentBlock.OnChanged"/>; dispose it to stop
/// receiving change notifications for that block.
/// </summary>
public readonly struct ContentBlockChangedSubscription : IDisposable
{
    private readonly ContentBlock _owner;
    private readonly Action _callback;

    internal ContentBlockChangedSubscription(ContentBlock owner, Action callback)
    {
        _owner = owner;
        _callback = callback;
    }

    public void Dispose()
    {
        _owner?.RemoveCallback(_callback);
    }
}
