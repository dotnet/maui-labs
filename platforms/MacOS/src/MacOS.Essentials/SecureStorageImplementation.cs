using Foundation;
using Security;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

class SecureStorageImplementation : ISecureStorage
{
    const string ServiceName = "maui_secure_storage";

    public Task<string?> GetAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentNullException(nameof(key));

        return Task.FromResult(GetValue(key));
    }

    public Task SetAsync(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentNullException(nameof(key));
        ArgumentNullException.ThrowIfNull(value);

        SetValue(key, value);
        return Task.CompletedTask;
    }

    public bool Remove(string key)
    {
        using var record = new SecRecord(SecKind.GenericPassword)
        {
            Account = key,
            Service = ServiceName
        };
        using var match = SecKeyChain.QueryAsRecord(record, out var resultCode);
        if (resultCode == SecStatusCode.Success)
        {
            SecKeyChain.Remove(record);
            return true;
        }
        return false;
    }

    public void RemoveAll()
    {
        using var query = new SecRecord(SecKind.GenericPassword) { Service = ServiceName };
        SecKeyChain.Remove(query);
    }

    string? GetValue(string key)
    {
        using var record = new SecRecord(SecKind.GenericPassword)
        {
            Account = key,
            Service = ServiceName
        };
        using var match = SecKeyChain.QueryAsRecord(record, out var resultCode);
        if (resultCode == SecStatusCode.Success && match?.ValueData != null)
            return NSString.FromData(match.ValueData, NSStringEncoding.UTF8);
        return null;
    }

    void SetValue(string key, string value)
    {
        // Remove existing first
        Remove(key);

        using var newRecord = new SecRecord(SecKind.GenericPassword)
        {
            Account = key,
            Service = ServiceName,
            Label = key,
            Accessible = SecAccessible.AfterFirstUnlock,
            ValueData = NSData.FromString(value, NSStringEncoding.UTF8)
        };
        var result = SecKeyChain.Add(newRecord);
        if (result != SecStatusCode.Success)
            throw new InvalidOperationException($"Error adding secure storage record: {result}");
    }
}
