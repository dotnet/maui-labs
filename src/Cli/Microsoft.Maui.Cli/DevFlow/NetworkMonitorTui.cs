using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.DataGrid;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;

namespace Microsoft.Maui.Cli.DevFlow;

/// <summary>
/// Bindable model for a network request row in the DataGrid.
/// </summary>
public sealed partial class NetworkRow
{
    [Bindable] public partial int Index { get; set; }
    [Bindable] public partial string Id { get; set; }
    [Bindable] public partial string Method { get; set; }
    [Bindable] public partial string Url { get; set; }
    [Bindable] public partial string Host { get; set; }
    [Bindable] public partial int StatusCode { get; set; }
    [Bindable] public partial string StatusText { get; set; }
    [Bindable] public partial string Duration { get; set; }
    [Bindable] public partial string Size { get; set; }
    [Bindable] public partial string Error { get; set; }
    [Bindable] public partial string ContentType { get; set; }
    [Bindable] public partial string Timestamp { get; set; }

    public NetworkRow()
    {
        Id = string.Empty;
        Method = string.Empty;
        Url = string.Empty;
        Host = string.Empty;
        StatusText = string.Empty;
        Duration = string.Empty;
        Size = string.Empty;
        Error = string.Empty;
        ContentType = string.Empty;
        Timestamp = string.Empty;
    }
}

/// <summary>
/// Full-screen split-pane TUI for monitoring network requests.
/// Left pane: request list (DataGrid). Right pane: request details.
/// Details update automatically when the selected row changes.
/// </summary>
public static class NetworkMonitorTui
{
    public static async Task RunAsync(string host, int port, string? filterHost, string? filterMethod)
    {
        var wsUrl = $"ws://{host}:{port}/ws/v1/network";
        var status = new State<string?>("Connecting...");
        var detailText = new State<string?>("Select a request to view details.");
        var headerText = new State<string?>($"🌐 Network Monitor — {host}:{port}  [0 requests]");

        var rows = new List<NetworkRow>();
        int lastLoadedRow = -1;
        string? lastLoadedId = null;
        CancellationTokenSource? detailCts = null;

        // DataGrid document
        var doc = new DataGridListDocument<NetworkRow>();
        using (doc.BeginUpdate())
        {
            doc.AddColumn(new DataGridColumnInfo<int>("index", "#", ReadOnly: true, NetworkRow.Accessor.Index));
            doc.AddColumn(new DataGridColumnInfo<string>("method", "Method", ReadOnly: true, NetworkRow.Accessor.Method));
            doc.AddColumn(new DataGridColumnInfo<string>("url", "URL", ReadOnly: true, NetworkRow.Accessor.Url));
            doc.AddColumn(new DataGridColumnInfo<int>("statusCode", "Status", ReadOnly: true, NetworkRow.Accessor.StatusCode));
            doc.AddColumn(new DataGridColumnInfo<string>("duration", "Duration", ReadOnly: true, NetworkRow.Accessor.Duration));
            doc.AddColumn(new DataGridColumnInfo<string>("size", "Size", ReadOnly: true, NetworkRow.Accessor.Size));
        }

        using var view = new DataGridDocumentView(doc);

        var grid = new DataGridControl { View = view }
            .ShowHeader(true)
            .ShowRowAnchor(true)
            .RowAnchorWidth(1)
            .ReadOnly(true)
            .EditMode(DataGridEditMode.OnTyping)
            .SelectionMode(DataGridSelectionMode.Row)
            .FrozenColumns(0)
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Stretch);

        grid.Columns.Add(new DataGridColumn<int>
        {
            Key = "index",
            TypedValueAccessor = NetworkRow.Accessor.Index,
            Width = GridLength.Fixed(5),
            CellAlignment = TextAlignment.Right,
        });
        grid.Columns.Add(new DataGridColumn<string>
        {
            Key = "method",
            TypedValueAccessor = NetworkRow.Accessor.Method,
            Width = GridLength.Fixed(7),
        });
        grid.Columns.Add(new DataGridColumn<string>
        {
            Key = "url",
            TypedValueAccessor = NetworkRow.Accessor.Url,
            Width = GridLength.Star(1),
        });
        grid.Columns.Add(new DataGridColumn<int>
        {
            Key = "statusCode",
            TypedValueAccessor = NetworkRow.Accessor.StatusCode,
            Width = GridLength.Fixed(7),
            CellAlignment = TextAlignment.Right,
        });
        grid.Columns.Add(new DataGridColumn<string>
        {
            Key = "duration",
            TypedValueAccessor = NetworkRow.Accessor.Duration,
            Width = GridLength.Fixed(10),
            CellAlignment = TextAlignment.Right,
        });
        grid.Columns.Add(new DataGridColumn<string>
        {
            Key = "size",
            TypedValueAccessor = NetworkRow.Accessor.Size,
            Width = GridLength.Fixed(10),
            CellAlignment = TextAlignment.Right,
        });

