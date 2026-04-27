using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

/// <summary>
/// Bridges MAUI's IDispatcher to Blazor's abstract Dispatcher.
/// </summary>
internal class MacOSBlazorDispatcher : Microsoft.AspNetCore.Components.Dispatcher
{
    private readonly IDispatcher _dispatcher;

    public MacOSBlazorDispatcher(IDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public override bool CheckAccess() => _dispatcher.IsDispatchRequired == false;

    public override Task InvokeAsync(Action workItem)
    {
        var tcs = new TaskCompletionSource();
        _dispatcher.Dispatch(() =>
        {
            try
            {
                workItem();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    public override Task InvokeAsync(Func<Task> workItem)
    {
        var tcs = new TaskCompletionSource();
        _dispatcher.Dispatch(async () =>
        {
            try
            {
                await workItem();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    public override Task<TResult> InvokeAsync<TResult>(Func<TResult> workItem)
    {
        var tcs = new TaskCompletionSource<TResult>();
        _dispatcher.Dispatch(() =>
        {
            try
            {
                tcs.SetResult(workItem());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    public override Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> workItem)
    {
        var tcs = new TaskCompletionSource<TResult>();
        _dispatcher.Dispatch(async () =>
        {
            try
            {
                tcs.SetResult(await workItem());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }
}
