using Microsoft.Maui.DevFlow.Agent.Core.Sqlite;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// The SQLite half of the file browser: a schema, an editable grid, and a query pane that can change
/// what it reads - all against the live file, inside the app that owns it.
/// </summary>
public partial class DevFlowAgentService
{
    private const string SqliteApi = "/api/v1/storage/sqlite";

    /// <summary>
    /// The cap that keeps a <c>SELECT *</c> against a million rows from being the answer to a
    /// question nobody asked. Results say whether it bit.
    /// </summary>
    private const int DefaultSqliteMaxRows = 500;

    private void MapSqliteRoutes()
    {
        _server.MapGet($"{SqliteApi}/schema", HandleSqliteSchema);
        _server.MapPost($"{SqliteApi}/query", HandleSqliteQuery);
        _server.MapGet($"{SqliteApi}/rows", HandleSqliteRows);
        _server.MapPost($"{SqliteApi}/rows", HandleSqliteInsertRow);
        _server.MapPut($"{SqliteApi}/rows", HandleSqliteUpdateRow);
        _server.MapPost($"{SqliteApi}/rows/delete", HandleSqliteDeleteRow);
        _server.MapPost($"{SqliteApi}/create", HandleSqliteCreate);
    }

    /// <summary>
    /// Turns a root and a relative path into a file on disk, under the same containment rules every
    /// other file route obeys. The operation asked for is <c>download</c> or <c>upload</c> so that a
    /// root published read-only stays read-only here too.
    /// </summary>
    private string ResolveDatabasePath(HttpRequest request, bool writing)
    {
        var root = ResolveFileStorageRoot(request, writing ? FileStorageOperationUpload : FileStorageOperationDownload);

        var relativePath = request.QueryParams.GetValueOrDefault("path");
        if (string.IsNullOrWhiteSpace(relativePath))
            relativePath = ReadRequestedPath(request);

        if (string.IsNullOrWhiteSpace(relativePath))
            throw new InvalidOperationException("file path is required");

        var resolved = FileStoragePathResolver.Resolve(root.BasePath, relativePath);
        FileStoragePathResolver.EnsureNoReparsePointTraversal(resolved.BasePath, resolved.FullPath, includeTarget: true);

        if (!File.Exists(resolved.FullPath))
            throw new FileNotFoundException($"File not found: {resolved.RelativePath}");

        return resolved.FullPath;
    }

    private static string? ReadRequestedPath(HttpRequest request)
        => request.BodyAs(AgentJsonContext.Default.SqliteRequestBody)?.Path;

