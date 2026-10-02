using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core.Sqlite;
using Microsoft.Maui.DevFlow.Driver;
using SQLitePCL;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// The database browser, end to end over the agent's HTTP surface. Everything here runs against a
/// real file, because the parts worth testing - the authorizer, the rowid addressing, type affinity
/// on a write - are all things SQLite does rather than things this code does.
/// </summary>
public class SqliteBrowserTests
{
    [Fact]
    public async Task Schema_ReportsTablesViewsColumnsAndIndexes()
    {
        await using var fixture = await Fixture.CreateAsync();

        var schema = await fixture.Client.GetDatabaseSchemaAsync("app.db");
        var tables = schema.GetProperty("tables").EnumerateArray().ToArray();

        // Tables first, then views, each alphabetically - and SQLite's own internal ones left out.
        Assert.Equal(
            ["counters", "notes", "people", "named"],
            tables.Select(x => x.GetProperty("name").GetString()));
        Assert.Equal(
            ["table", "table", "table", "view"],
            tables.Select(x => x.GetProperty("kind").GetString()));

        var people = tables.Single(x => x.GetProperty("name").GetString() == "people");
        Assert.True(people.GetProperty("hasRowId").GetBoolean());

        var name = people.GetProperty("columns").EnumerateArray().Single(x => x.GetProperty("name").GetString() == "name");
        Assert.Equal("TEXT", name.GetProperty("type").GetString());
        Assert.True(name.GetProperty("notNull").GetBoolean());

        // The index a UNIQUE constraint brought with it is reported too - a query plan will name it,
        // so a schema that did not admit to having it would leave the reader stuck.
        var indexes = people.GetProperty("indexes").EnumerateArray().ToArray();
        Assert.Contains(indexes, x => x.GetProperty("name").GetString() == "ix_people_name"
            && !x.GetProperty("automatic").GetBoolean());
        Assert.Contains(indexes, x => x.GetProperty("automatic").GetBoolean()
            && x.GetProperty("unique").GetBoolean());

        // A view has no rowid, so it can be read but never edited.
        var view = tables.Single(x => x.GetProperty("kind").GetString() == "view");
        Assert.False(view.GetProperty("hasRowId").GetBoolean());
        Assert.Empty(view.GetProperty("indexes").EnumerateArray());
    }

    [Fact]
    public async Task Rows_CarryRowIdsAndDescribeBlobsRatherThanSendingThem()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Client.GetDatabaseRowsAsync("app.db", "people");

        Assert.Equal(["id", "name", "email", "avatar"], result.GetProperty("columns").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal([1L, 2L], result.GetProperty("rowIds").EnumerateArray().Select(x => x.GetInt64()));

        var ada = result.GetProperty("rows")[0];
        Assert.Equal("Ada", ada[1].GetString());

        // A cell cannot show a blob and a row should not cost what one weighs.
        Assert.StartsWith("BLOB[16] 89504E47", ada[3].GetString());

        // Null stays null: "no value" and the word "NULL" are different cells.
        Assert.Equal(JsonValueKind.Null, result.GetProperty("rows")[1][3].ValueKind);
    }

    [Fact]
    public async Task Rows_OfAViewComeBackWithNoRowIdsToEditBy()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Client.GetDatabaseRowsAsync("app.db", "named");

