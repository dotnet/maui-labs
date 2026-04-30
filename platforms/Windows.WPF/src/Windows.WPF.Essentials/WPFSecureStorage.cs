using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Platforms.Windows.WPF.Essentials
{
	public class WPFSecureStorage : ISecureStorage
	{
		static readonly string _storageDir = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			AppDomain.CurrentDomain.FriendlyName, "secure_storage");

		static string GetFilePath(string key)
		{
			// Use SHA256 hash for filenames — fixed length, irreversible, filesystem-safe
			var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
			var fileName = Convert.ToHexString(hash).ToLowerInvariant() + ".dat";
			return Path.Combine(_storageDir, fileName);
		}

		public Task<string?> GetAsync(string key)
		{
			var path = GetFilePath(key);
			if (!File.Exists(path)) return Task.FromResult<string?>(null);

			var encrypted = File.ReadAllBytes(path);
			var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
			return Task.FromResult<string?>(Encoding.UTF8.GetString(decrypted));
		}

		public Task SetAsync(string key, string? value)
		{
			Directory.CreateDirectory(_storageDir);
			var path = GetFilePath(key);

			if (value == null)
			{
				if (File.Exists(path)) File.Delete(path);
			}
			else
			{
				var encrypted = ProtectedData.Protect(
					Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
				File.WriteAllBytes(path, encrypted);
			}
			return Task.CompletedTask;
		}

		public bool Remove(string key)
		{
			var path = GetFilePath(key);
			if (!File.Exists(path)) return false;
			File.Delete(path);
			return true;
		}

		public void RemoveAll()
		{
			if (Directory.Exists(_storageDir))
				Directory.Delete(_storageDir, true);
		}
	}
}
