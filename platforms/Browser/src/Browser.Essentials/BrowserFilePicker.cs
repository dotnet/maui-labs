using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Platforms.Browser.Essentials;

/// <summary>
/// File picker backed by a hidden &lt;input type="file"&gt; element. Picked file contents
/// are copied into the in-memory WebAssembly file system so the returned
/// <see cref="FileResult"/> paths are readable with regular System.IO APIs.
/// PickOptions.FileTypes is platform-keyed and has no browser entry, so it is ignored.
/// </summary>
[SupportedOSPlatform("browser")]
public class BrowserFilePicker : IFilePicker
{
	public async Task<FileResult?> PickAsync(PickOptions? options = null)
	{
		var results = await PickCoreAsync(multiple: false).ConfigureAwait(false);
		return results.FirstOrDefault();
	}

	public async Task<IEnumerable<FileResult?>> PickMultipleAsync(PickOptions? options = null) =>
		await PickCoreAsync(multiple: true).ConfigureAwait(false);

	static async Task<IEnumerable<FileResult>> PickCoreAsync(bool multiple)
	{
		await BrowserEssentials.WhenInitializedAsync().ConfigureAwait(false);
		var json = await BrowserEssentialsInterop.PickFiles(null, multiple).ConfigureAwait(false);

		using var doc = JsonDocument.Parse(json);
		var results = new List<FileResult>();
		var pickDirectory = Path.Combine(Path.GetTempPath(), "maui-filepicker", Guid.NewGuid().ToString("N"));
		foreach (var file in doc.RootElement.EnumerateArray())
		{
			var name = file.GetProperty("name").GetString() ?? "file";
			var contentType = file.GetProperty("type").GetString();
			var bytes = Convert.FromBase64String(file.GetProperty("dataBase64").GetString() ?? string.Empty);

			Directory.CreateDirectory(pickDirectory);
			var path = Path.Combine(pickDirectory, name);
			await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);

			var result = new FileResult(path, string.IsNullOrEmpty(contentType) ? "application/octet-stream" : contentType);
			results.Add(result);
		}
		return results;
	}
}
