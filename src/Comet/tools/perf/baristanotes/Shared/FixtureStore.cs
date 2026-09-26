using System.Text.Json;
using System.Text.RegularExpressions;

namespace BaristaComparison;

public sealed partial class FixtureStore
{
    private int _started;
    public string DirectoryPath { get; }
    public string DatabasePath => Path.Combine(DirectoryPath, "barista_notes.db");
    public string PreferencesName => "barista_comparison_" + Receipt.Namespace;
    public ProvisioningReceipt Receipt { get; }
    public bool IsNew { get; }
    private string ReceiptPath => Path.Combine(DirectoryPath, "receipt.json");

    [GeneratedRegex(@"\Afixture-v2-(100|1000)-[a-z0-9]{1,16}\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamespacePattern();

    public static string NamespacePath(string appData, FixtureSelection selection)
    {
        FixtureValidation.Require(NamespacePattern().IsMatch(selection.Namespace) &&
            selection.Namespace.StartsWith($"fixture-v2-{selection.DrinkCount}-", StringComparison.Ordinal),
            "Invalid fixture namespace.");
        _ = FixtureValidation.ExpectedInputHash(selection.DrinkCount);
        FixtureValidation.Require(selection.Mode is "provision" or "validate" or "run", "Invalid mode.");
        return Path.Combine(appData, "barista-comparison", selection.Namespace);
    }

    public FixtureStore(string appData, FixtureSelection selection)
    {
        DirectoryPath = NamespacePath(appData, selection);
        if (Directory.Exists(DirectoryPath))
        {
            if (!File.Exists(ReceiptPath))
                throw new InvalidDataException("Existing namespace has no receipt; no changes allowed.");
            Receipt = JsonSerializer.Deserialize(File.ReadAllBytes(ReceiptPath), FixtureJson.Default.ProvisioningReceipt)
                ?? throw new InvalidDataException("Null receipt.");
            FixtureValidation.Require(Receipt.Namespace == selection.Namespace &&
                Receipt.FixtureName == $"barista-perf-v2-{selection.DrinkCount}" &&
                Receipt.InputSha256 == FixtureValidation.ExpectedInputHash(selection.DrinkCount) &&
                Receipt.State == "complete" && Receipt.SemanticSha256 is { Length: 64 },
                "Existing namespace is incomplete, failed, or mismatched; no changes allowed.");
        }
        else
        {
            FixtureValidation.Require(selection.Mode == "provision", "Missing namespace requires explicit provision mode.");
            Directory.CreateDirectory(DirectoryPath);
            Receipt = new()
            {
                Namespace = selection.Namespace,
                FixtureName = $"barista-perf-v2-{selection.DrinkCount}",
                InputSha256 = FixtureValidation.ExpectedInputHash(selection.DrinkCount)
            };
            using var stream = new FileStream(ReceiptPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, Receipt, FixtureJson.Default.ProvisioningReceipt);
            stream.Flush(true);
            IsNew = true;
        }
    }

    public async Task<FixtureReadback> ExecuteAsync(FixtureDefinition fixture, IFixtureAdapter adapter)
    {
        FixtureValidation.Validate(fixture);
        FixtureValidation.Require(Receipt.FixtureName == fixture.Name, "Fixture/receipt mismatch.");
        if (!IsNew)
            return await ValidateAsync(fixture, adapter);
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("Provisioning has already been started; retries are forbidden.");
        try
        {
            foreach (var value in fixture.Beans)
                await Create("bean", value.Key, Receipt.Ids.Beans, () => adapter.CreateBeanAsync(value));
            foreach (var value in fixture.Bags)
                await Create("bag", value.Key, Receipt.Ids.Bags, () => adapter.CreateBagAsync(value, Receipt.Ids));
            foreach (var value in fixture.Equipment)
                await Create("equipment", value.Key, Receipt.Ids.Equipment, () => adapter.CreateEquipmentAsync(value));
            foreach (var value in fixture.People)
                await Create("person", value.Key, Receipt.Ids.People, () => adapter.CreatePersonAsync(value));
            foreach (var value in fixture.Drinks)
                await Create("drink", value.Key, Receipt.Ids.Drinks, () => adapter.CreateDrinkAsync(value, Receipt.Ids));
            Receipt.PendingOperation = "preferences";
            Save();
            await adapter.ApplyPreferencesAsync(fixture, Receipt.Ids);
            Receipt.PendingOperation = "fresh-scope-readback";
            Save();
            var readback = await adapter.ReadAsync(Receipt.Ids);
            FixtureValidation.AssertReadback(fixture, readback);
            var canonical = FixtureValidation.Canonical(readback);
            using (var stream = new FileStream(Path.Combine(DirectoryPath, "readback.json"), FileMode.CreateNew))
            {
                stream.Write(canonical);
                stream.Flush(true);
            }
            Receipt.SemanticSha256 = FixtureValidation.Hash(canonical);
            Receipt.PendingOperation = null;
            Receipt.State = "complete";
            Save();
            return readback;
        }
        catch (Exception error)
        {
            Receipt.State = "failed";
            Receipt.Failure = error.ToString();
            Save();
            throw;
        }
    }

    public async Task<FixtureReadback> ValidateAsync(FixtureDefinition fixture, IFixtureAdapter adapter)
    {
        FixtureValidation.Require(Receipt.State == "complete", "Namespace is not complete.");
        var readback = await adapter.ReadAsync(Receipt.Ids);
        FixtureValidation.AssertReadback(fixture, readback);
        FixtureValidation.Require(FixtureValidation.Hash(FixtureValidation.Canonical(readback)) == Receipt.SemanticSha256,
            "Persisted values no longer match the completion receipt.");
        return readback;
    }

    private async Task Create(string kind, string key, Dictionary<string, string> ids, Func<Task<string>> create)
    {
        Receipt.PendingOperation = $"{kind}:{key}";
        Save();
        var id = await create();
        FixtureValidation.Require(!string.IsNullOrWhiteSpace(id) && !ids.ContainsValue(id), "Invalid/duplicate native ID.");
        ids.Add(key, id);
        Receipt.PendingOperation = null;
        Save();
    }

    private void Save() => AtomicWrite(ReceiptPath,
        JsonSerializer.SerializeToUtf8Bytes(Receipt, FixtureJson.Default.ProvisioningReceipt));

    public static void AtomicWrite(string path, byte[] bytes)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(true);
        }
        File.Move(temporary, path, overwrite: true);
    }
}
