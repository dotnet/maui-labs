using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Storage;

namespace Platform.Maui.Linux.Gtk4.Essentials.Storage;

/// <summary>
/// Secure storage that uses libsecret (freedesktop.org Secret Service / GNOME Keyring)
/// when available, falling back to AES-256 encrypted file storage otherwise.
/// </summary>
public class LinuxSecureStorage : ISecureStorage
{
	private readonly object _lock = new();
	private bool _useLibSecret;
	private bool _libSecretProbed;
	private IntPtr _schemaNamePtr;
	private IntPtr _attrNamePtr;
	private LibSecretInterop.SecretSchema _schema;

	/// <summary>
	/// Returns the active storage backend: "libsecret" (GNOME Keyring / Secret Service)
	/// or "encrypted-file" (AES-256 file-based fallback).
	/// Accessing this property triggers the libsecret availability probe if not yet done.
	/// </summary>
	public string Backend
	{
		get
		{
			lock (_lock)
			{
				return TryEnsureLibSecret() ? "libsecret" : "encrypted-file";
			}
		}
	}

	// ── ISecureStorage ──────────────────────────────────────────────────

	public Task<string?> GetAsync(string key)
	{
		lock (_lock)
		{
			if (TryEnsureLibSecret())
			{
				var result = LibSecretGet(key);
				if (result != null)
					return Task.FromResult<string?>(result);
				// null could mean "not found" — that's fine, return null
				return Task.FromResult<string?>(null);
			}

			var store = LoadStore();
			return Task.FromResult(store.TryGetValue(key, out var value) ? value : null);
		}
	}

	public Task SetAsync(string key, string value)
	{
		lock (_lock)
		{
			if (TryEnsureLibSecret())
			{
				LibSecretSet(key, value);
				return Task.CompletedTask;
			}

			var store = LoadStore();
			store[key] = value;
			SaveStore(store);
			return Task.CompletedTask;
		}
	}

	public bool Remove(string key)
	{
		lock (_lock)
		{
			if (TryEnsureLibSecret())
				return LibSecretClear(key);

			var store = LoadStore();
			var removed = store.Remove(key);
			if (removed)
				SaveStore(store);
			return removed;
		}
	}

	public void RemoveAll()
	{
		lock (_lock)
		{
			if (TryEnsureLibSecret())
			{
				// libsecret has no "remove all" — fall through to file cleanup,
				// but also note that individual items remain in the keyring.
				// Users who stored via libsecret should remove keys individually.
			}

			if (File.Exists(DataFilePath))
				File.Delete(DataFilePath);
			if (File.Exists(KeyFilePath))
				File.Delete(KeyFilePath);
		}
	}

	// ── libsecret integration ───────────────────────────────────────────

	private bool TryEnsureLibSecret()
	{
		if (_libSecretProbed)
			return _useLibSecret;

		_libSecretProbed = true;

		try
		{
			if (!LibSecretInterop.IsAvailable())
				return false;

			// Allocate schema name and attribute name strings that live for the process lifetime
			_schemaNamePtr = Marshal.StringToCoTaskMemUTF8("org.maui.gtk.securestorage");
			_attrNamePtr = Marshal.StringToCoTaskMemUTF8("key");

			_schema = new LibSecretInterop.SecretSchema
			{
				Name = _schemaNamePtr,
				Flags = LibSecretInterop.SECRET_SCHEMA_NONE,
				Attr0 = new LibSecretInterop.SecretSchemaAttribute
				{
					Name = _attrNamePtr,
					Type = LibSecretInterop.SECRET_SCHEMA_ATTRIBUTE_STRING,
				},
				// Sentinel — all remaining attrs are zeroed (IntPtr.Zero, 0) by default
			};

			// Probe with a lookup to verify the Secret Service daemon is reachable
			var ht = LibSecretInterop.CreateAttributesTable("key", "__probe__", out var kPtr, out var vPtr);
			try
			{
				var result = LibSecretInterop.SecretPasswordLookupVSync(
					ref _schema, ht, IntPtr.Zero, out var err);

				var errMsg = LibSecretInterop.ConsumeError(err);
				if (errMsg != null)
					return false; // daemon not running or similar

				if (result != IntPtr.Zero)
					LibSecretInterop.SecretPasswordFree(result);
			}
			finally
			{
				LibSecretInterop.FreeAttributesTable(ht, kPtr, vPtr);
			}

			_useLibSecret = true;
			return true;
		}
		catch
		{
			_useLibSecret = false;
			return false;
		}
	}

