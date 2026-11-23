using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BaristaComparison;
using CometBaristaNotes.Services;

namespace CometBaristaNotes.Comparison;

public sealed class CometFixtureSession
{
    int _started;
    public FixtureSelection Selection { get; }
    public FixtureStore Store { get; }

    public CometFixtureSession(string appData, FixtureSelection selection, BaristaAppStorage storage,
        Func<string, bool> hasOrphanedPreferences)
    {
        var directory = Preflight(appData, selection, hasOrphanedPreferences);
        storage.ConfigureComparisonDatabase(Path.Combine(directory, "barista_notes.db"));
        Selection = selection;
        Store = new FixtureStore(appData, selection);
    }

    public static bool IsReview(FixtureSelection selection) =>
        selection == new FixtureSelection("review", 0, "run");

    public static string Preflight(string appData, FixtureSelection selection, Func<string, bool> hasOrphanedPreferences)
    {
        var directory = FixtureStore.NamespacePath(appData, selection);
        if (File.Exists(directory))
            throw new InvalidDataException("A file already occupies the fixture namespace.");
        if (hasOrphanedPreferences("barista_comparison_" + selection.Namespace))
            throw new InvalidDataException("External preferences already occupy this database-only fixture namespace.");
        if (Directory.Exists(directory))
        {
            var existing = new FixtureStore(appData, selection);
            if (!File.Exists(existing.DatabasePath) ||
                !File.Exists(Path.Combine(existing.DirectoryPath, "readback.json")))
                throw new InvalidDataException("A completed namespace is missing its database or readback.");
        }
        else if (selection.Mode != "provision")
            throw new InvalidDataException("A missing namespace requires explicit provision mode.");
        return directory;
    }

    public async Task<FixtureReadback?> StartAsync(IFixtureAdapter adapter, Func<int, byte[]> loadInput)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("This process already started the selected fixture operation.");
        if (Selection.Mode == "run")
            return null;

        var fixture = FixtureValidation.Parse(loadInput(Selection.DrinkCount), Selection.DrinkCount);
        return Store.IsNew
            ? await Store.ExecuteAsync(fixture, adapter)
            : await Store.ValidateAsync(fixture, adapter);
    }

    public static FixtureSelection ReadSelection(string path) =>
        JsonSerializer.Deserialize(File.ReadAllBytes(path), FixtureJson.Default.FixtureSelection)
        ?? throw new InvalidDataException("Fixture selection is null.");
}

public sealed record CometFixtureStatus(
    FixtureSelection Selection, string RunState, string? Error, string? ReadbackToken);

public sealed record CometFixtureReport(CometFixtureStatus Status, ProvisioningReceipt? Receipt);
public sealed record CometFixtureChunk(int Offset, int Length, int TotalBytes, string Sha256, string Base64);

public static class CometFixtureExports
{
    public static CometFixtureChunk Read(string appData, FixtureSelection selection, string token, int offset, int length)
    {
        _ = FixtureStore.NamespacePath(appData, selection);
        if (!Guid.TryParseExact(token, "N", out var id) || token != id.ToString("N") ||
            offset < 0 || length is < 1 or > 16384)
            throw new InvalidDataException("Invalid export token or chunk range.");
        var bytes = File.ReadAllBytes(Path.Combine(appData, "barista-comparison-control", "exports",
            selection.Namespace + "-" + token + ".json"));
        if (offset >= bytes.Length)
            throw new InvalidDataException("Export offset is outside the file.");
        var count = Math.Min(length, bytes.Length - offset);
        return new CometFixtureChunk(offset, count, bytes.Length, FixtureValidation.Hash(bytes),
            Convert.ToBase64String(bytes, offset, count));
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CometFixtureStatus))]
[JsonSerializable(typeof(CometFixtureReport))]
[JsonSerializable(typeof(CometFixtureChunk))]
public partial class CometFixtureStatusJson : JsonSerializerContext;
