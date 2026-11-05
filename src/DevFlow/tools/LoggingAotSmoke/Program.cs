using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.DevFlow.Logging;

if (RuntimeFeature.IsDynamicCodeSupported || JsonSerializer.IsReflectionEnabledByDefault)
    throw new InvalidOperationException("Run the published Native AOT executable with reflection JSON disabled.");

var directory = Path.Combine(Path.GetTempPath(), $"devflow-logging-nativeaot-{Guid.NewGuid():N}");
Directory.CreateDirectory(directory);
try
{
    using var gate = new ReaderWriterLockSlim();
    using var writer = new FileLogWriter(directory, gate);
    var reader = new FileLogReader(directory, gate, writer);
    var entry = new FileLogEntry(
        new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc),
        "Information", "NativeAot", "Native \"JSON\"\nCaf\u00e9");
    writer.Write(entry);
    Require(reader.Read().Single() == entry, "Buffered log entry did not round-trip.");

    using (var capture = new ConsoleLogCapture(writer))
    {
        capture.Install(captureTrace: false);
        Console.WriteLine("Native AOT console capture");
    }
    Require(reader.Read(source: "console.out").Single().Message == "Native AOT console capture",
        "Console capture did not round-trip.");
    writer.Dispose();

    var persisted = reader.Read();
    Require(persisted.Count == 2 && persisted.Contains(entry),
        "Persisted entries did not round-trip.");
    using (var json = JsonDocument.Parse(File.ReadLines(Path.Combine(directory, "log-current.jsonl")).First()))
    {
        Require(json.RootElement.EnumerateObject().Select(property => property.Name)
            .SequenceEqual(new[] { "t", "l", "c", "m", "e", "s" }),
            "JSONL field names changed.");
        Require(json.RootElement.GetProperty("e").ValueKind == JsonValueKind.Null,
            "Optional null field was omitted.");
    }

    using (var provider = new FileLogProvider(Path.Combine(directory, "provider")))
    {
        provider.CreateLogger("NativeAotProvider").LogWarning("Native AOT ILogger {Count}", 3);
        Require(provider.Reader.Read().Single().Message == "Native AOT ILogger 3",
            "ILogger entry did not round-trip.");
    }

    Console.WriteLine("PASS NativeAOT logging: reflection=false dynamicCode=false buffer/disk/console/ILogger/jsonl=true");
}
finally
{
    Directory.Delete(directory, recursive: true);
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