	private string? LibSecretGet(string key)
	{
		var ht = LibSecretInterop.CreateAttributesTable("key", key, out var kPtr, out var vPtr);
		try
		{
			var resultPtr = LibSecretInterop.SecretPasswordLookupVSync(
				ref _schema, ht, IntPtr.Zero, out var err);

			var errMsg = LibSecretInterop.ConsumeError(err);
			if (errMsg != null)
				return null;

			if (resultPtr == IntPtr.Zero)
				return null;

			var result = Marshal.PtrToStringUTF8(resultPtr);
			LibSecretInterop.SecretPasswordFree(resultPtr);
			return result;
		}
		finally
		{
			LibSecretInterop.FreeAttributesTable(ht, kPtr, vPtr);
		}
	}

	private void LibSecretSet(string key, string value)
	{
		var ht = LibSecretInterop.CreateAttributesTable("key", key, out var kPtr, out var vPtr);
		try
		{
			LibSecretInterop.SecretPasswordStoreVSync(
				ref _schema,
				ht,          // attributes
				IntPtr.Zero, // default collection
				key,         // label
				value,       // password
				IntPtr.Zero, // cancellable
				out var err);

			var errMsg = LibSecretInterop.ConsumeError(err);
			if (errMsg != null)
				throw new InvalidOperationException($"libsecret store failed: {errMsg}");
		}
		finally
		{
			LibSecretInterop.FreeAttributesTable(ht, kPtr, vPtr);
		}
	}

	private bool LibSecretClear(string key)
	{
		var ht = LibSecretInterop.CreateAttributesTable("key", key, out var kPtr, out var vPtr);
		try
		{
			var removed = LibSecretInterop.SecretPasswordClearVSync(
				ref _schema, ht, IntPtr.Zero, out var err);

			LibSecretInterop.ConsumeError(err);
			return removed;
		}
		finally
		{
			LibSecretInterop.FreeAttributesTable(ht, kPtr, vPtr);
		}
	}

	// ── Encrypted-file fallback (original implementation) ───────────────

	private string StoragePath
	{
		get
		{
			var dataDir = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
			if (string.IsNullOrEmpty(dataDir))
				dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
			var appDir = Path.Combine(dataDir, AppDomain.CurrentDomain.FriendlyName, ".secure");
			Directory.CreateDirectory(appDir);
			try { File.SetUnixFileMode(appDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
			catch { }
			return appDir;
		}
	}

	private string DataFilePath => Path.Combine(StoragePath, "secure_store.dat");
	private string KeyFilePath => Path.Combine(StoragePath, "secure_store.key");

	private byte[] GetOrCreateKey()
	{
		if (File.Exists(KeyFilePath))
		{
			var keyData = File.ReadAllBytes(KeyFilePath);
			if (keyData.Length == 32)
				return keyData;
		}

		var key = RandomNumberGenerator.GetBytes(32);
		File.WriteAllBytes(KeyFilePath, key);
		try { File.SetUnixFileMode(KeyFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
		catch { }
		return key;
	}

	private Dictionary<string, string> LoadStore()
	{
		if (!File.Exists(DataFilePath))
			return new();

		try
		{
			var encrypted = File.ReadAllBytes(DataFilePath);
			var key = GetOrCreateKey();
			var json = Decrypt(encrypted, key);
			return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
		}
		catch
		{
			return new();
		}
	}

	private void SaveStore(Dictionary<string, string> store)
	{
		var json = JsonSerializer.Serialize(store);
		var key = GetOrCreateKey();
		var encrypted = Encrypt(json, key);
		File.WriteAllBytes(DataFilePath, encrypted);
		try { File.SetUnixFileMode(DataFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
		catch { }
	}

	private static byte[] Encrypt(string plainText, byte[] key)
	{
		using var aes = Aes.Create();
		aes.Key = key;
		aes.GenerateIV();
		using var encryptor = aes.CreateEncryptor();
		var plainBytes = Encoding.UTF8.GetBytes(plainText);
		var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
		var result = new byte[aes.IV.Length + cipherBytes.Length];
		aes.IV.CopyTo(result, 0);
		cipherBytes.CopyTo(result, aes.IV.Length);
		return result;
	}

	private static string Decrypt(byte[] cipherWithIv, byte[] key)
	{
		using var aes = Aes.Create();
		aes.Key = key;
		var iv = new byte[aes.BlockSize / 8];
		Array.Copy(cipherWithIv, 0, iv, 0, iv.Length);
		aes.IV = iv;
		using var decryptor = aes.CreateDecryptor();
		var cipherBytes = new byte[cipherWithIv.Length - iv.Length];
		Array.Copy(cipherWithIv, iv.Length, cipherBytes, 0, cipherBytes.Length);
		var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
		return Encoding.UTF8.GetString(plainBytes);
	}
}
