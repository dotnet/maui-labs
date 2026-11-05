using Microsoft.Maui.DevFlow.Agent.Core.Sqlite;
using Microsoft.Maui.DevFlow.Driver;
using System.Text;
using System.Text.Json;
using System.Diagnostics;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// The SQLite half of the file browser: a schema, an editable grid, and a query pane that can change
/// what it reads - all against the live file, inside the app that owns it.
/// </summary>
public partial class DevFlowAgentService
{
    /// <summary>
    /// The cap that keeps a <c>SELECT *</c> against a million rows from being the answer to a
    /// question nobody asked. Results say whether it bit.
    /// </summary>
    private const int DefaultSqliteMaxRows = 500;

    private void MapSqliteRoutes()
    {
        _server.MapGet("/api/v1/storage/sqlite/schema", r => RunSqliteAsync(r, HandleSqliteSchema));
        _server.MapPost("/api/v1/storage/sqlite/query", r => RunSqliteAsync(r, HandleSqliteQuery));
        _server.MapGet("/api/v1/storage/sqlite/rows", r => RunSqliteAsync(r, HandleSqliteRows));
        _server.MapPost("/api/v1/storage/sqlite/rows", r => RunSqliteAsync(r, HandleSqliteInsertRow));
        _server.MapPut("/api/v1/storage/sqlite/rows", r => RunSqliteAsync(r, HandleSqliteUpdateRow));
        _server.MapPost("/api/v1/storage/sqlite/rows/delete", r => RunSqliteAsync(r, HandleSqliteDeleteRow));
        _server.MapPost("/api/v1/storage/sqlite/create", r => RunSqliteAsync(r, HandleSqliteCreate));
    }

    private Task<HttpResponse> RunSqliteAsync(HttpRequest request, Func<HttpRequest, Task<HttpResponse>> handler)
    {
        if (!SqliteGateway.IsAvailable(out var reason) && _options.Sqlite.InitializeProvider is null)
            return Task.FromResult(NotSupported("storage.sqlite", reason));
        return Task.Run(async () =>
        {
            var response = await handler(request).ConfigureAwait(false);
            if (response.Body is not null && Encoding.UTF8.GetByteCount(response.Body) > SqliteBrowser.MaxResponseBytes)
                return HttpResponse.Error("The SQLite response exceeds the 1 MiB limit. Select fewer columns or rows.", 413);
            return response;
        }, request.CancellationToken);
    }

    private static HttpResponse SqliteFault(string message, Exception exception)
    {
        if (exception is JsonException)
            return HttpResponse.Error("Invalid JSON request body.");
        if (exception is OperationCanceledException)
            return HttpResponse.Error("The database request was cancelled.", 408);
        Console.Error.WriteLine($"[Microsoft.Maui.DevFlow.Agent] {message}: {exception.GetType().Name}");
        return HttpResponse.Error(message, 500);
    }

