using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CometBaristaNotes.Data;

internal sealed class SqliteNativeDatabase : IDisposable
{
#if IOS || MACCATALYST
    // iOS/Mac Catalyst: system SQLite linked via -lsqlite3 (see csproj LinkerArgument).
    // Symbols are in the main executable, so DllImport uses __Internal.
    private const string Library = "__Internal";
    internal static string NativeLibraryName => Library;
#elif ANDROID
    // Android: the Android runtime resolves DllImport via Java's System.loadLibrary (dlopen),
    // NOT NativeLibrary.SetDllImportResolver. The Library constant must be the actual native
    // library name. SQLitePCLRaw.lib.e_sqlite3.android bundles libe_sqlite3.so in the APK.
    private const string Library = "e_sqlite3";
    internal static string NativeLibraryName => Library;
#else
    // Desktop (Windows, macOS, Linux): redirect the sentinel name to the platform library
    // via NativeLibrary.SetDllImportResolver, which IS supported on CoreCLR.
    private const string Library = "baristanotes_sqlite";

    static SqliteNativeDatabase()
    {
        NativeLibrary.SetDllImportResolver(typeof(SqliteNativeDatabase).Assembly, ResolveImport);
    }

    /// <summary>Primary native library name for the current desktop platform.</summary>
    internal static string NativeLibraryName =>
        OperatingSystem.IsWindows() ? "winsqlite3" : "sqlite3";

    /// <summary>Deterministic helper for unit testing all platform paths.</summary>
    internal static string NativeLibraryNameFor(bool isWindows, bool isAndroid) =>
        isWindows ? "winsqlite3" :   // Windows: system winsqlite3.dll
        isAndroid ? "e_sqlite3"  :   // Android: bundled by SQLitePCLRaw.lib.e_sqlite3.android
                    "sqlite3";       // macOS/Linux: system libsqlite3.dylib/.so

    private static IntPtr ResolveImport(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        if (libraryName != Library)
            return IntPtr.Zero;

        var primary = NativeLibraryName;
        if (NativeLibrary.TryLoad(primary, assembly, searchPath, out var handle)
            || NativeLibrary.TryLoad(primary, out handle))
            return handle;

        // Fallback: e_sqlite3 (bundled by SQLitePCLRaw.lib.e_sqlite3 when referenced).
        if (NativeLibrary.TryLoad("e_sqlite3", assembly, searchPath, out handle)
            || NativeLibrary.TryLoad("e_sqlite3", out handle))
            return handle;

        throw new DllNotFoundException(
            $"Unable to load the platform SQLite library (tried '{primary}', 'e_sqlite3'). " +
            "Ensure libsqlite3 is installed on the system or add a SQLitePCLRaw.lib.e_sqlite3 package reference.");
    }
#endif
    private const int Ok = 0;
    private const int Row = 100;
    private const int Done = 101;
    private const int OpenReadWrite = 0x00000002;
    private const int OpenCreate = 0x00000004;
    private const int OpenFullMutex = 0x00010000;
    private static readonly IntPtr Transient = new(-1);
    private IntPtr _handle;

    public SqliteNativeDatabase(string path)
    {
        Check(sqlite3_open_v2(path, out _handle, OpenReadWrite | OpenCreate | OpenFullMutex, null));
        Execute("PRAGMA journal_mode=WAL;");
        Execute("PRAGMA busy_timeout=5000;");
    }

    public void Execute(string sql)
    {
        IntPtr error = IntPtr.Zero;
        var result = sqlite3_exec(_handle, sql, IntPtr.Zero, IntPtr.Zero, out error);
        if (result == Ok)
            return;

        var message = error == IntPtr.Zero ? ErrorMessage() : Marshal.PtrToStringUTF8(error);
        if (error != IntPtr.Zero)
            sqlite3_free(error);
        throw new InvalidOperationException($"SQLite error {result}: {message}");
    }

    public (int Version, string Payload)? ReadState()
    {
        using var statement = Prepare("SELECT SchemaVersion, Payload FROM AppState WHERE Id = 1;");
        var result = sqlite3_step(statement.Handle);
        if (result == Done)
            return null;
        if (result != Row)
            Check(result);

        var text = sqlite3_column_text(statement.Handle, 1);
        return (sqlite3_column_int(statement.Handle, 0), Marshal.PtrToStringUTF8(text) ?? string.Empty);
    }

    public void WriteState(int version, string payload)
    {
        using var statement = Prepare(
            "INSERT INTO AppState(Id, SchemaVersion, Payload, UpdatedAt) VALUES(1, ?1, ?2, ?3) " +
            "ON CONFLICT(Id) DO UPDATE SET SchemaVersion=excluded.SchemaVersion, Payload=excluded.Payload, UpdatedAt=excluded.UpdatedAt;");
        Check(sqlite3_bind_int(statement.Handle, 1, version));
        Check(sqlite3_bind_text(statement.Handle, 2, payload, -1, Transient));
        Check(sqlite3_bind_text(statement.Handle, 3, DateTime.UtcNow.ToString("O"), -1, Transient));
        Check(sqlite3_step(statement.Handle), Done);
    }

    public string? GetPreference(string key)
    {
        using var statement = Prepare("SELECT Value FROM Preferences WHERE Key = ?1;");
        Check(sqlite3_bind_text(statement.Handle, 1, key, -1, Transient));
        var result = sqlite3_step(statement.Handle);
        if (result == Done)
            return null;
        if (result != Row)
            Check(result);
        return Marshal.PtrToStringUTF8(sqlite3_column_text(statement.Handle, 0));
    }

    public void SetPreference(string key, string value)
    {
        using var statement = Prepare(
            "INSERT INTO Preferences(Key, Value) VALUES(?1, ?2) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value;");
        Check(sqlite3_bind_text(statement.Handle, 1, key, -1, Transient));
        Check(sqlite3_bind_text(statement.Handle, 2, value, -1, Transient));
        Check(sqlite3_step(statement.Handle), Done);
    }

    public void RemovePreference(string key)
    {
        using var statement = Prepare("DELETE FROM Preferences WHERE Key = ?1;");
        Check(sqlite3_bind_text(statement.Handle, 1, key, -1, Transient));
        Check(sqlite3_step(statement.Handle), Done);
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;
        sqlite3_close_v2(_handle);
        _handle = IntPtr.Zero;
    }

    private Statement Prepare(string sql)
    {
        Check(sqlite3_prepare_v2(_handle, sql, -1, out var statement, IntPtr.Zero));
        return new Statement(statement);
    }

    private void Check(int result, int expected = Ok)
    {
        if (result != expected)
            throw new InvalidOperationException($"SQLite error {result}: {ErrorMessage()}");
    }

    private string ErrorMessage() => Marshal.PtrToStringUTF8(sqlite3_errmsg(_handle)) ?? "Unknown error";

    private sealed class Statement(IntPtr handle) : IDisposable
    {
        public IntPtr Handle { get; } = handle;
        public void Dispose() => sqlite3_finalize(Handle);
    }

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filename,
        out IntPtr database,
        int flags,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? virtualFileSystem);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close_v2(IntPtr database);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_errmsg(IntPtr database);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr argument, out IntPtr error);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern void sqlite3_free(IntPtr pointer);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int bytes, out IntPtr statement, IntPtr tail);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(IntPtr statement);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(IntPtr statement);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_int(IntPtr statement, int index, int value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_text(IntPtr statement, int index, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int bytes, IntPtr destructor);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_int(IntPtr statement, int column);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
}