        Assert.Equal(2, result.GetProperty("rows").GetArrayLength());
        Assert.Empty(result.GetProperty("rowIds").EnumerateArray());
    }

    [Fact]
    public async Task Rows_Truncate_AndSayThatTheyDid()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Client.GetDatabaseRowsAsync("app.db", "people", maxRows: 1);

        Assert.Equal(1, result.GetProperty("rows").GetArrayLength());
        Assert.True(result.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task Query_RunsAScriptAndReportsWhatTheWriteDid()
    {
        await using var fixture = await Fixture.CreateAsync();

        // The script only does what it looks like if every statement runs, not just the one whose rows
        // are shown.
        var result = await fixture.Client.QueryDatabaseAsync(
            "app.db",
            "UPDATE people SET email = 'ada@lovelace.example' WHERE name = 'Ada'; SELECT email FROM people WHERE name = 'Ada'");

        Assert.Null(GetError(result));
        Assert.Equal("ada@lovelace.example", result.GetProperty("rows")[0][0].GetString());

        // -1 rather than 0: "0 rows affected" is a true and useful thing for a DELETE to say, and
        // "not that kind of statement" is not the same answer.
        Assert.Equal(-1, result.GetProperty("rowsAffected").GetInt32());
    }

    [Fact]
    public async Task Query_ReportsAFailedStatementAsAnAnswerRatherThanAFault()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Client.QueryDatabaseAsync("app.db", "SELECT * FROM nowhere");

        Assert.Contains("nowhere", GetError(result));
        Assert.Empty(result.GetProperty("rows").EnumerateArray());
    }

    [Fact]
    public async Task Query_CannotAttachASecondFile()
    {
        await using var fixture = await Fixture.CreateAsync();

        var elsewhere = Path.Combine(fixture.Root, "elsewhere.db");
        var result = await fixture.Client.QueryDatabaseAsync("app.db", $"ATTACH DATABASE '{elsewhere}' AS other");

        // Refused by SQLite's own authorizer while the statement was being prepared, so it never
        // ran - rather than by string-matching the SQL, which loses to a comment and a line break.
        Assert.Contains("not authorized", GetError(result));
    }

    [Fact]
    public async Task Query_CannotLoadAnExtension()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Client.QueryDatabaseAsync("app.db", "SELECT load_extension('evil')");

        Assert.Contains("not authorized", GetError(result));
    }

    [Fact]
    public async Task Insert_ReadsTheRecordBackRatherThanEchoingWhatWasSent()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Client.InsertDatabaseRowAsync(
            "app.db", "people", [("name", "Katherine")]);

        Assert.Null(GetError(result));
        Assert.Equal(1, result.GetProperty("rowsAffected").GetInt32());

        // The id was chosen by the table, so it can only come from reading the record back.
        Assert.Equal("3", result.GetProperty("rows")[0][0].GetString());
        Assert.Equal("Katherine", result.GetProperty("rows")[0][1].GetString());
    }

    [Fact]
    public async Task Update_AppliesTheColumnsAffinityAndAnswersWithWhatIsStored()
    {
        await using var fixture = await Fixture.CreateAsync();

        // "7" typed into an INTEGER column is stored as the number, which is why a grid that kept
        // showing what was typed would be showing something the database does not contain.
        var result = await fixture.Client.UpdateDatabaseRowAsync("app.db", "counters", 1, [("value", "7")]);

        Assert.Null(GetError(result));
        Assert.Equal("7", result.GetProperty("rows")[0][1].GetString());

        var stored = await fixture.Client.QueryDatabaseAsync("app.db", "SELECT typeof(value) FROM counters WHERE rowid = 1");
        Assert.Equal("integer", stored.GetProperty("rows")[0][0].GetString());
    }

    [Fact]
    public async Task Update_RefusesAColumnTheTableDoesNotHave()
    {
        await using var fixture = await Fixture.CreateAsync();

        // The column name is looked up in the schema and never reaches a statement, so this is
        // refused by name rather than by SQLite parsing it.
        var result = await fixture.Client.UpdateDatabaseRowAsync(
            "app.db", "people", 1, [("name\" = '', email = 'stolen'; --", "x")]);

        Assert.Contains("has no column called", GetError(result));

        var untouched = await fixture.Client.GetDatabaseRowsAsync("app.db", "people");
        Assert.Equal("ada@example.com", untouched.GetProperty("rows")[0][2].GetString());
    }

    [Fact]
    public async Task Update_SaysSoWhenTheRecordIsNoLongerThere()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Client.UpdateDatabaseRowAsync("app.db", "people", 9999, [("name", "Nobody")]);

        Assert.Contains("no longer there", GetError(result));
    }

    [Fact]
    public async Task Write_ToAViewIsRefused()
    {
        await using var fixture = await Fixture.CreateAsync();

        // A view has no rowid, so there is no record for an UPDATE to name.
        var result = await fixture.Client.UpdateDatabaseRowAsync("app.db", "named", 1, [("name", "x")]);

        Assert.Contains("no table called 'named'", GetError(result));
    }

    [Fact]
    public async Task Delete_RemovesTheRecord()
    {
        await using var fixture = await Fixture.CreateAsync();

        var deleted = await fixture.Client.DeleteDatabaseRowAsync("app.db", "people", 2);
        Assert.Equal(1, deleted.GetProperty("rowsAffected").GetInt32());

        var rows = await fixture.Client.GetDatabaseRowsAsync("app.db", "people");
        Assert.Equal(1, rows.GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public async Task Schema_OfAFileThatIsNotADatabaseSaysSo()
    {
        await using var fixture = await Fixture.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "notes.db"), "just some text");

        var result = await fixture.Client.GetDatabaseSchemaAsync("notes.db");

        // The extension is a guess, and this is the single most likely way it turns out wrong.
        Assert.Contains("is not a SQLite database", result.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Create_WritesADatabaseThatCanThenBeOpened()
    {
        await using var fixture = await Fixture.CreateAsync();

        var created = await fixture.Client.CreateDatabaseAsync("fresh/new.db");
        Assert.True(created.GetProperty("success").GetBoolean());

        // A file with the header, not an empty one: opening alone leaves zero bytes behind.
        Assert.True(new FileInfo(Path.Combine(fixture.Root, "fresh", "new.db")).Length > 0);

        var schema = await fixture.Client.GetDatabaseSchemaAsync("fresh/new.db");
        Assert.Empty(schema.GetProperty("tables").EnumerateArray());
    }

    [Fact]
    public async Task Create_RefusesToOverwriteSomethingAlreadyThere()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Client.CreateDatabaseAsync("app.db");

        Assert.Contains("already called", result.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Rows_AreFoundWhateverCaseTheTableIsAskedForIn()
    {
        await using var fixture = await Fixture.CreateAsync();

        // SQLite resolves PEOPLE to people, so refusing this would refuse a name the engine accepts.
        var result = await fixture.Client.GetDatabaseRowsAsync("app.db", "PeOpLe");

        Assert.Null(GetError(result));
        Assert.Equal(2, result.GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public async Task Update_AcceptsATableAndColumnInAnyCase()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Client.UpdateDatabaseRowAsync("app.db", "PEOPLE", 1, [("NAME", "Ada L")]);

        Assert.Null(GetError(result));

        // Read back under the spelling the table actually uses, to show the write landed there.
        var rows = await fixture.Client.GetDatabaseRowsAsync("app.db", "people");
        Assert.Equal("Ada L", rows.GetProperty("rows")[0][1].GetString());
    }

    [Fact]
    public async Task Update_StillRefusesAColumnThatDoesNotExistInAnyCase()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Client.UpdateDatabaseRowAsync("app.db", "people", 1, [("nope", "x")]);

        Assert.Contains("has no column called", GetError(result));
    }

    [Fact]
    public async Task Query_TreatsANonPositiveRowCapAsTheDefault()
    {
        await using var fixture = await Fixture.CreateAsync();

        // 0 passed straight through would answer every statement with no rows and truncated: true.
        var result = await fixture.Client.QueryDatabaseAsync("app.db", "SELECT * FROM people", maxRows: 0);

        Assert.Null(GetError(result));
        Assert.Equal(2, result.GetProperty("rows").GetArrayLength());
        Assert.False(result.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task Rows_OfATableWithItsOwnRowidColumn_AreAddressedByTheRealRowid()
    {
        await using var fixture = await Fixture.CreateAsync();

        // A declared column called rowid, holding the same value twice. "WHERE rowid = 1" means this
        // column now, so an edit addressed that way would change both records.
        await fixture.Client.QueryDatabaseAsync("app.db",
            "CREATE TABLE tags (rowid INTEGER, label TEXT); INSERT INTO tags VALUES (1, 'first'), (1, 'second');");

        var rows = await fixture.Client.GetDatabaseRowsAsync("app.db", "tags");
        Assert.Equal(["rowid", "label"], rows.GetProperty("columns").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal([1L, 2L], rows.GetProperty("rowIds").EnumerateArray().Select(x => x.GetInt64()));

        var updated = await fixture.Client.UpdateDatabaseRowAsync("app.db", "tags", 1, [("label", "changed")]);
        Assert.Null(GetError(updated));
        Assert.Equal(1, updated.GetProperty("rowsAffected").GetInt32());

        var deleted = await fixture.Client.DeleteDatabaseRowAsync("app.db", "tags", 2);
        Assert.Equal(1, deleted.GetProperty("rowsAffected").GetInt32());

        var left = await fixture.Client.QueryDatabaseAsync("app.db", "SELECT label FROM tags");
        Assert.Equal(["changed"], left.GetProperty("rows").EnumerateArray().Select(x => x[0].GetString()));
    }

    [Fact]
    public async Task Rows_OfATableThatHasTakenEveryRowidName_AreReadOnly()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.Client.QueryDatabaseAsync("app.db",
            "CREATE TABLE opaque (rowid INTEGER, _rowid_ INTEGER, oid INTEGER); INSERT INTO opaque VALUES (1, 1, 1);");

        var schema = await fixture.Client.GetDatabaseSchemaAsync("app.db");
        var opaque = schema.GetProperty("tables").EnumerateArray().Single(x => x.GetProperty("name").GetString() == "opaque");
        Assert.False(opaque.GetProperty("hasRowId").GetBoolean());

        var rows = await fixture.Client.GetDatabaseRowsAsync("app.db", "opaque");
        Assert.Equal(1, rows.GetProperty("rows").GetArrayLength());
        Assert.Empty(rows.GetProperty("rowIds").EnumerateArray());

        var refused = await fixture.Client.UpdateDatabaseRowAsync("app.db", "opaque", 1, [("oid", "2")]);
        Assert.Contains("cannot be edited", GetError(refused));
    }

    [Fact]
    public async Task Rows_OfAViewThatNeverFinishes_AreStoppedAtTheDeadline()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Client.QueryDatabaseAsync("app.db", EndlessView);

        // Opening a view in the grid is a query like any other, and this one never produces a row.
        var result = await Task.Run(() => SqliteBrowser.ReadRows(fixture.DatabasePath, "endless", 10, TimeSpan.FromMilliseconds(500)))
            .WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Contains("was stopped", result.Error);
    }

    [Fact]
    public async Task Update_WhoseTriggerNeverFinishes_IsStoppedAtTheDeadlineAndChangesNothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Client.QueryDatabaseAsync("app.db",
            EndlessView + "; CREATE TRIGGER stall AFTER UPDATE ON counters BEGIN SELECT count(*) FROM endless; END;");

        // Changing one cell runs the trigger, and the trigger holds the write lock for as long as it runs.
        var result = await Task.Run(() => SqliteBrowser.UpdateRow(
                fixture.DatabasePath, "counters", 1, [new SqliteCellEdit("value", "8")], TimeSpan.FromMilliseconds(500)))
            .WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Contains("was stopped", result.Error);

        var stored = await fixture.Client.QueryDatabaseAsync("app.db", "SELECT value FROM counters");
        Assert.Equal("0", stored.GetProperty("rows")[0][0].GetString());
    }

    [Fact]
    public async Task Values_AreWrittenTheSameWayWhateverCultureTheAppRunsIn()
    {
        await using var fixture = await Fixture.CreateAsync();

        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            // "1,5" would come back as TEXT when the grid sent it back, and fail outright in a STRICT table.
            var result = SqliteBrowser.Query(fixture.DatabasePath, "SELECT 1.5, 0.25, -2", 10);

            Assert.Null(result.Error);
            Assert.Equal("1.5 | 0.25 | -2", string.Join(" | ", result.Rows[0]));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task Authorizer_RefusesLoadExtensionWhilePreparing_EvenWithExtensionLoadingOn()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var database = SqliteGateway.Open(fixture.DatabasePath);

        // Turned on deliberately, so the authorizer is the only thing left that can say no. With loading
        // off, SQLite answers "not authorized" at run time by itself, and an authorizer that checks the
        // wrong argument still passes a test that only runs the query.
        Assert.Equal(raw.SQLITE_OK, raw.sqlite3_enable_load_extension(database.Handle, 1));

        var rc = raw.sqlite3_prepare_v2(database.Handle, "SELECT load_extension('evil')", out var statement);
        var message = raw.sqlite3_errmsg(database.Handle).utf8_to_string();
        statement?.Dispose();

        // SQLite reports a denied function as a plain error rather than SQLITE_AUTH, so the wording is
        // what says it was the authorizer. Without one, this statement prepares successfully.
        Assert.NotEqual(raw.SQLITE_OK, rc);
        Assert.Contains("not authorized to use function", message);
    }

    [Fact]
    public void Agent_ReferencesNothingThatRegistersASqliteProvider()
    {
        // Microsoft.Data.Sqlite's connection registers the batteries bundle from its static constructor,
        // replacing the provider the app set up, and a bundle would bring a provider of the agent's own.
        var references = AssemblyReferenceGuard.GetReferencedAssemblyNames(typeof(AgentOptions).Assembly.Location);
        Assert.DoesNotContain(references, x => x.StartsWith("Microsoft.Data.Sqlite", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, x => x.StartsWith("SQLitePCLRaw.batteries", StringComparison.OrdinalIgnoreCase));

        var packages = XDocument.Load(Path.Combine(
                TestRepo.Root, "src", "DevFlow", "Microsoft.Maui.DevFlow.Agent.Abstractions", "Microsoft.Maui.DevFlow.Agent.Abstractions.csproj"))
            .Descendants("PackageReference")
            .Select(x => (string?)x.Attribute("Include") ?? "")
            .ToArray();

        Assert.DoesNotContain(packages, x => x.StartsWith("Microsoft.Data.Sqlite", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(packages, x => x.StartsWith("SQLitePCLRaw.bundle", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A view that never produces a row: counting upward, forever, for a number below zero.</summary>
    private const string EndlessView =
        "CREATE VIEW endless AS WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM n) SELECT x FROM n WHERE x < 0";

    private static string? GetError(JsonElement result)
        => result.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
            ? error.GetString()
            : null;

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly RootedAgentService _service;

        private Fixture(RootedAgentService service, AgentClient client, string root)
        {
            _service = service;
            Client = client;
            Root = root;
        }

        public AgentClient Client { get; }
        public string Root { get; }
        public string DatabasePath => Path.Combine(Root, "app.db");

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "devflow-sqlite-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            await SeedAsync(Path.Combine(root, "app.db"));

            var port = GetFreePort();
            var service = new RootedAgentService(port, root);
            service.StartServerOnly(Immediate);

            var client = new AgentClient("localhost", port);
            await WaitForAgentAsync(client);

            return new Fixture(service, client, root);
        }

        private static async Task SeedAsync(string path)
        {
            SQLitePCL.Batteries_V2.Init();

            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE people (id INTEGER PRIMARY KEY, name TEXT NOT NULL, email TEXT UNIQUE, avatar BLOB);
                CREATE INDEX ix_people_name ON people(name);
                CREATE TABLE notes (body TEXT);
                CREATE TABLE counters (label TEXT, value INTEGER);
                CREATE VIEW named AS SELECT name FROM people;
                INSERT INTO people (name, email, avatar)
                    VALUES ('Ada', 'ada@example.com', X'89504E470D0A1A0A0000000D49484452');
                INSERT INTO people (name, email) VALUES ('Grace', 'grace@example.com');
                INSERT INTO counters (label, value) VALUES ('hits', 0);
                """;
            await command.ExecuteNonQueryAsync();
        }

        private static async Task WaitForAgentAsync(AgentClient client)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var roots = await client.ListStorageRootsAsync();
                if (roots.ValueKind != JsonValueKind.Undefined)
                    return;

                await Task.Delay(100);
            }

            throw new InvalidOperationException("The agent did not start listening.");
        }

        public ValueTask DisposeAsync()
        {
            _service.Dispose();

            // Pooling is off on the agent's connections, so nothing is still holding the file.
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(Root, recursive: true); } catch (IOException) { }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class RootedAgentService : DevFlowAgentService
    {
        private readonly string _root;

        public RootedAgentService(int port, string root)
            // Leases stay on, as they are by default: every write route here has to get through the
            // same gate a real session puts it behind, or a client that forgets to claim one passes.
            : base(new AgentOptions { Port = port })
            => _root = root;

        protected override string GetAppDataBasePath() => _root;

        // One root, so a test that forgets to name one still lands somewhere predictable.
        protected override string GetCacheBasePath() => _root;
    }

    /// <summary>
    /// Runs everything inline. These tests subclass the framework-neutral service, so there is no UI
    /// thread to marshal to and nothing to wait for.
    /// </summary>
    private static readonly DelegateAgentDispatcher Immediate = new(() => false, action => action());

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
