using Microsoft.Maui.Storage;

namespace Platform.Maui.Linux.Gtk4.Essentials.Storage;

public class LinuxFileSystem : IFileSystem
{
	public string CacheDirectory
	{
		get
		{
			var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
			if (!string.IsNullOrEmpty(xdgCache))
				return Path.Combine(xdgCache, AppDomain.CurrentDomain.FriendlyName);
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				".cache", AppDomain.CurrentDomain.FriendlyName);
		}
	}

	public string AppDataDirectory
	{
		get
		{
			var xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
			if (!string.IsNullOrEmpty(xdgData))
				return Path.Combine(xdgData, AppDomain.CurrentDomain.FriendlyName);
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				".local", "share", AppDomain.CurrentDomain.FriendlyName);
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
