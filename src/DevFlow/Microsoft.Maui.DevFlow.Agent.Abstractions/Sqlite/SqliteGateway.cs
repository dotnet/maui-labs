using Microsoft.Data.Sqlite;
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
    private static readonly object ProviderGate = new();
    private static bool _providerChecked;

    /// <summary>
    /// Registers a SQLite provider, but only if the host app has not already registered one.
    /// </summary>
    /// <remarks>
    /// This is why the package reference is <c>Microsoft.Data.Sqlite.Core</c> and not
    /// <c>Microsoft.Data.Sqlite</c>. The full package initialises a bundle from a static constructor,
    /// which would replace whatever provider the app under test set up - a debugging tool quietly
    /// changing how the app talks to its own database is not a trade worth making for convenience.
    /// </remarks>
    public static void EnsureProvider()
    {
        if (_providerChecked)
            return;

        lock (ProviderGate)
        {
            if (_providerChecked)
                return;

            try
            {
                // Any call into the provider throws when none is registered. A version string coming
                // back means the app already did this, and we leave its choice alone.
                _ = raw.sqlite3_libversion();
            }
            catch (Exception)
            {
                Batteries_V2.Init();
            }

            _providerChecked = true;
        }
    }

    /// <summary>
    /// Opens a database, with the authorizer installed before anything can be prepared on it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The file is not a SQLite database - a perfectly ordinary thing for a file called <c>.db</c>
    /// to turn out not to be - or SQLite refused to open it.
    /// </exception>
    public static SqliteConnection Open(string fullPath)
    {
        EnsureProvider();

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,

            // ReadWrite and not ReadWriteCreate: a typo in a path should be an error, not a new
            // empty database appearing in the directory the file manager is showing.
            Mode = SqliteOpenMode.ReadWrite,

            // Pooling keeps the handle - and on a write, the lock - alive after this connection is
            // disposed, which would leave the file manager unable to rename or delete a database
            // anyone had so much as looked at. One connection per request, closed with the request.
            Pooling = false
        }.ToString());

        try
        {
            connection.Open();
            Restrict(connection);

            // Open does not touch the file. SQLite reads page one when the first statement is
            // prepared, so a text file with a .db on it opens perfectly happily and only falls over
            // later - inside a schema read or somebody's query, where the failure arrives as
            // SQLite's own wording instead of a sentence about the file being the wrong sort of
            // thing. This is the cheapest statement that forces the header to be read.
            using (var probe = connection.CreateCommand())
            {
                probe.CommandText = "PRAGMA schema_version";
                probe.ExecuteScalar();
            }
        }
        catch (SqliteException ex)
        {
            connection.Dispose();

            // SQLITE_NOTADB is what a .db that is really a thumbnail cache or a renamed zip comes
            // back as, and it is the single most likely failure here - the extension is a guess.
            throw ex.SqliteErrorCode == 26
                ? new InvalidOperationException($"'{Path.GetFileName(fullPath)}' is not a SQLite database.")
                : new InvalidOperationException(ex.Message, ex);
        }
        catch
        {
            connection.Dispose();
            throw;
        }

        return connection;
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
    private static void Restrict(SqliteConnection connection)
    {
        var result = raw.sqlite3_set_authorizer(
            connection.Handle,
            (_, action, argument, _, _, _) => action switch
            {
                raw.SQLITE_ATTACH => raw.SQLITE_DENY,
                raw.SQLITE_FUNCTION when IsExtensionLoader(argument.utf8_to_string()) => raw.SQLITE_DENY,
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

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());

        connection.Open();

        // Opening alone leaves a zero-byte file: SQLite writes page one when it first has something
        // to put in it. A pragma that changes a setting is the cheapest thing that counts as that.
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version = 0";
        command.ExecuteNonQuery();
    }
}
