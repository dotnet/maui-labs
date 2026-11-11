using System.Text;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.Cli.DevFlow;

internal sealed record StorageResponse(string Body, bool Success, bool IsRead);

internal sealed record FileDownloadResult(StorageResponse Response, JsonObject? Metadata = null, string? Error = null);

internal sealed class StorageOperations(HttpClient http, string baseUrl)
{
    internal Task<StorageResponse> GetRootsAsync() =>
        SendAsync(HttpMethod.Get, "/api/v1/storage/roots");

    internal Task<StorageResponse> ListPreferencesAsync(string? sharedName) =>
        SendAsync(HttpMethod.Get, "/api/v1/storage/preferences" + SharedNameQuery(sharedName));

    internal Task<StorageResponse> GetPreferenceAsync(string key, string type, string? sharedName) =>
        SendAsync(HttpMethod.Get, $"/api/v1/storage/preferences/{Uri.EscapeDataString(key)}?type={Uri.EscapeDataString(type)}"
            + (sharedName is null ? "" : $"&sharedName={Uri.EscapeDataString(sharedName)}"));

    internal Task<StorageResponse> SetPreferenceAsync(string key, string value, string type, string? sharedName) =>
        SendAsync(HttpMethod.Put, $"/api/v1/storage/preferences/{Uri.EscapeDataString(key)}", new JsonObject
        {
            ["value"] = value,
            ["type"] = type,
            ["sharedName"] = sharedName
        });

    internal Task<StorageResponse> DeletePreferenceAsync(string key, string? sharedName) =>
        SendAsync(HttpMethod.Delete, $"/api/v1/storage/preferences/{Uri.EscapeDataString(key)}" + SharedNameQuery(sharedName));

    internal Task<StorageResponse> ClearPreferencesAsync(string? sharedName) =>
        SendAsync(HttpMethod.Delete, "/api/v1/storage/preferences" + SharedNameQuery(sharedName));

    internal Task<StorageResponse> GetSecureAsync(string key) =>
        SendAsync(HttpMethod.Get, $"/api/v1/storage/secure/{Uri.EscapeDataString(key)}");

    internal Task<StorageResponse> SetSecureAsync(string key, string value) =>
        SendAsync(HttpMethod.Put, $"/api/v1/storage/secure/{Uri.EscapeDataString(key)}", new JsonObject
        {
            ["value"] = value
        });

    internal Task<StorageResponse> DeleteSecureAsync(string key) =>
        SendAsync(HttpMethod.Delete, $"/api/v1/storage/secure/{Uri.EscapeDataString(key)}");

    internal Task<StorageResponse> ClearSecureAsync() =>
        SendAsync(HttpMethod.Delete, "/api/v1/storage/secure");

    internal Task<StorageResponse> ListFilesAsync(string? path, string? root) =>
        SendAsync(HttpMethod.Get, "/api/v1/storage/files" + BuildFilesQuery(path, root));

    internal Task<StorageResponse> DownloadAsync(string path, string? root) =>
        SendAsync(HttpMethod.Get, FilePath(path, root));

    internal Task<StorageResponse> UploadAsync(string path, string? root, string contentBase64) =>
        SendAsync(HttpMethod.Put, FilePath(path, root), new JsonObject
        {
            ["contentBase64"] = contentBase64
        });

    internal Task<StorageResponse> DeleteFileAsync(string path, string? root) =>
        SendAsync(HttpMethod.Delete, FilePath(path, root));

    internal Task<StorageResponse> GetDeviceAsync(string resource) =>
        SendAsync(HttpMethod.Get, "/api/v1/device/" + resource);

    internal Task<StorageResponse> GetPermissionsAsync(string? permission) =>
        GetDeviceAsync(permission is null ? "permissions" : $"permissions/{Uri.EscapeDataString(permission)}");

    internal Task<StorageResponse> GetGeolocationAsync(string accuracy, int timeout) =>
        GetDeviceAsync($"geolocation?accuracy={Uri.EscapeDataString(accuracy)}&timeout={timeout}");

