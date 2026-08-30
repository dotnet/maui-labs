using System.Text;

namespace DevFlow.Sample;

/// <summary>
/// Durable record of the todo mutations the app has actually committed.
/// </summary>
/// <remarks>
/// <para>
/// The ledger lives in app-private storage and is never read back by any page, so what it holds
/// is decided by the domain layer alone. That is what makes it usable as an independent business
/// oracle: a DevFlow flow drives the UI and asserts on rendered text, while a reader outside the
/// automation channel opens this file and checks what the app committed. A view model that
/// renders "4 items" without ever committing the add would satisfy the UI assertion and fail the
/// ledger check.
/// </para>
/// <para>
/// Each entry is one line of JSON. The whole file is rewritten after every committed mutation, so
/// the file always mirrors the current entry list even if an earlier write could not be performed.
/// </para>
/// </remarks>
public sealed class TodoLedger
{
    public const string FileName = "todo-ledger.jsonl";

    readonly List<string> _entries = [];
    readonly Lock _gate = new();

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            Write();
        }
    }

    public void RecordAdded(TodoItem item) => Record("todo-added", item);

    public void RecordRemoved(TodoItem item) => Record("todo-removed", item);

    void Record(string eventName, TodoItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (_gate)
        {
            _entries.Add(FormatEntry(eventName, item));
            Write();
        }
    }

    /// <summary>
    /// Formats one ledger line. The layout is fixed so an out-of-band oracle can assert on an
    /// exact record rather than on a loose substring.
    /// </summary>
    internal static string FormatEntry(string eventName, TodoItem item)
        => new StringBuilder()
            .Append("{\"event\":\"").Append(Escape(eventName))
            .Append("\",\"id\":\"").Append(Escape(item.Id))
            .Append("\",\"title\":\"").Append(Escape(item.Title))
            .Append("\",\"completed\":").Append(item.IsCompleted ? "true" : "false")
            .Append('}')
            .ToString();

    void Write()
    {
        try
        {
            var builder = new StringBuilder();
            foreach (var entry in _entries)
                builder.Append(entry).Append('\n');
            File.WriteAllText(
                Path.Combine(FileSystem.AppDataDirectory, FileName),
                builder.ToString(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            // App storage may not be resolvable yet during startup. The next committed mutation
            // rewrites the complete ledger, so a skipped write never leaves a partial file behind.
        }
    }

    static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            switch (character)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (character < ' ')
                        builder.Append("\\u").Append(((int)character).ToString("x4"));
                    else
                        builder.Append(character);
                    break;
            }
        }
        return builder.ToString();
    }
}
