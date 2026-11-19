using System.IO;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Platforms.Windows.WPF.Essentials
{
	public class WPFFileSystem : IFileSystem
	{
		public string AppDataDirectory
		{
			get
			{
				var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
					AppDomain.CurrentDomain.FriendlyName);
				Directory.CreateDirectory(path);
				return path;
			}
		}

		public string CacheDirectory
		{
			get
			{
				var path = Path.Combine(Path.GetTempPath(), AppDomain.CurrentDomain.FriendlyName);
				Directory.CreateDirectory(path);
				return path;
			}
		}

		public Task<System.IO.Stream> OpenAppPackageFileAsync(string filename)
		{
			var basePath = AppContext.BaseDirectory;
			var filePath = Path.Combine(basePath, filename);

			if (!File.Exists(filePath))
				throw new FileNotFoundException($"App package file not found: {filename}", filePath);

			return Task.FromResult<System.IO.Stream>(File.OpenRead(filePath));
		}

		public Task<bool> AppPackageFileExistsAsync(string filename)
		{
			var filePath = Path.Combine(AppContext.BaseDirectory, filename);
			return Task.FromResult(File.Exists(filePath));
		}
	}
}
