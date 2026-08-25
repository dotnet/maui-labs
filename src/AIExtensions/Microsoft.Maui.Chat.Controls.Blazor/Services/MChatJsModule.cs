// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Microsoft.Maui.Chat.Controls.Blazor;

/// <summary>
/// Lazy-loaded wrapper around the <c>mchat.js</c> module. Components inject an
/// <see cref="IJSRuntime"/> and grab this helper to keep call sites tight.
/// </summary>
internal sealed class MChatJsModule : IAsyncDisposable
{
    private const string ModulePath = "./_content/Microsoft.Maui.Chat.Controls.Blazor/mchat.js";

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;

    public MChatJsModule(IJSRuntime js) => _js = js;

    private async ValueTask<IJSObjectReference> GetModuleAsync()
    {
        // Cache the module reference so subsequent calls skip the dynamic import.
        return _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
    }

    public async ValueTask FocusAsync(ElementReference element)
    {
        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("focus", element);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async ValueTask AutoSizeAsync(ElementReference element)
    {
        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("autoSize", element);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async ValueTask ScrollToBottomAsync(ElementReference element)
    {
        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("scrollToBottom", element);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async ValueTask<long> StickToBottomAsync<T>(
        ElementReference element,
        DotNetObjectReference<T> peer) where T : class
    {
        try
        {
            var module = await GetModuleAsync();
            return await module.InvokeAsync<long>("stickToBottom", element, peer);
        }
        catch (JSDisconnectedException)
        {
            return 0;
        }
        catch (ObjectDisposedException)
        {
            return 0;
        }
    }

    public async ValueTask ReleaseStickToBottomAsync(long handle)
    {
        if (handle == 0)
        {
            return;
        }

        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("releaseStickToBottom", handle);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async ValueTask PlayAudioAsync(ElementReference element)
    {
        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("playAudio", element);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async ValueTask PauseAudioAsync(ElementReference element)
    {
        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("pauseAudio", element);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        _module = null;
    }
}
