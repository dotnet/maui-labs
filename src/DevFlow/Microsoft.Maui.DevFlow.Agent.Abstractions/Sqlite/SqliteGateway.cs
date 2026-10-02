using SQLitePCL;

namespace Microsoft.Maui.DevFlow.Agent.Core.Sqlite;

/// <summary>
/// Opens the app's own SQLite files, in the app's own process.
/// </summary>
/// <remarks>
/// <para>
/// In process on purpose. The alternative - copy the file out, edit it, copy it back - reads a
/// snapshot, misses anything still sitting in the write-ahead log, and overwrites whatever the
/// running app wrote in between. Opening the real file is the only way the browser shows what the
/// app is actually storing, and it is also the only way asking a 4GB database for twenty rows costs
/// twenty rows.
/// </para>
/// <para>
/// Every connection carries an authorizer that fences it to the file it was opened on. See
/// <see cref="Restrict"/>.
/// </para>
/// </remarks>
internal static class SqliteGateway
{
    /// <summary>
    /// How long one request gets on a connection before whatever it is running is interrupted.
    /// </summary>
    /// <remarks>
    /// This may be a phone. A cartesian join typed by accident is not a hung request to be waited
    /// out - it is a warm device with a flat battery, holding a write lock on a file the file
    /// manager is also showing.
    /// </remarks>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private static readonly object ProviderGate = new();
    private static volatile bool _providerReady;

    /// <summary>
    /// Makes sure a SQLite provider is registered, without ever replacing one that is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The agent references SQLitePCLRaw's core and nothing else: no bundle, so it brings no provider
    /// of its own to compete with the app's, and not Microsoft.Data.Sqlite, whose connection registers
    /// the batteries bundle from a static constructor whether or not the app already chose one. The
    /// app's provider - e_sqlite3, sqlcipher, the system library - is the one every database here is
    /// opened with.
    /// </para>
    /// <para>
    /// When nothing is registered yet there is nothing to replace, so the app's own batteries bundle is
    /// initialised if it ships one - the same thing the app's first connection would do. With no
    /// bundle at all the app has no SQLite to browse, and that is the error.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The app has no SQLite provider.</exception>
    public static void EnsureProvider()
    {
        if (_providerReady)
            return;

        lock (ProviderGate)
        {
            if (_providerReady)
                return;

            if (!HasProvider())
            {
                RegisterAppBundle();

                // Not remembered: an app that registers its provider later should not be told no forever.
                if (!HasProvider())
                    throw new InvalidOperationException("This app has no SQLite provider registered, so there is no SQLite to open its databases with.");
            }

            _providerReady = true;
        }
    }

    private static bool HasProvider()
    {
        try
        {
            // Any call into the provider throws when none is registered.
            _ = raw.sqlite3_libversion_number();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void RegisterAppBundle()
    {
        try
        {
            Type.GetType("SQLitePCL.Batteries_V2, SQLitePCLRaw.batteries_v2")
                ?.GetMethod("Init", Type.EmptyTypes)
                ?.Invoke(null, null);
        }
        catch (Exception)
        {
            // A bundle that will not load leaves no provider, which the caller reports.
        }
    }

    /// <summary>
    /// Opens a database, with the authorizer installed before anything can be prepared on it.
    /// </summary>
    /// <param name="timeout">The deadline for everything run on the connection. <see cref="DefaultTimeout"/> when null.</param>
    /// <exception cref="InvalidOperationException">
    /// The file is not a SQLite database - a perfectly ordinary thing for a file called <c>.db</c>
    /// to turn out not to be - or SQLite refused to open it.
    /// </exception>
    public static SqliteDatabase Open(string fullPath, TimeSpan? timeout = null)
    {
        EnsureProvider();

        // ReadWrite and not create: a typo in a path should be an error, not a new empty database
        // appearing in the directory the file manager is showing. And one connection per request,
        // closed with the request, so no pooled handle keeps a lock the file manager then trips over.
        var database = SqliteDatabase.Open(fullPath, raw.SQLITE_OPEN_READWRITE, timeout ?? DefaultTimeout);

        try
        {
            Restrict(database.Handle);

            // Open does not touch the file. SQLite reads page one when the first statement is
            // prepared, so a text file with a .db on it opens perfectly happily and only falls over
            // later - inside a schema read or somebody's query, where the failure arrives as
            // SQLite's own wording instead of a sentence about the file being the wrong sort of
            // thing. This is the cheapest statement that forces the header to be read.
            database.Execute("PRAGMA schema_version");
        }
        catch (SqliteError ex)
        {
            database.Dispose();

            // SQLITE_NOTADB is what a .db that is really a thumbnail cache or a renamed zip comes
            // back as, and it is the single most likely failure here - the extension is a guess.
            throw ex.Code == raw.SQLITE_NOTADB
                ? new InvalidOperationException($"'{Path.GetFileName(fullPath)}' is not a SQLite database.")
                : new InvalidOperationException(ex.Message, ex);
        }
        catch
        {
            database.Dispose();
            throw;
        }

        return database;
    }

    /// <summary>
    /// Fences the connection to the one file it was opened on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SQLite's own authorizer hook, called once per operation while a statement is being prepared -
    /// so a denied statement never runs at all, and there is no parsing on our side to be fooled.
    /// String-matching the SQL for "attach" is the other way to try this, and it is the way that
    /// loses to a comment and a line break.
    /// </para>
    /// <para>
    /// Two things are refused and every write is allowed through: <c>ATTACH</c>, which would take a
    /// second file into the same connection and walk straight past the storage root the agent
    /// serves; and <c>load_extension</c>, which loads a shared library and runs its code inside the
    /// app - not a database operation at any setting.
    /// </para>
    /// </remarks>
    private static void Restrict(sqlite3 handle)
    {
        var result = raw.sqlite3_set_authorizer(
            handle,
            // For SQLITE_FUNCTION the first string is always null and the function's name is the
            // second - checking the first matches nothing, and leaves only the app's extension-loading
            // setting between a query and a shared library.
            (_, action, _, function, _, _) => action switch
            {
                raw.SQLITE_ATTACH => raw.SQLITE_DENY,
                raw.SQLITE_FUNCTION when IsExtensionLoader(function) => raw.SQLITE_DENY,
                _ => raw.SQLITE_OK
            },
            null
        );

        if (result != raw.SQLITE_OK)
        {
            // An authorizer that did not install is a connection with no fence around it, and
            // carrying on would mean serving queries under a guarantee that is no longer true.
            throw new InvalidOperationException("The database could not be opened safely.");
        }
    }

    private static bool IsExtensionLoader(string? function)
        => string.Equals(function, "load_extension", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Writes the header of a new, empty database - a real one, that anything it is later handed to
    /// will open as a database.
    /// </summary>
    public static void Create(string fullPath)
    {
        EnsureProvider();

        using var database = SqliteDatabase.Open(
            fullPath, raw.SQLITE_OPEN_READWRITE | raw.SQLITE_OPEN_CREATE, DefaultTimeout);

        // Opening alone leaves a zero-byte file: SQLite writes page one when it first has something
        // to put in it. A pragma that changes a setting is the cheapest thing that counts as that.
        database.Execute("PRAGMA user_version = 0");
    }
}
