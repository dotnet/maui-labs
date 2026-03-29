using System.Runtime.InteropServices;

namespace Platform.Maui.Linux.Gtk4.Essentials.Storage;

/// <summary>
/// P/Invoke bindings for libsecret-1 non-variadic ("v") password API
/// and the minimal GLib helpers needed to build a GHashTable of string→string.
/// </summary>
internal static class LibSecretInterop
{
	private const string LibSecret = "libsecret-1.so.0";
	private const string LibGLib = "libglib-2.0.so.0";

	// ── SecretSchema structs ────────────────────────────────────────────

	[StructLayout(LayoutKind.Sequential)]
	public struct SecretSchemaAttribute
	{
		public IntPtr Name; // const gchar*
		public int Type;    // SecretSchemaAttributeType enum
	}

	// SecretSchema contains a fixed-size array of 32 attributes.
	[StructLayout(LayoutKind.Sequential)]
	public struct SecretSchema
	{
		public IntPtr Name;  // const gchar*
		public int Flags;    // SecretSchemaFlags

		// 32 attributes (name + type pairs), last must be { NULL, 0 }
		public SecretSchemaAttribute Attr0;
		public SecretSchemaAttribute Attr1;
		public SecretSchemaAttribute Attr2;
		public SecretSchemaAttribute Attr3;
		public SecretSchemaAttribute Attr4;
		public SecretSchemaAttribute Attr5;
		public SecretSchemaAttribute Attr6;
		public SecretSchemaAttribute Attr7;
		public SecretSchemaAttribute Attr8;
		public SecretSchemaAttribute Attr9;
		public SecretSchemaAttribute Attr10;
		public SecretSchemaAttribute Attr11;
		public SecretSchemaAttribute Attr12;
		public SecretSchemaAttribute Attr13;
		public SecretSchemaAttribute Attr14;
		public SecretSchemaAttribute Attr15;
		public SecretSchemaAttribute Attr16;
		public SecretSchemaAttribute Attr17;
		public SecretSchemaAttribute Attr18;
		public SecretSchemaAttribute Attr19;
		public SecretSchemaAttribute Attr20;
		public SecretSchemaAttribute Attr21;
		public SecretSchemaAttribute Attr22;
		public SecretSchemaAttribute Attr23;
		public SecretSchemaAttribute Attr24;
		public SecretSchemaAttribute Attr25;
		public SecretSchemaAttribute Attr26;
		public SecretSchemaAttribute Attr27;
		public SecretSchemaAttribute Attr28;
		public SecretSchemaAttribute Attr29;
		public SecretSchemaAttribute Attr30;
		public SecretSchemaAttribute Attr31;
	}

	public const int SECRET_SCHEMA_NONE = 0;
	public const int SECRET_SCHEMA_ATTRIBUTE_STRING = 0;

	// ── libsecret password API (non-variadic "v" variants) ──────────────

	[DllImport(LibSecret, EntryPoint = "secret_password_storev_sync")]
	public static extern bool SecretPasswordStoreVSync(
		ref SecretSchema schema,
		IntPtr attributes,   // GHashTable*
		IntPtr collection,   // NULL for default collection
		[MarshalAs(UnmanagedType.LPUTF8Str)] string label,
		[MarshalAs(UnmanagedType.LPUTF8Str)] string password,
		IntPtr cancellable,  // NULL
		out IntPtr error);   // GError**

	[DllImport(LibSecret, EntryPoint = "secret_password_lookupv_sync")]
	public static extern IntPtr SecretPasswordLookupVSync(
		ref SecretSchema schema,
		IntPtr attributes,   // GHashTable*
		IntPtr cancellable,  // NULL
		out IntPtr error);   // GError**

	[DllImport(LibSecret, EntryPoint = "secret_password_clearv_sync")]
	public static extern bool SecretPasswordClearVSync(
		ref SecretSchema schema,
		IntPtr attributes,   // GHashTable*
		IntPtr cancellable,  // NULL
		out IntPtr error);   // GError**