    private TimeSpan SqliteTimeout(HttpRequest request)
    {
        var configured = _options.Sqlite.Timeout;
        if (configured <= TimeSpan.Zero || configured > TimeSpan.FromSeconds(25))
            throw new InvalidOperationException("SQLite timeout must be positive and at most 25 seconds.");
        // Admission and body reads also consume the transport budget.
        var remaining = TimeSpan.FromSeconds(25) - Stopwatch.GetElapsedTime(request.StartedAt);
        if (remaining <= TimeSpan.Zero)
            throw new OperationCanceledException();
        return remaining < configured ? remaining : configured;
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
        => request.BodyAs(StorageAgentJsonContext.Default.SqliteRequestBody)?.Path;

    private Task<HttpResponse> HandleSqliteSchema(HttpRequest request)
    {
        try
        {
            var fullPath = ResolveDatabasePath(request, writing: false);
            return Task.FromResult(HttpResponse.Json(
                SqliteBrowser.ReadSchema(fullPath, SqliteTimeout(request), request.CancellationToken, _options.Sqlite),
                StorageAgentJsonContext.Default.SqliteSchemaResponse));
        }
        catch (FileNotFoundException ex)
        {
            return Task.FromResult(HttpResponse.NotFound(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(HttpResponse.Error(ex.Message));
        }
        catch (Exception ex)
        {
            return Task.FromResult(SqliteFault("Failed to read the database schema", ex));
        }
    }

    private Task<HttpResponse> HandleSqliteQuery(HttpRequest request)
    {
        try
        {
            var body = request.BodyAs(StorageAgentJsonContext.Default.SqliteQueryBody);
            if (body == null || string.IsNullOrWhiteSpace(body.Sql))
                return Task.FromResult(HttpResponse.Error("Request body must include 'sql'"));

            // Writing, because the query pane is a database client: it can UPDATE, DELETE and run
            // DDL. A root that does not allow uploads does not allow this either.
            var fullPath = ResolveDatabasePath(request, writing: true);

            // A non-positive cap means "use the default", the same as on the rows route. Passing 0
            // through would answer every statement with no rows and truncated: true.
            var maxRows = body.MaxRows is > 0 ? body.MaxRows.Value : DefaultSqliteMaxRows;

            return Task.FromResult(HttpResponse.Json(
                SqliteBrowser.Query(fullPath, body.Sql, maxRows, SqliteTimeout(request), request.CancellationToken, _options.Sqlite),
                StorageAgentJsonContext.Default.SqliteQueryResponse));
        }
        catch (FileNotFoundException ex)
        {
            return Task.FromResult(HttpResponse.NotFound(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(HttpResponse.Error(ex.Message));
        }
        catch (Exception ex)
        {
            return Task.FromResult(SqliteFault("Failed to run the statement", ex));
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
                SqliteBrowser.ReadRows(fullPath, table, maxRows, SqliteTimeout(request), cursor: request.QueryParams.GetValueOrDefault("cursor"),
                    cancellationToken: request.CancellationToken, options: _options.Sqlite), StorageAgentJsonContext.Default.SqliteRowsResponse));
        }
        catch (FileNotFoundException ex)
        {
            return Task.FromResult(HttpResponse.NotFound(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(HttpResponse.Error(ex.Message));
        }
        catch (Exception ex)
        {
            return Task.FromResult(SqliteFault("Failed to read the table", ex));
        }
    }

    private Task<HttpResponse> HandleSqliteInsertRow(HttpRequest request)
    {
        return WithRowRequest(request, (fullPath, body)
            => SqliteBrowser.InsertRow(fullPath, body.Table!, ToEdits(body.Values), SqliteTimeout(request),
                cancellationToken: request.CancellationToken, options: _options.Sqlite));
    }

    private Task<HttpResponse> HandleSqliteUpdateRow(HttpRequest request)
    {
        return WithRowRequest(request, (fullPath, body) =>
        {
            if (body.RowId is not { } rowId)
                throw new InvalidOperationException("Request body must include 'rowId'");

            return SqliteBrowser.UpdateRow(fullPath, body.Table!, SqliteBrowser.ParseRowId(rowId), ToEdits(body.Values), SqliteTimeout(request),
                expectedVersion: body.ExpectedVersion, cancellationToken: request.CancellationToken, options: _options.Sqlite);
        });
    }

    private Task<HttpResponse> HandleSqliteDeleteRow(HttpRequest request)
    {
        return WithRowRequest(request, (fullPath, body) =>
        {
            if (body.RowId is not { } rowId)
                throw new InvalidOperationException("Request body must include 'rowId'");

            return SqliteBrowser.DeleteRow(fullPath, body.Table!, SqliteBrowser.ParseRowId(rowId), SqliteTimeout(request),
                expectedVersion: body.ExpectedVersion, cancellationToken: request.CancellationToken, options: _options.Sqlite);
        });
    }

    /// <summary>The parts every row route shares: the file, the table name, and how failures read.</summary>
    private Task<HttpResponse> WithRowRequest(HttpRequest request, Func<string, SqliteRowBody, SqliteQueryResponse> act)
    {
        try
        {
            var body = request.BodyAs(StorageAgentJsonContext.Default.SqliteRowBody);
            if (body == null || string.IsNullOrWhiteSpace(body.Table))
                return Task.FromResult(HttpResponse.Error("Request body must include 'table'"));

            var fullPath = ResolveDatabasePath(request, writing: true);
            return Task.FromResult(HttpResponse.Json(act(fullPath, body), StorageAgentJsonContext.Default.SqliteQueryResponse));
        }
        catch (FileNotFoundException ex)
        {
            return Task.FromResult(HttpResponse.NotFound(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(HttpResponse.Error(ex.Message));
        }
        catch (Exception ex)
        {
            return Task.FromResult(SqliteFault("Failed to change the record", ex));
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

            var body = request.BodyAs(StorageAgentJsonContext.Default.SqliteRequestBody);
            if (body == null || string.IsNullOrWhiteSpace(body.Path))
                return Task.FromResult(HttpResponse.Error("Request body must include 'path'"));

            var resolved = FileStoragePathResolver.Resolve(root.BasePath, body.Path);
            FileStoragePathResolver.EnsureNoReparsePointTraversal(resolved.BasePath, resolved.FullPath, includeTarget: true);

            if (File.Exists(resolved.FullPath) || Directory.Exists(resolved.FullPath))
                return Task.FromResult(HttpResponse.Error($"Something is already called {resolved.RelativePath}"));

            var directory = Path.GetDirectoryName(resolved.FullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            SqliteGateway.Create(resolved.FullPath, request.CancellationToken, _options.Sqlite, SqliteTimeout(request));

            return Task.FromResult(HttpResponse.Json(
                new SqliteCreatedResponse
                {
                    Success = true,
                    Root = root.Id,
                    Path = resolved.RelativePath,
                    Size = new FileInfo(resolved.FullPath).Length
                },
                StorageAgentJsonContext.Default.SqliteCreatedResponse));
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(HttpResponse.Error(ex.Message));
        }
        catch (Exception ex)
        {
            return Task.FromResult(SqliteFault("Failed to create the database", ex));
        }
    }

    private static IReadOnlyList<SqliteCellEdit> ToEdits(SqliteCellBody[]? cells)
        => cells?.Select(x => x is null ? throw new InvalidOperationException("A cell cannot be null.")
            : new SqliteCellEdit(x.Column, x.Value, x.Type, x.Truncated)).ToArray() ?? [];

    private static int? ParsePositiveInt(string? text)
    {
        if (text is null)
            return null;
        if (!int.TryParse(text, out var value))
            throw new InvalidOperationException("maxRows must be a 32-bit integer.");
        return value > 0 ? value : null;
    }
}
