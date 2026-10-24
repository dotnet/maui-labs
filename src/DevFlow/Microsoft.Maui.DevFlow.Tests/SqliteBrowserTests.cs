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
        Assert.Equal(["1", "2"], result.GetProperty("rowIds").EnumerateArray().Select(x => x.GetString()));

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
    public async Task Create_FailedHostConfigurationLeavesNoPartialDatabase()
    {
        await using var fixture = await Fixture.CreateAsync();
        var target = Path.Combine(fixture.Root, "failed.db");
        var options = new SqliteBrowserOptions
        {
            ConfigureConnection = connection =>
            {
                Assert.True(connection.IsNew);
                Assert.Equal(target, connection.Path);
                throw new InvalidOperationException("Host configuration failed.");
            }
        };
        Assert.Throws<InvalidOperationException>(() => SqliteGateway.Create(target, options: options));
        Assert.False(File.Exists(target));
        Assert.DoesNotContain(Directory.GetFiles(fixture.Root),
            p => Path.GetFileName(p).StartsWith(".devflow-database-", StringComparison.Ordinal));
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
        Assert.Equal(["1", "2"], rows.GetProperty("rowIds").EnumerateArray().Select(x => x.GetString()));

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

    [Fact]
    public async Task GeneratedRowidColumn_DoesNotShadowTheIdentityUsedForEdits()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Client.QueryDatabaseAsync("app.db",
            "CREATE TABLE generated (label TEXT, rowid INTEGER GENERATED ALWAYS AS (1) VIRTUAL);"
            + "INSERT INTO generated(label) VALUES ('first'), ('second')");
        var rows = await fixture.Client.ReadDatabaseRowsAsync("app.db", "generated");
        Assert.Equal(["1", "2"], rows.RowIds);
        var result = await fixture.Client.UpdateDatabaseRecordAsync("app.db", "generated", rows.RowIds[0],
            [new() { Column = "label", Type = "text", Value = "changed" }], rows.RowVersions[0]);
        Assert.Null(result.Error);
        Assert.Equal(1, result.RowsAffected);
        var remaining = await fixture.Client.ReadDatabaseRowsAsync("app.db", "generated");
        Assert.Equal(["changed", "second"], remaining.Rows.Select(r => r[0]));

        var schema = await fixture.Client.ReadDatabaseSchemaAsync("app.db");
        var column = schema.Tables.Single(t => t.Name == "generated").Columns.Single(c => c.Name == "rowid");
        Assert.True(column.IsGenerated);
        Assert.False(column.IsWritable);
        var refused = await fixture.Client.UpdateDatabaseRecordAsync("app.db", "generated", "1",
            [new() { Column = "rowid", Value = "8" }]);
        Assert.Contains("generated or hidden", refused.Error);
    }

    [Fact]
    public async Task ColumnMatching_FoldsAsciiButNotDistinctUnicodeNames()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Client.QueryDatabaseAsync("app.db",
            "CREATE TABLE unicode (\"é\" TEXT, \"É\" TEXT); INSERT INTO unicode VALUES ('lower', 'upper')");
        var changed = await fixture.Client.UpdateDatabaseRecordAsync("app.db", "unicode", "1",
            [new() { Column = "É", Value = "edited" }]);
        Assert.Null(changed.Error);
        Assert.Equal(new string?[] { "lower", "edited" }, changed.Rows[0].AsEnumerable());
    }

    [Fact]
    public async Task RowIds_RoundTripAll64BitsAsStrings()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Client.QueryDatabaseAsync("app.db",
            "INSERT INTO notes(rowid, body) VALUES (9007199254740992, 'one'), (9007199254740993, 'two'), (9223372036854775807, 'last')");
        var rows = await fixture.Client.ReadDatabaseRowsAsync("app.db", "notes");
        Assert.Equal(["9007199254740992", "9007199254740993", "9223372036854775807"], rows.RowIds);
        var changed = await fixture.Client.UpdateDatabaseRecordAsync("app.db", "notes", rows.RowIds[1],
            [new() { Column = "body", Value = "changed" }]);
        Assert.Equal("9007199254740993", changed.RowId);
        Assert.Equal("changed", changed.Rows[0][0]);
        Assert.Null(changed.Error);
    }

    [Fact]
    public async Task TypedCells_RoundTripBlobsNumbersNullAndEmbeddedNullText()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Client.QueryDatabaseAsync("app.db", "CREATE TABLE dynamic (a, b, c, d, e, f)");
        var inserted = await fixture.Client.InsertDatabaseRecordAsync("app.db", "dynamic",
        [
            new() { Column = "a", Type = "integer", Value = "9223372036854775807" },
            new() { Column = "b", Type = "real", Value = "1.5" },
            new() { Column = "c", Type = "blob", Value = "AP8=" },
            new() { Column = "d", Type = "null" },
            new() { Column = "e", Type = "text", Value = "a\0b" },
            new() { Column = "f", Type = "blob", Value = "" }
        ]);
        Assert.Null(inserted.Error);
        Assert.Equal(["integer", "real", "blob", "null", "text", "blob"], inserted.Cells[0].Select(c => c.Type));
        Assert.Equal(["9223372036854775807", "1.5", "AP8=", null, "a\0b", ""], inserted.Cells[0].Select(c => c.Value));
        Assert.Equal("a\0b", inserted.Rows[0][4]);
        var updated = await fixture.Client.UpdateDatabaseRecordAsync("app.db", "dynamic", inserted.RowId!,
            inserted.Cells[0].Select((c, i) => new SqliteCellBody
            {
                Column = inserted.Columns[i], Type = c.Type, Value = c.Value, Truncated = c.Truncated
            }), inserted.RowVersion);
        Assert.Null(updated.Error);
        Assert.Equal(inserted.Cells[0].Select(c => (c.Type, c.Value)), updated.Cells[0].Select(c => (c.Type, c.Value)));
    }

    [Fact]
    public async Task Rows_PaginateByRowidAndRejectStaleOrCrossTableCursors()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Client.ReadDatabaseRowsAsync("app.db", "people", maxRows: 1);
        Assert.Equal(["1"], first.RowIds);
        Assert.NotNull(first.NextCursor);
        var second = await fixture.Client.ReadDatabaseRowsAsync("app.db", "people", 1, first.NextCursor);
        Assert.Equal(["2"], second.RowIds);
        Assert.False(second.Truncated);
        Assert.Null(second.NextCursor);
        var wrongTable = await fixture.Client.ReadDatabaseRowsAsync("app.db", "notes", 1, first.NextCursor);
        Assert.Contains("cursor", wrongTable.Error);
        await fixture.Client.QueryDatabaseAsync("app.db", "ALTER TABLE people ADD COLUMN more TEXT");
        var stale = await fixture.Client.ReadDatabaseRowsAsync("app.db", "people", 1, first.NextCursor);
        Assert.Contains("schema changed", stale.Error);
    }

    [Theory]
    [InlineData("CREATE VIEW page AS SELECT name FROM people", "page", "Ada", "Grace")]
    [InlineData("CREATE TABLE page (name TEXT PRIMARY KEY) WITHOUT ROWID; INSERT INTO page VALUES ('b'), ('a')", "page", "a", "b")]
    public async Task ReadOnlyRows_PaginateInDeterministicOrder(string sql, string table, string firstValue, string secondValue)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Client.QueryDatabaseAsync("app.db", sql);
        var first = await fixture.Client.ReadDatabaseRowsAsync("app.db", table, 1);
        var second = await fixture.Client.ReadDatabaseRowsAsync("app.db", table, 1, first.NextCursor);
        Assert.Empty(first.RowIds);
        Assert.Equal(firstValue, first.Rows[0][0]);
        Assert.Equal(secondValue, second.Rows[0][0]);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task ResultLimits_BoundRowsCellDataAndTheSerializedResponse()
    {
        await using var fixture = await Fixture.CreateAsync();
        var many = await fixture.Client.ExecuteDatabaseAsync("app.db",
            "WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<2000) SELECT x FROM n",
            int.MaxValue);
        Assert.Equal(SqliteBrowser.MaxRows, many.Rows.Length);
        Assert.True(many.Truncated);

        var large = await fixture.Client.ExecuteDatabaseAsync("app.db", "SELECT zeroblob(100000), replace(hex(zeroblob(50000)), '0', 'a')");
        Assert.Null(large.Error);
        Assert.All(large.Cells[0], c => { Assert.True(c.Truncated); Assert.Null(c.Value); });

        var bounded = await fixture.Client.ExecuteDatabaseAsync("app.db",
            "WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<100) SELECT replace(hex(zeroblob(16000)), '0', 'a') FROM n");
        Assert.True(bounded.Truncated);
        Assert.InRange(bounded.Rows.Length, 1, 99);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(bounded, StorageJsonContext.Default.SqliteQueryResponse).Length <= SqliteBrowser.MaxResponseBytes);
        var refused = await fixture.Client.UpdateDatabaseRecordAsync("app.db", "people", "1",
            [new() { Column = "name", Type = "text", Value = null, Truncated = true }]);
        Assert.Contains("truncated", refused.Error);
    }

    [Fact]
    public async Task OptimisticWrites_RejectChangedRowsWithoutOverwritingTheApp()
    {
        await using var fixture = await Fixture.CreateAsync();
        var rows = await fixture.Client.ReadDatabaseRowsAsync("app.db", "counters");
        await fixture.Client.QueryDatabaseAsync("app.db", "UPDATE counters SET value = 42");
        var update = await fixture.Client.UpdateDatabaseRecordAsync("app.db", "counters", "1",
            [new() { Column = "value", Value = "7" }], rows.RowVersions[0]);
        Assert.Equal("conflict", update.ErrorCode);
        var delete = await fixture.Client.DeleteDatabaseRecordAsync("app.db", "counters", "1", rows.RowVersions[0]);
        Assert.Equal("conflict", delete.ErrorCode);
        var stored = await fixture.Client.ReadDatabaseRowsAsync("app.db", "counters");
        Assert.Equal("42", stored.Rows[0][1]);
        var allowed = await fixture.Client.DeleteDatabaseRecordAsync("app.db", "counters", "1", stored.RowVersions[0]);
        Assert.Null(allowed.Error);
        Assert.Equal(1, allowed.RowsAffected);
    }

    [Fact]
    public async Task Update_ChangingIntegerPrimaryKeyReturnsTheNewIdentity()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Client.UpdateDatabaseRecordAsync("app.db", "people", "1",
            [new() { Column = "id", Type = "integer", Value = "42" }]);
        Assert.Null(result.Error);
        Assert.Equal("42", result.RowId);
        Assert.Equal("Ada", result.Rows[0][1]);
    }

    [Fact]
    public async Task HostConfiguration_RunsBeforeValidationAndOnReadOnlyConnections()
    {
        await using var fixture = await Fixture.CreateAsync();
        var seen = new List<bool>();
        var options = new SqliteBrowserOptions
        {
            ConfigureConnection = connection =>
            {
                Assert.Equal(fixture.DatabasePath, connection.Path);
                seen.Add(connection.IsReadOnly);
                connection.Execute("PRAGMA cache_size = 16");
            },
            InitializeProvider = () => throw new InvalidOperationException("An existing provider must not be replaced.")
        };
        Assert.NotEmpty(SqliteBrowser.ReadSchema(fixture.DatabasePath, options: options).Tables);
        var result = SqliteBrowser.Query(fixture.DatabasePath, "PRAGMA cache_size", 1, options: options);
        Assert.Equal("16", result.Rows[0][0]);
        Assert.Equal([true, false], seen);
    }

    [Fact]
    public async Task Cancellation_InterruptsAQueryAndDoesNotHoldTheDatabaseLock()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var result = await Task.Run(() => SqliteBrowser.Query(fixture.DatabasePath,
            "WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n) SELECT count(*) FROM n",
            1, cancellationToken: cancel.Token)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("cancelled", result.ErrorCode);
        Assert.NotEmpty(SqliteBrowser.ReadSchema(fixture.DatabasePath).Tables);
    }

    [Fact]
    public async Task Cancellation_StopsWaitingForAnAppOwnedLock()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var owner = new SqliteConnection($"Data Source={fixture.DatabasePath};Pooling=False");
        await owner.OpenAsync();
        await using var command = owner.CreateCommand();
        command.CommandText = "BEGIN EXCLUSIVE";
        await command.ExecuteNonQueryAsync();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var result = await Task.Run(() => SqliteBrowser.ReadRows(fixture.DatabasePath, "people", 1,
            cancellationToken: cancel.Token)).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("cancelled", result.ErrorCode);
    }

    [Fact]
    public async Task ConfiguredDeadline_ReturnsASqlErrorBeforeTheHttpClientTimesOut()
    {
        await using var fixture = await Fixture.CreateAsync(TimeSpan.FromMilliseconds(100));
        var result = await fixture.Client.ExecuteDatabaseAsync("app.db",
            "WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n) SELECT count(*) FROM n");
        Assert.Equal("timeout", result.ErrorCode);
        Assert.Contains("was stopped", result.Error);
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

        public static async Task<Fixture> CreateAsync(TimeSpan? timeout = null)
        {
            var root = Path.Combine(Path.GetTempPath(), "devflow-sqlite-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            await SeedAsync(Path.Combine(root, "app.db"));

            var port = GetFreePort();
            var service = new RootedAgentService(port, root, timeout);
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

        public RootedAgentService(int port, string root, TimeSpan? timeout = null)
            // Leases stay on, as they are by default: every write route here has to get through the
            // same gate a real session puts it behind, or a client that forgets to claim one passes.
            : base(new AgentOptions { Port = port })
        {
            _root = root;
            if (timeout.HasValue)
                _options.Sqlite.Timeout = timeout.Value;
        }

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
