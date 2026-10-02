using System.Text.Json;
using AppKit;
using CoreGraphics;

namespace MacOS.RuntimeTests;

public sealed class RuntimeTestContext
{
    readonly RuntimeScenario _scenario;
    readonly object _gate = new();
    readonly HashSet<string> _cases = new(StringComparer.Ordinal);
    int _assertions;
    int _assertionsAtLastCase;
    bool _terminal;

    public RuntimeTestContext(RuntimeScenario scenario, string evidenceDirectory)
    {
        _scenario = scenario;
        EvidenceDirectory = Path.GetFullPath(evidenceDirectory);
        Directory.CreateDirectory(EvidenceDirectory);
        if (File.Exists(Path.Combine(EvidenceDirectory, "result.json")))
            throw new InvalidOperationException("Evidence directory already contains a terminal result.");
    }

    public string EvidenceDirectory { get; }

    public void Assert(bool condition, string message, string failureId = "assertion")
    {
        lock (_gate)
        {
            _assertions++;
            File.AppendAllText(EvidencePath("assertions.txt"),
                $"{_assertions}: {(condition ? "PASS" : "FAIL")} {message}{Environment.NewLine}");
            if (!condition)
                throw new RuntimeAssertionException(failureId, message);
        }
    }

    public void Pass(string name)
    {
        lock (_gate)
        {
            if (_assertions <= _assertionsAtLastCase || !_cases.Add(name))
                throw new InvalidOperationException($"Case '{name}' is duplicate or has no new assertions.");
            _assertionsAtLastCase = _assertions;
            Console.WriteLine($"PASS {name}");
        }
    }

    public void Complete()
    {
        lock (_gate)
        {
            if (_assertions == 0 || _cases.Count != _scenario.ExpectedCases ||
                (_scenario.ExpectedAssertions is { } expected && _assertions != expected))
                throw new InvalidOperationException(
                    $"Incomplete scenario: cases {_cases.Count}/{_scenario.ExpectedCases}, " +
                    $"assertions {_assertions}/{_scenario.ExpectedAssertions?.ToString() ?? "positive"}.");
            Finish("passed", 0, null, "All declared cases passed.");
        }
    }

    public void BaselineFailure(string failureId, string message)
    {
        lock (_gate)
        {
            if (_assertions == 0 || string.IsNullOrWhiteSpace(failureId))
                throw new InvalidOperationException("A baseline failure needs observed assertions and a concrete id.");
            Finish("regression", 42, failureId, message);
        }
    }

    public void Fail(Exception exception)
    {
        lock (_gate)
        {
            Console.Error.WriteLine(exception);
            Finish("failed", 1, exception is RuntimeAssertionException assertion
                ? assertion.FailureId : "exception", exception.Message);
        }
    }

    void Finish(string outcome, int exitCode, string? failureId, string message)
    {
        if (_terminal)
        {
            Console.Error.WriteLine($"Ignored duplicate terminal result: {outcome}: {message}");
            return;
        }
        _terminal = true;
        try
        {
            var json = JsonSerializer.Serialize(new
            {
                scenario = _scenario.Name, outcome, exitCode,
                assertions = _assertions, cases = _cases.Count,
                expectedCases = _scenario.ExpectedCases, expectedAssertions = _scenario.ExpectedAssertions,
                failureId, message
            });
            var temporary = EvidencePath("result.json.tmp");
            File.WriteAllText(temporary, json);
            File.Move(temporary, EvidencePath("result.json"));
            Console.WriteLine($"RUNTIME_RESULT {json}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL writing terminal evidence: {ex}");
            exitCode = 1;
        }
        Environment.Exit(exitCode);
    }

    public string EvidencePath(string name)
    {
        var path = Path.GetFullPath(Path.Combine(EvidenceDirectory, name));
        if (!path.StartsWith(EvidenceDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Evidence path must remain inside the scenario directory.", nameof(name));
        return path;
    }

    public void WriteJson(string name, object value) =>
        File.WriteAllText(EvidencePath(name), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));

    public void AppendJson(string name, object value) =>
        File.AppendAllText(EvidencePath(name), JsonSerializer.Serialize(value) + Environment.NewLine);

    public void Capture(NSView view, string name)
    {
        view.LayoutSubtreeIfNeeded();
        view.DisplayIfNeeded();
        var bounds = view.Bounds;
        var scale = view.Window?.BackingScaleFactor ?? 1;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("Cannot capture an empty native view.");
        using var bitmap = new NSBitmapImageRep(IntPtr.Zero,
            (nint)(bounds.Width * scale), (nint)(bounds.Height * scale),
            8, 4, true, false, NSColorSpace.DeviceRGB, 0, 0);
        bitmap.Size = bounds.Size;
        NSGraphicsContext.GlobalSaveGraphicsState();
        try
        {
            using var graphics = NSGraphicsContext.FromBitmap(bitmap)
                ?? throw new InvalidOperationException("Could not create screenshot context.");
            NSGraphicsContext.CurrentContext = graphics;
            view.CacheDisplay(bounds, bitmap);
            graphics.CGContext.SetBlendMode(CGBlendMode.DestinationOver);
            graphics.CGContext.SetFillColor(1, 1, 1, 1);
            graphics.CGContext.FillRect(new CGRect(0, 0, bitmap.PixelsWide, bitmap.PixelsHigh));
        }
        finally
        {
            NSGraphicsContext.GlobalRestoreGraphicsState();
        }
        using var png = bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png)
            ?? throw new InvalidOperationException("Could not encode screenshot.");
        File.WriteAllBytes(EvidencePath(name), png.ToArray());
    }

    public static Task FlushMainQueueAsync()
    {
        // Without a synchronization context, resume the awaiting fixture on AppKit's thread.
        var completion = new TaskCompletionSource();
        NSApplication.SharedApplication.BeginInvokeOnMainThread(() =>
            NSApplication.SharedApplication.BeginInvokeOnMainThread(() => completion.SetResult()));
        return completion.Task;
    }
}

sealed class RuntimeAssertionException(string failureId, string message) : Exception(message)
{
    public string FailureId { get; } = failureId;
}
