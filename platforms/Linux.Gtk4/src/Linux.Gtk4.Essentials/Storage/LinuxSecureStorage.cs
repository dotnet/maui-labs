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
public class LinuxSecureStorage : ISecureStorage
{
	private const string MauiApplicationIdMetadataKey = "MauiApplicationId";
	private const string LegacyLibSecretSchemaName = "org.maui.gtk.securestorage";
	private const string ScopedLibSecretSchemaName = "org.maui.gtk.securestorage.v2";
	private const string KeyAttributeName = "key";
	private const string AppIdAttributeName = "application";

	private readonly object _lock = new();
	private readonly string _applicationId = ResolveApplicationId();
	private bool _useLibSecret;
	private bool _libSecretProbed;
	private LegacyMigrationState? _legacyMigrationState;
	private IntPtr _schemaNamePtr;
	private IntPtr _legacySchemaNamePtr;
	private IntPtr _attrNamePtr;
	private IntPtr _appAttrNamePtr;
	private LibSecretInterop.SecretSchema _schema;
	private LibSecretInterop.SecretSchema _legacySchema;

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
			{
				IgnoreLegacyFallbackForKey(key);
				return LibSecretClearScoped(key);
			}

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
				LibSecretClearAll();
				IgnoreLegacyFallbackForAllKeys();
			}

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

			// Use a versioned schema for new entries so legacy key-only migration
			// never matches the new per-app records by accident.
			_schemaNamePtr = Marshal.StringToCoTaskMemUTF8(ScopedLibSecretSchemaName);
			_legacySchemaNamePtr = Marshal.StringToCoTaskMemUTF8(LegacyLibSecretSchemaName);
			_attrNamePtr = Marshal.StringToCoTaskMemUTF8(KeyAttributeName);
			_appAttrNamePtr = Marshal.StringToCoTaskMemUTF8(AppIdAttributeName);

			_legacySchema = new LibSecretInterop.SecretSchema
			{
				Name = _legacySchemaNamePtr,
				Flags = LibSecretInterop.SECRET_SCHEMA_NONE,
				Attr0 = new LibSecretInterop.SecretSchemaAttribute
				{
					Name = _attrNamePtr,
					Type = LibSecretInterop.SECRET_SCHEMA_ATTRIBUTE_STRING,
				},
				// Sentinel — all remaining attrs are zeroed (IntPtr.Zero, 0) by default
			};

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

		if (ShouldIgnoreLegacyFallback(key))
			return null;

		var legacyValue = LibSecretGetLegacy(key);
		if (legacyValue == null)
			return null;

		LibSecretSetScoped(key, legacyValue);
		return legacyValue;
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

	private string? LibSecretGetLegacy(string key)
	{
		var ht = CreateLegacyAttributesTable(key, out var keyPtr, out var valuePtr);
		try
		{
			return LibSecretLookup(ref _legacySchema, ht);
		}
		finally
		{
			LibSecretInterop.FreeAttributesTable(ht, keyPtr, valuePtr);
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

		// Legacy entries were written into a single shared schema with no app scope.
		// Clear that legacy namespace as part of RemoveAll() so pre-upgrade secrets
		// cannot survive or be resurrected after the user explicitly wipes storage.
		var legacyHt = LibSecretInterop.CreateEmptyAttributesTable();
		try
		{
			LibSecretInterop.SecretPasswordClearVSync(
				ref _legacySchema, legacyHt, IntPtr.Zero, out var err);
			LibSecretInterop.ConsumeError(err);
		}
		finally
		{
			LibSecretInterop.FreeAttributesTable(legacyHt);
		}
	}

	private IntPtr CreateScopedAttributesTable(string key, out IntPtr[] ptrs) =>
		LibSecretInterop.CreateAttributesTable(
			out ptrs,
			(AppIdAttributeName, _applicationId),
			(KeyAttributeName, key));

	private static IntPtr CreateLegacyAttributesTable(string key, out IntPtr keyPtr, out IntPtr valuePtr) =>
		LibSecretInterop.CreateAttributesTable(KeyAttributeName, key, out keyPtr, out valuePtr);

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

	private bool ShouldIgnoreLegacyFallback(string key)
	{
		var state = GetLegacyMigrationState();
		if (state.IgnoreAll)
			return true;

		return state.IgnoredKeyHashes.Contains(HashKey(key), StringComparer.Ordinal);
	}

	private void IgnoreLegacyFallbackForKey(string key)
	{
		var state = GetLegacyMigrationState();
		if (state.IgnoreAll)
			return;

		var keyHash = HashKey(key);
		if (state.IgnoredKeyHashes.Contains(keyHash, StringComparer.Ordinal))
			return;

		state.IgnoredKeyHashes.Add(keyHash);
		SaveLegacyMigrationState(state);
	}

	private void IgnoreLegacyFallbackForAllKeys()
	{
		var state = GetLegacyMigrationState();
		if (state.IgnoreAll)
			return;

		state.IgnoreAll = true;
		state.IgnoredKeyHashes.Clear();
		SaveLegacyMigrationState(state);
	}

	private LegacyMigrationState GetLegacyMigrationState()
	{
		if (_legacyMigrationState != null)
			return _legacyMigrationState;

		if (!File.Exists(LegacyMigrationStatePath))
			return _legacyMigrationState = new LegacyMigrationState();

		try
		{
			var json = File.ReadAllText(LegacyMigrationStatePath);
			_legacyMigrationState = JsonSerializer.Deserialize<LegacyMigrationState>(json) ?? new LegacyMigrationState();
		}
		catch
		{
			_legacyMigrationState = new LegacyMigrationState();
		}

		return _legacyMigrationState;
	}

	private void SaveLegacyMigrationState(LegacyMigrationState state)
	{
		_legacyMigrationState = state;

		if (!state.IgnoreAll && state.IgnoredKeyHashes.Count == 0)
		{
			if (File.Exists(LegacyMigrationStatePath))
				File.Delete(LegacyMigrationStatePath);
			return;
		}

		var json = JsonSerializer.Serialize(state);
		File.WriteAllText(LegacyMigrationStatePath, json);
		try { File.SetUnixFileMode(LegacyMigrationStatePath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
		catch { }
	}

	private static string HashKey(string key)
	{
		var keyBytes = Encoding.UTF8.GetBytes(key);
		return Convert.ToHexString(SHA256.HashData(keyBytes));
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
	private string LegacyMigrationStatePath => Path.Combine(StoragePath, "secure_store_legacy_migration.json");

	private sealed class LegacyMigrationState
	{
		public bool IgnoreAll { get; set; }
		public List<string> IgnoredKeyHashes { get; set; } = new();
	}

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
