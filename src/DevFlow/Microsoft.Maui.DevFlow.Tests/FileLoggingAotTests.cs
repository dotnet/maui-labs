using System.Text.Json;
using Microsoft.Maui.DevFlow.Logging;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class FileLoggingAotTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), $"devflow-log-aot-{Guid.NewGuid():N}");

    public FileLoggingAotTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void Entry_RoundTripsThroughBufferAndDisk()
    {
        using var gate = new ReaderWriterLockSlim();
        using var writer = new FileLogWriter(_directory, gate);
        var reader = new FileLogReader(_directory, gate, writer);
        var entry = new FileLogEntry(
            new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc),
            "Warning", "NativeAot", "Espresso \"test\"\nCaf\u00e9", "Exception text", "native");

        gate.EnterReadLock();
        try
        {
            writer.Write(entry);
            Assert.Equal(entry, Assert.Single(writer.GetBufferedEntries()));
        }
        finally
        {
            gate.ExitReadLock();
        }

        writer.Flush();
        Assert.Equal(entry, Assert.Single(reader.Read()));

        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "log-current.jsonl")));
        Assert.Equal(new[] { "t", "l", "c", "m", "e", "s" },
            json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(entry.Timestamp, json.RootElement.GetProperty("t").GetDateTime());
        Assert.Equal(entry.Message, json.RootElement.GetProperty("m").GetString());
    }

    [Fact]
    public void NullOptionalFields_AreWrittenAndRestored()
    {
        using var gate = new ReaderWriterLockSlim();
        using var writer = new FileLogWriter(_directory, gate);
        var reader = new FileLogReader(_directory, gate, writer);
        var entry = new FileLogEntry(DateTime.UtcNow, "Information", "Test", "Message");

        writer.Write(entry);
        writer.Flush();

        Assert.Equal(entry, Assert.Single(reader.Read()));
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "log-current.jsonl")));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("e").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("s").ValueKind);
    }

    [Fact]
    public void LegacyAndMalformedLines_PreserveValidRecords()
    {
        File.WriteAllText(Path.Combine(_directory, "log-current.jsonl"),
            """
            {"t":"2026-09-21T12:00:00Z","l":"Information","c":"Legacy","m":"Previous format"}
            not-json
            {"t":"2026-09-21T12:00:00Z","l":42,"c":"Invalid","m":"Wrong type"}

            """);
        using var gate = new ReaderWriterLockSlim();
        using var writer = new FileLogWriter(_directory, gate);
        var reader = new FileLogReader(_directory, gate, writer);

        var entry = Assert.Single(reader.Read());
        Assert.Equal("Previous format", entry.Message);
        Assert.Null(entry.Exception);
        Assert.Null(entry.Source);
    }

    [Fact]
    public void RealReader_FiltersAndPagesBufferedAndPersistedEntries()
    {
        using var gate = new ReaderWriterLockSlim();
        using var writer = new FileLogWriter(_directory, gate);
        var reader = new FileLogReader(_directory, gate, writer);
        var start = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
        for (var index = 0; index < 6; index++)
            writer.Write(new FileLogEntry(start.AddSeconds(index), "Information", "Test",
                $"Message {index}", Source: index % 2 == 0 ? "native" : "console.out"));

        Assert.Equal("Message 2", Assert.Single(reader.Read(limit: 1, skip: 1, source: "NATIVE")).Message);
        writer.Flush();
        Assert.Equal("Message 2", Assert.Single(reader.Read(limit: 1, skip: 1, source: "NATIVE")).Message);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