	[DllImport(LibSecret, EntryPoint = "secret_password_free")]
	public static extern void SecretPasswordFree(IntPtr password);

	// ── GLib GHashTable helpers ─────────────────────────────────────────

	[DllImport(LibGLib, EntryPoint = "g_str_hash")]
	public static extern uint GStrHash(IntPtr key);

	[DllImport(LibGLib, EntryPoint = "g_str_equal")]
	public static extern bool GStrEqual(IntPtr a, IntPtr b);

	// g_hash_table_new(GHashFunc, GEqualFunc)
	[DllImport(LibGLib, EntryPoint = "g_hash_table_new")]
	public static extern IntPtr GHashTableNew(IntPtr hashFunc, IntPtr equalFunc);

	[DllImport(LibGLib, EntryPoint = "g_hash_table_insert")]
	public static extern bool GHashTableInsert(IntPtr hashTable, IntPtr key, IntPtr value);

	[DllImport(LibGLib, EntryPoint = "g_hash_table_destroy")]
	public static extern void GHashTableDestroy(IntPtr hashTable);

	// ── GLib error helpers ──────────────────────────────────────────────

	[DllImport(LibGLib, EntryPoint = "g_error_free")]
	public static extern void GErrorFree(IntPtr error);

	// GError struct: { GQuark domain; gint code; gchar *message; }
	[StructLayout(LayoutKind.Sequential)]
	public struct GError
	{
		public uint Domain;
		public int Code;
		public IntPtr Message;
	}

	// ── Helpers ─────────────────────────────────────────────────────────

	// Function pointers for g_str_hash and g_str_equal, resolved once.
	private static readonly IntPtr s_strHashPtr;
	private static readonly IntPtr s_strEqualPtr;

	static LibSecretInterop()
	{
		if (!NativeLibrary.TryLoad(LibGLib, out var glibHandle))
			return;

		NativeLibrary.TryGetExport(glibHandle, "g_str_hash", out s_strHashPtr);
		NativeLibrary.TryGetExport(glibHandle, "g_str_equal", out s_strEqualPtr);
	}

	/// <summary>
	/// Creates a GHashTable&lt;string,string&gt; with a single key→value entry.
	/// Caller must free with <see cref="GHashTableDestroy"/>.
	/// The returned pinned strings must be freed after the hash table is destroyed.
	/// </summary>
	public static IntPtr CreateAttributesTable(string key, string value, out IntPtr keyPtr, out IntPtr valuePtr)
	{
		var ht = GHashTableNew(s_strHashPtr, s_strEqualPtr);
		keyPtr = Marshal.StringToCoTaskMemUTF8(key);
		valuePtr = Marshal.StringToCoTaskMemUTF8(value);
		GHashTableInsert(ht, keyPtr, valuePtr);
		return ht;
	}

	public static void FreeAttributesTable(IntPtr ht, params IntPtr[] ptrs)
	{
		if (ht != IntPtr.Zero)
			GHashTableDestroy(ht);
		foreach (var p in ptrs)
			if (p != IntPtr.Zero)
				Marshal.FreeCoTaskMem(p);
	}

	/// <summary>
	/// Returns true if libsecret-1.so.0 can be loaded on this system.
	/// </summary>
	public static bool IsAvailable()
	{
		if (!NativeLibrary.TryLoad(LibSecret, out var handle))
			return false;
		NativeLibrary.Free(handle);
		return true;
	}

	/// <summary>
	/// Extracts and frees a GError, returning its message string, or null if no error.
	/// </summary>
	public static string? ConsumeError(IntPtr errorPtr)
	{
		if (errorPtr == IntPtr.Zero)
			return null;
		var err = Marshal.PtrToStructure<GError>(errorPtr);
		var msg = Marshal.PtrToStringUTF8(err.Message);
		GErrorFree(errorPtr);
		return msg;
	}
}