    internal Task<StorageResponse> StartSensorAsync(string sensor, string speed) =>
        SendAsync(HttpMethod.Post, $"/api/v1/device/sensors/{Uri.EscapeDataString(sensor)}/start?speed={Uri.EscapeDataString(speed)}");

    internal Task<StorageResponse> StopSensorAsync(string sensor) =>
        SendAsync(HttpMethod.Post, $"/api/v1/device/sensors/{Uri.EscapeDataString(sensor)}/stop");

    internal async Task<FileDownloadResult> DownloadToFileAsync(string devicePath, string? root, string destinationPath)
    {
        var response = await DownloadAsync(devicePath, root);
        if (!response.Success)
            return new(response);

        var metadata = CliJson.ParseNode(response.Body) as JsonObject;
        if (metadata is null || !metadata.TryGetPropertyValue("contentBase64", out var content)
            || content is null || content.GetValueKind() != System.Text.Json.JsonValueKind.String)
            return new(response, Error: "Download response did not include contentBase64.");

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(content.GetValue<string>());
        }
        catch (FormatException ex)
        {
            return new(response, Error: $"Download response contained invalid base64 content: {ex.Message}");
        }

        var localPath = ResolveDownloadDestinationPath(destinationPath, devicePath);
        var parentDirectory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(parentDirectory))
            Directory.CreateDirectory(parentDirectory);

        await File.WriteAllBytesAsync(localPath, bytes);

        metadata.Remove("contentBase64");
        metadata["success"] = true;
        metadata["localPath"] = localPath;

        return new(response, metadata);
    }

    internal static async Task<string> ReadUploadFileAsync(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Local file not found: {fullPath}", fullPath);

        return Convert.ToBase64String(await File.ReadAllBytesAsync(fullPath));
    }

    private async Task<StorageResponse> SendAsync(HttpMethod method, string path, JsonNode? body = null)
    {
        using var request = new HttpRequestMessage(method, baseUrl + path);
        if (body is not null)
            request.Content = new StringContent(CliJson.SerializeUntyped(body, indented: false), Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request);

        return new(await response.Content.ReadAsStringAsync(), response.IsSuccessStatusCode, method == HttpMethod.Get);
    }

    private static string SharedNameQuery(string? sharedName) =>
        sharedName is null ? "" : $"?sharedName={Uri.EscapeDataString(sharedName)}";

    private static string FilePath(string path, string? root) =>
        $"/api/v1/storage/files/{Uri.EscapeDataString(path)}{BuildFilesQuery(root: root)}";

    private static string BuildFilesQuery(string? path = null, string? root = null)
    {
        var query = new List<string>();
        if (!string.IsNullOrEmpty(path))
            query.Add($"path={Uri.EscapeDataString(path)}");
        if (!string.IsNullOrEmpty(root))
            query.Add($"root={Uri.EscapeDataString(root)}");

        return query.Count == 0 ? "" : "?" + string.Join("&", query);
    }

    private static string ResolveDownloadDestinationPath(string destinationPath, string devicePath)
    {
        var fullDestinationPath = Path.GetFullPath(destinationPath);
        if (Directory.Exists(fullDestinationPath) || EndsWithDirectorySeparator(destinationPath))
            return Path.Combine(fullDestinationPath, GetDeviceFileName(devicePath));

        return fullDestinationPath;
    }

    private static bool EndsWithDirectorySeparator(string path) =>
        path.Length > 0 && (path[^1] == Path.DirectorySeparatorChar || path[^1] == Path.AltDirectorySeparatorChar);

    private static string GetDeviceFileName(string devicePath)
    {
        var normalizedPath = devicePath.Replace('\\', '/').TrimEnd('/');
        var fileName = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (string.IsNullOrEmpty(fileName))
            throw new ArgumentException("The device path must include a file name.", nameof(devicePath));

        return fileName;
    }
}
