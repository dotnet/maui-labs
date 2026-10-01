using System.Text;

namespace AIExtensions.Sample.ChatPlayground;

internal static class AtomicFile
{
    public static void WriteAllText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var stagingPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(stagingPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(stagingPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(stagingPath))
                File.Delete(stagingPath);
        }
    }
}
