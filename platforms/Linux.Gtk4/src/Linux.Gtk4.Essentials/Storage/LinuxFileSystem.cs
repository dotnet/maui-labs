using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Storage;

public class LinuxFileSystem : IFileSystem
{
	public string CacheDirectory
	{
		get
		{
			var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
			var path = !string.IsNullOrEmpty(xdgCache)
				? Path.Combine(xdgCache, AppDomain.CurrentDomain.FriendlyName)
				: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
					".cache", AppDomain.CurrentDomain.FriendlyName);
			Directory.CreateDirectory(path);
			return path;
		}
	}

	public string AppDataDirectory
	{
		get
		{
			var xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
			var path = !string.IsNullOrEmpty(xdgData)
				? Path.Combine(xdgData, AppDomain.CurrentDomain.FriendlyName)
				: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
					".local", "share", AppDomain.CurrentDomain.FriendlyName);
			Directory.CreateDirectory(path);
			return path;
		}
	}

	public Task<Stream> OpenAppPackageFileAsync(string filename)
	{
		var basePath = AppContext.BaseDirectory;
		var filePath = Path.Combine(basePath, filename);
		if (!File.Exists(filePath))
			throw new FileNotFoundException($"App package file not found: {filename}", filePath);
		return Task.FromResult<Stream>(File.OpenRead(filePath));
	}

	public Task<bool> AppPackageFileExistsAsync(string filename)
	{
		var basePath = AppContext.BaseDirectory;
		return Task.FromResult(File.Exists(Path.Combine(basePath, filename)));
	}
}