        // Detail pane
        var detailArea = new TextArea(detailText)
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Stretch);

        // Header
        var header = new TextBlock(headerText) { Wrap = false };

        // Footer
        var footer = new Footer
        {
            Left = new TextBlock(status),
            Right = new Markup("[dim]↑↓[/]: Navigate  [dim]Ctrl+Q[/]: Quit") { Wrap = false },
        };

        // Split pane: grid on left, details on right
        var splitter = new HSplitter(
            grid,
            new VStack(
                new Markup("[bold]Details[/]") { Wrap = false },
                new Rule(),
                new ScrollViewer(detailArea)
                    .HorizontalAlignment(Align.Stretch)
                    .VerticalAlignment(Align.Stretch)
            ).Spacing(0).HorizontalAlignment(Align.Stretch).VerticalAlignment(Align.Stretch)
        ).Ratio(0.55).MinFirst(30).MinSecond(25).BarSize(1)
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Stretch);

        var root = new DockLayout()
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Stretch)
            .Top(new VStack(header, new Rule()).Spacing(0))
            .Content(splitter)
            .Bottom(footer);

        TerminalApp? app = null;

        void CheckSelectionChanged()
        {
            if (app == null) return;
            var row = grid.CurrentCell.Row;
            if (row < 0 || row >= rows.Count) return;
            var id = rows[row].Id;
            if (string.IsNullOrEmpty(id)) return;
            if (row == lastLoadedRow && id == lastLoadedId) return;

            lastLoadedRow = row;
            lastLoadedId = id;

            var r = rows[row];
            var summary = new StringBuilder();
            summary.AppendLine($"Method:      {r.Method}");
            summary.AppendLine($"URL:         {r.Url}");
            summary.AppendLine($"Status:      {(r.StatusCode > 0 ? r.StatusCode.ToString() : "---")}");
            summary.AppendLine($"Duration:    {r.Duration}");
            summary.AppendLine($"Size:        {r.Size}");
            if (!string.IsNullOrEmpty(r.Error))
                summary.AppendLine($"Error:       {r.Error}");
            summary.AppendLine();
            summary.AppendLine("Loading full details...");
            detailText.Value = summary.ToString();

            detailCts?.Cancel();
            var cts = new CancellationTokenSource();
            detailCts = cts;

            var currentApp = app;
            Task.Run(async () =>
            {
                try
                {
                    await LoadDetailAsync(host, port, id, detailText, status, currentApp, cts.Token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    currentApp.Post(() => status.Value = $"Error: {ex.Message}");
                }
            });
        }

        grid.KeyDown((_, _) => CheckSelectionChanged());
        grid.PointerPressedRouted += (_, _) => CheckSelectionChanged();

        using var session = Terminal.Open();
        app = new TerminalApp(root, session.Instance);
        await using (app)
        {
            var cts = new CancellationTokenSource();
            var wsTask = Task.Run(() => WebSocketLoop(wsUrl, doc, rows, filterHost, filterMethod, host, port, headerText, status, app, cts.Token));

            app.Run(cts.Token);

            cts.Cancel();
            detailCts?.Cancel();
            try { await wsTask; } catch { }
        }
    }

    private static async Task LoadDetailAsync(
        string host, int port,
        string id,
        State<string?> detailText,
        State<string?> status,
        TerminalApp app,
        CancellationToken ct)
    {
        using var client = new Microsoft.Maui.DevFlow.Driver.AgentClient(host, port);
        var detail = await client.GetNetworkRequestDetailAsync(id);
        ct.ThrowIfCancellationRequested();

        if (detail == null)
        {
            app.Post(() => detailText.Value = "Request not found.");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"ID:          {detail.Id}");
        sb.AppendLine($"Timestamp:   {detail.Timestamp}");
        sb.AppendLine($"Method:      {detail.Method}");
        sb.AppendLine($"URL:         {detail.Url}");

        if (!string.IsNullOrEmpty(detail.Error))
            sb.AppendLine($"Error:       {detail.Error}");
        else
            sb.AppendLine($"Status:      {detail.StatusCode} {detail.StatusText}");

        sb.AppendLine($"Duration:    {detail.DurationMs}ms");
        sb.AppendLine();

        if (detail.RequestHeaders is { Count: > 0 })
        {
            sb.AppendLine("── Request Headers ──");
            foreach (var h in detail.RequestHeaders)
                sb.AppendLine($"  {h.Key}: {string.Join(", ", h.Value)}");
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(detail.RequestBody))
        {
            sb.AppendLine($"── Request Body ({detail.RequestSize} bytes){(detail.RequestBodyTruncated ? " [truncated]" : "")} ──");
            sb.AppendLine(TryFormatJson(detail.RequestBody));
            sb.AppendLine();
        }

        if (detail.ResponseHeaders is { Count: > 0 })
        {
            sb.AppendLine("── Response Headers ──");
            foreach (var h in detail.ResponseHeaders)
                sb.AppendLine($"  {h.Key}: {string.Join(", ", h.Value)}");
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(detail.ResponseBody))
        {
            sb.AppendLine($"── Response Body ({detail.ResponseSize} bytes){(detail.ResponseBodyTruncated ? " [truncated]" : "")} ──");
            sb.AppendLine(TryFormatJson(detail.ResponseBody));
        }

        ct.ThrowIfCancellationRequested();
        var text = sb.ToString();
        app.Post(() => detailText.Value = text);
    }

    private static string TryFormatJson(string text)
    {
        try
        {
            return CliJson.PrettyPrint(text);
        }
        catch { return text; }
    }

    private static async Task WebSocketLoop(
        string wsUrl,
        DataGridListDocument<NetworkRow> doc,
        List<NetworkRow> rows,
        string? filterHost,
        string? filterMethod,
        string agentHost,
        int agentPort,
        State<string?> headerText,
        State<string?> status,
        TerminalApp app,
        CancellationToken ct)
    {
        int counter = 0;

        void UpdateHeader() => headerText.Value = $"🌐 Network Monitor — {agentHost}:{agentPort}  [{rows.Count} requests]";

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(wsUrl), ct);
                app.Post(() => status.Value = "Listening...");

                var buffer = new byte[65536];
                var sb = new StringBuilder();

                while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result;
                    sb.Clear();
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close) break;
                    if (ct.IsCancellationRequested) break;

                    var msg = sb.ToString();
                    if (string.IsNullOrEmpty(msg)) continue;
                    try
                    {
                        using var jsonDoc = JsonDocument.Parse(msg);
                        var type = jsonDoc.RootElement.GetProperty("type").GetString();

                        if (type == "replay" && jsonDoc.RootElement.TryGetProperty("entries", out var entries))
                        {
                            var newRows = new List<NetworkRow>();
                            foreach (var entry in entries.EnumerateArray())
                            {
                                if (!MatchesFilter(entry, filterHost, filterMethod)) continue;
                                counter++;
                                newRows.Add(CreateRow(counter, entry));
                            }
                            app.Post(() =>
                            {
                                using (doc.BeginUpdate())
                                {
                                    foreach (var r in newRows)
                                    {
                                        doc.AddRow(r);
                                        rows.Add(r);
                                    }
                                }
                                UpdateHeader();
                            });
                        }
                        else if (type == "request" && jsonDoc.RootElement.TryGetProperty("entry", out var reqEntry))
                        {
                            if (!MatchesFilter(reqEntry, filterHost, filterMethod)) continue;
                            counter++;
                            var row = CreateRow(counter, reqEntry);
                            app.Post(() =>
                            {
                                using (doc.BeginUpdate())
                                {
                                    doc.InsertRow(0, row);
                                    rows.Insert(0, row);
                                }
                                UpdateHeader();
                            });
                        }
                    }
                    catch (JsonException) { }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (WebSocketException)
            {
                if (ct.IsCancellationRequested) break;
                app.Post(() => status.Value = "Reconnecting...");
                try { await Task.Delay(1000, ct); } catch { break; }
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) break;
                var errorMsg = ex.Message;
                app.Post(() => status.Value = $"Error: {errorMsg}");
                try { await Task.Delay(2000, ct); } catch { break; }
            }
        }
    }

    private static NetworkRow CreateRow(int index, JsonElement entry)
    {
        var statusCode = entry.TryGetProperty("statusCode", out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetInt32() : 0;
        var durationMs = entry.TryGetProperty("durationMs", out var dm) && dm.ValueKind == JsonValueKind.Number ? dm.GetInt64() : 0;
        var responseSize = entry.TryGetProperty("responseSize", out var rs) && rs.ValueKind == JsonValueKind.Number ? rs.GetInt64() : 0;
        var error = entry.TryGetProperty("error", out var er) && er.ValueKind == JsonValueKind.String ? er.GetString() ?? "" : "";

        string statusText;
        if (!string.IsNullOrEmpty(error))
            statusText = "ERR";
        else if (statusCode > 0)
            statusText = statusCode.ToString();
        else
            statusText = "---";

        return new NetworkRow
        {
            Index = index,
            Id = entry.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            Method = entry.TryGetProperty("method", out var m) ? m.GetString() ?? "" : "",
            Url = entry.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
            Host = entry.TryGetProperty("host", out var h) ? h.GetString() ?? "" : "",
            StatusCode = statusCode,
            StatusText = statusText,
            Duration = $"{durationMs}ms",
            Size = FormatSize(responseSize),
            Error = error,
            ContentType = entry.TryGetProperty("responseContentType", out var ct) ? ct.GetString() ?? "" : "",
            Timestamp = entry.TryGetProperty("timestamp", out var ts) ? ts.GetString() ?? "" : "",
        };
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "--";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    private static bool MatchesFilter(JsonElement entry, string? filterHost, string? filterMethod)
    {
        if (filterHost != null)
        {
            var h = entry.TryGetProperty("host", out var hv) ? hv.GetString() : null;
            if (h == null || !h.Contains(filterHost, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (filterMethod != null)
        {
            var m = entry.TryGetProperty("method", out var mv) ? mv.GetString() : null;
            if (!string.Equals(m, filterMethod, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
}
