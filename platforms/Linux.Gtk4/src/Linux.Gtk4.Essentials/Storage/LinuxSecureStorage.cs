using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Storage;

/// <summary>
/// Secure storage that uses libsecret (freedesktop.org Secret Service / GNOME Keyring)
/// when available, falling back to AES-256 encrypted file storage otherwise.
/// </summary>
public class LinuxSecureStorage : ISecureStorage, IDisposable
{
	private const string MauiApplicationIdMetadataKey = "MauiApplicationId";
	private const string LibSecretSchemaName = "org.maui.gtk.securestorage.v2";
	private const string KeyAttributeName = "key";
	private const string AppIdAttributeName = "application";

	private readonly object _lock = new();
	private readonly string _applicationId = ResolveApplicationId();
	private bool _useLibSecret;
	private bool _libSecretProbed;
	private IntPtr _schemaNamePtr;
	private IntPtr _attrNamePtr;
	private IntPtr _appAttrNamePtr;
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
		return Task.Run(() =>
		{
			lock (_lock)
			{
				if (TryEnsureLibSecret())
				{
					var result = LibSecretGet(key);
					if (result != null)
						return result;
					return (string?)null;
				}

				var store = LoadStore();
				return store.TryGetValue(key, out var value) ? value : null;
			}
		});
	}

	public Task SetAsync(string key, string value)
	{
		return Task.Run(() =>
		{
			lock (_lock)
			{
				if (TryEnsureLibSecret())
				{
					LibSecretSet(key, value);
					return;
				}

				var store = LoadStore();
				store[key] = value;
				SaveStore(store);
			}
		});
	}

	public bool Remove(string key)
	{
		lock (_lock)
		{
			if (TryEnsureLibSecret())
				return LibSecretClearScoped(key);

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
				LibSecretClearAll();

			// Always clean up file-based fallback artifacts
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

			// New Linux GTK4 secure storage is intentionally isolated in a versioned,
			// app-scoped schema. Preview-era secrets are not migrated forward.
			_schemaNamePtr = Marshal.StringToCoTaskMemUTF8(LibSecretSchemaName);
			_attrNamePtr = Marshal.StringToCoTaskMemUTF8(KeyAttributeName);
			_appAttrNamePtr = Marshal.StringToCoTaskMemUTF8(AppIdAttributeName);

			_schema = new LibSecretInterop.SecretSchema
			{
				Name = _schemaNamePtr,
				Flags = LibSecretInterop.SECRET_SCHEMA_NONE,
				Attr0 = new LibSecretInterop.SecretSchemaAttribute
				{
					Name = _appAttrNamePtr,
					Type = LibSecretInterop.SECRET_SCHEMA_ATTRIBUTE_STRING,
				},
				Attr1 = new LibSecretInterop.SecretSchemaAttribute
				{
					Name = _attrNamePtr,
					Type = LibSecretInterop.SECRET_SCHEMA_ATTRIBUTE_STRING,
				},
				// Sentinel — all remaining attrs are zeroed (IntPtr.Zero, 0) by default
			};

			// Probe with a lookup to verify the Secret Service daemon is reachable
			var ht = CreateScopedAttributesTable("__probe__", out var ptrs);
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
				LibSecretInterop.FreeAttributesTable(ht, ptrs);
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
		var ht = CreateScopedAttributesTable(key, out var ptrs);
		try
		{
			var result = LibSecretLookup(ref _schema, ht);
			if (result != null)
				return result;
		}
		finally
		{
			LibSecretInterop.FreeAttributesTable(ht, ptrs);
		}

		return null;
	}

	private void LibSecretSet(string key, string value)
		=> LibSecretSetScoped(key, value);

	private void LibSecretSetScoped(string key, string value)
	{
		var ht = CreateScopedAttributesTable(key, out var ptrs);
		try
		{
			LibSecretInterop.SecretPasswordStoreVSync(
				ref _schema,
				ht,          // attributes
				IntPtr.Zero, // default collection
				$"{_applicationId}:{key}", // label
				value,       // password
				IntPtr.Zero, // cancellable
				out var err);

			var errMsg = LibSecretInterop.ConsumeError(err);
			if (errMsg != null)
				throw new InvalidOperationException($"libsecret store failed: {errMsg}");
		}
		finally
		{
			LibSecretInterop.FreeAttributesTable(ht, ptrs);
		}
	}

	private bool LibSecretClearScoped(string key)
	{
		var ht = CreateScopedAttributesTable(key, out var ptrs);
		try
		{
			var removed = LibSecretInterop.SecretPasswordClearVSync(
				ref _schema, ht, IntPtr.Zero, out var err);

			LibSecretInterop.ConsumeError(err);
			return removed;
		}
		finally
		{
			LibSecretInterop.FreeAttributesTable(ht, ptrs);
		}
	}

	private void LibSecretClearAll()
	{
		var ht = LibSecretInterop.CreateAttributesTable(
			out var ptrs,
			(AppIdAttributeName, _applicationId));
		try
		{
			LibSecretInterop.SecretPasswordClearVSync(
				ref _schema, ht, IntPtr.Zero, out var err);
			// Ignore errors on bulk clear — best-effort removal
			LibSecretInterop.ConsumeError(err);
		}
		finally
		{
			LibSecretInterop.FreeAttributesTable(ht, ptrs);
		}
	}

	private IntPtr CreateScopedAttributesTable(string key, out IntPtr[] ptrs) =>
		LibSecretInterop.CreateAttributesTable(
			out ptrs,
			(AppIdAttributeName, _applicationId),
			(KeyAttributeName, key));

	private static string? LibSecretLookup(ref LibSecretInterop.SecretSchema schema, IntPtr attributes)
	{
		var resultPtr = LibSecretInterop.SecretPasswordLookupVSync(
			ref schema, attributes, IntPtr.Zero, out var err);

		var errMsg = LibSecretInterop.ConsumeError(err);
		if (errMsg != null || resultPtr == IntPtr.Zero)
			return null;

		var result = Marshal.PtrToStringUTF8(resultPtr);
		LibSecretInterop.SecretPasswordFree(resultPtr);
		return result;
	}

	private static string ResolveApplicationId()
	{
		if (TryGetApplicationId(Assembly.GetEntryAssembly(), out var applicationId))
			return applicationId;

		var entryAssemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
		if (!string.IsNullOrWhiteSpace(entryAssemblyName))
			return entryAssemblyName;

		if (!string.IsNullOrWhiteSpace(AppDomain.CurrentDomain.FriendlyName))
			return AppDomain.CurrentDomain.FriendlyName;

		return "unknown";
	}

	private static bool TryGetApplicationId(Assembly? assembly, out string applicationId)
	{
		if (assembly != null)
		{
			foreach (var metadata in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
			{
				if (!string.Equals(metadata.Key, MauiApplicationIdMetadataKey, StringComparison.Ordinal))
					continue;

				if (string.IsNullOrWhiteSpace(metadata.Value))
					continue;

				applicationId = metadata.Value.Trim();
				return true;
			}
		}

		applicationId = string.Empty;
		return false;
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
		var plainBytes = Encoding.UTF8.GetBytes(plainText);
		var nonce = RandomNumberGenerator.GetBytes(12); // AES-GCM standard nonce size
		var tag = new byte[16]; // 128-bit authentication tag
		var cipherBytes = new byte[plainBytes.Length];

		using var aesGcm = new AesGcm(key, tag.Length);
		aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);

		// Format: nonce (12) || tag (16) || ciphertext
		var result = new byte[nonce.Length + tag.Length + cipherBytes.Length];
		nonce.CopyTo(result, 0);
		tag.CopyTo(result, nonce.Length);
		cipherBytes.CopyTo(result, nonce.Length + tag.Length);
		return result;
	}

	private static string Decrypt(byte[] data, byte[] key)
	{
		const int nonceSize = 12;
		const int tagSize = 16;
		if (data.Length < nonceSize + tagSize)
			throw new InvalidOperationException("Encrypted data is too short.");

		var nonce = data.AsSpan(0, nonceSize);
		var tag = data.AsSpan(nonceSize, tagSize);
		var cipherBytes = data.AsSpan(nonceSize + tagSize);
		var plainBytes = new byte[cipherBytes.Length];

		using var aesGcm = new AesGcm(key, tagSize);
		aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);
		return Encoding.UTF8.GetString(plainBytes);
	}

	public void Dispose()
	{
		if (_schemaNamePtr != IntPtr.Zero) { Marshal.FreeCoTaskMem(_schemaNamePtr); _schemaNamePtr = IntPtr.Zero; }
		if (_attrNamePtr != IntPtr.Zero) { Marshal.FreeCoTaskMem(_attrNamePtr); _attrNamePtr = IntPtr.Zero; }
		if (_appAttrNamePtr != IntPtr.Zero) { Marshal.FreeCoTaskMem(_appAttrNamePtr); _appAttrNamePtr = IntPtr.Zero; }
	}
}
