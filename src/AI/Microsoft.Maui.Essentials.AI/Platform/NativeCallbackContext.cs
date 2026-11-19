namespace Microsoft.Maui.Essentials.AI;

internal sealed class NativeCallbackContext
{
    private readonly ExecutionContext? _context = ExecutionContext.Capture();

    public Task RunAsync(Func<Task> callback)
    {
        if (_context is null)
            return callback();

        Task? task = null;
        ExecutionContext.Run(_context.CreateCopy(), _ => task = callback(), null);
        return task!;
    }
}