    private Task<HttpResponse> HandleSqliteSchema(HttpRequest request)
    {
        try
        {
            var fullPath = ResolveDatabasePath(request, writing: false);
            return Task.FromResult(HttpResponse.Json(
                SqliteBrowser.ReadSchema(fullPath), AgentJsonContext.Default.SqliteSchemaResponse));
        }
        catch (FileNotFoundException ex)
        {
            return Task.FromResult(HttpResponse.NotFound(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(HttpResponse.Error(ex.Message));
        }
        catch (Exception)
        {
            return Task.FromResult(HttpResponse.Error("Failed to read the database schema"));
        }
    }

    private Task<HttpResponse> HandleSqliteQuery(HttpRequest request)
    {
        try
        {
            var body = request.BodyAs(AgentJsonContext.Default.SqliteQueryBody);
            if (body == null || string.IsNullOrWhiteSpace(body.Sql))
                return Task.FromResult(HttpResponse.Error("Request body must include 'sql'"));

            // Writing, because the query pane is a database client: it can UPDATE, DELETE and run
            // DDL. A root that does not allow uploads does not allow this either.
            var fullPath = ResolveDatabasePath(request, writing: true);

            // A non-positive cap means "use the default", the same as on the rows route. Passing 0
            // through would answer every statement with no rows and truncated: true.
            var maxRows = body.MaxRows is > 0 ? body.MaxRows.Value : DefaultSqliteMaxRows;

            return Task.FromResult(HttpResponse.Json(
                SqliteBrowser.Query(fullPath, body.Sql, maxRows),
                AgentJsonContext.Default.SqliteQueryResponse));
        }
        catch (FileNotFoundException ex)
        {
            return Task.FromResult(HttpResponse.NotFound(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(HttpResponse.Error(ex.Message));
        }
        catch (Exception)
        {
            return Task.FromResult(HttpResponse.Error("Failed to run the statement"));
        }
    }

    private Task<HttpResponse> HandleSqliteRows(HttpRequest request)
    {
        try
        {
            var table = request.QueryParams.GetValueOrDefault("table");
            if (string.IsNullOrWhiteSpace(table))
                return Task.FromResult(HttpResponse.Error("table is required"));

            var fullPath = ResolveDatabasePath(request, writing: false);
            var maxRows = ParsePositiveInt(request.QueryParams.GetValueOrDefault("maxRows")) ?? DefaultSqliteMaxRows;

            return Task.FromResult(HttpResponse.Json(
                SqliteBrowser.ReadRows(fullPath, table, maxRows), AgentJsonContext.Default.SqliteRowsResponse));
        }
        catch (FileNotFoundException ex)
        {
            return Task.FromResult(HttpResponse.NotFound(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(HttpResponse.Error(ex.Message));
        }
        catch (Exception)
        {
            return Task.FromResult(HttpResponse.Error("Failed to read the table"));
        }
    }

    private Task<HttpResponse> HandleSqliteInsertRow(HttpRequest request)
    {
        return WithRowRequest(request, (fullPath, body)
            => SqliteBrowser.InsertRow(fullPath, body.Table!, ToEdits(body.Values)));
    }

    private Task<HttpResponse> HandleSqliteUpdateRow(HttpRequest request)
    {
        return WithRowRequest(request, (fullPath, body) =>
        {
            if (body.RowId is not { } rowId)
                throw new InvalidOperationException("Request body must include 'rowId'");

            return SqliteBrowser.UpdateRow(fullPath, body.Table!, rowId, ToEdits(body.Values));
        });
    }

    private Task<HttpResponse> HandleSqliteDeleteRow(HttpRequest request)
    {
        return WithRowRequest(request, (fullPath, body) =>
        {
            if (body.RowId is not { } rowId)
                throw new InvalidOperationException("Request body must include 'rowId'");

            return SqliteBrowser.DeleteRow(fullPath, body.Table!, rowId);
        });
    }

    /// <summary>The parts every row route shares: the file, the table name, and how failures read.</summary>
    private Task<HttpResponse> WithRowRequest(HttpRequest request, Func<string, SqliteRowBody, SqliteQueryResponse> act)
    {
        try
        {
            var body = request.BodyAs(AgentJsonContext.Default.SqliteRowBody);
            if (body == null || string.IsNullOrWhiteSpace(body.Table))
                return Task.FromResult(HttpResponse.Error("Request body must include 'table'"));

            var fullPath = ResolveDatabasePath(request, writing: true);
            return Task.FromResult(HttpResponse.Json(act(fullPath, body), AgentJsonContext.Default.SqliteQueryResponse));
        }
        catch (FileNotFoundException ex)
        {
            return Task.FromResult(HttpResponse.NotFound(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(HttpResponse.Error(ex.Message));
        }
        catch (Exception)
        {
            return Task.FromResult(HttpResponse.Error("Failed to change the record"));
        }
    }

    /// <summary>
    /// Makes a new, empty database - a real one, with the header a file needs to be opened as a
    /// database by anything it is later handed to.
    /// </summary>
    private Task<HttpResponse> HandleSqliteCreate(HttpRequest request)
    {
        try
        {
            var root = ResolveFileStorageRoot(request, FileStorageOperationUpload);

            var body = request.BodyAs(AgentJsonContext.Default.SqliteRequestBody);
            if (body == null || string.IsNullOrWhiteSpace(body.Path))
                return Task.FromResult(HttpResponse.Error("Request body must include 'path'"));

            var resolved = FileStoragePathResolver.Resolve(root.BasePath, body.Path);
            FileStoragePathResolver.EnsureNoReparsePointTraversal(resolved.BasePath, resolved.FullPath, includeTarget: true);

            if (File.Exists(resolved.FullPath) || Directory.Exists(resolved.FullPath))
                return Task.FromResult(HttpResponse.Error($"Something is already called {resolved.RelativePath}"));

            var directory = Path.GetDirectoryName(resolved.FullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            SqliteGateway.Create(resolved.FullPath);

            return Task.FromResult(HttpResponse.Json(
                new SqliteCreatedResponse
                {
                    Success = true,
                    Root = root.Id,
                    Path = resolved.RelativePath,
                    Size = new FileInfo(resolved.FullPath).Length
                },
                AgentJsonContext.Default.SqliteCreatedResponse));
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(HttpResponse.Error(ex.Message));
        }
        catch (Exception)
        {
            return Task.FromResult(HttpResponse.Error("Failed to create the database"));
        }
    }

    private static IReadOnlyList<SqliteCellEdit> ToEdits(SqliteCellBody[]? cells)
        => cells?.Select(x => new SqliteCellEdit(x.Column, x.Value)).ToArray() ?? [];

    private static int? ParsePositiveInt(string? text)
        => int.TryParse(text, out var value) && value > 0 ? value : null;
}
