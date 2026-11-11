namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Saves the shared notes in a plain text file next to the saved conversation.</summary>
/// <param name="path">The file the notes are saved in.</param>
public sealed class NotesFile(string path)
{
    /// <summary>Gets the file the notes are saved in.</summary>
    public string FilePath { get; } = path;

    /// <summary>Reads the saved notes, or returns an empty string when there are none.</summary>
    public string Read() => File.Exists(FilePath) ? File.ReadAllText(FilePath) : string.Empty;

    /// <summary>Saves the notes; empty notes remove the file.</summary>
    /// <param name="text">The notes.</param>
    public void Write(string text)
    {
        if (!string.IsNullOrEmpty(text))
            AtomicFile.WriteAllText(FilePath, text);
        else if (File.Exists(FilePath))
            File.Delete(FilePath);
    }
}
