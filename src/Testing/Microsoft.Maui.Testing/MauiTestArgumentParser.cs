using System.Text;

namespace Microsoft.Maui.Testing;

internal static class MauiTestArgumentParser
{
    public static string[] Parse(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return [];
        }

        var arguments = new List<string>();
        var current = new StringBuilder();
        char? quote = null;
        var tokenStarted = false;

        for (var index = 0; index < commandLine.Length; index++)
        {
            var character = commandLine[index];
            if (quote is not null)
            {
                if (character == quote)
                {
                    quote = null;
                }
                else if (character == '\\' &&
                    index + 1 < commandLine.Length &&
                    (commandLine[index + 1] == quote || commandLine[index + 1] == '\\'))
                {
                    current.Append(commandLine[++index]);
                }
                else
                {
                    current.Append(character);
                }

                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
                tokenStarted = true;
            }
            else if (char.IsWhiteSpace(character))
            {
                AddArgument(arguments, current, ref tokenStarted);
            }
            else
            {
                current.Append(character);
                tokenStarted = true;
            }
        }

        if (quote is not null)
        {
            throw new FormatException("The MTP argument string contains an unmatched quote.");
        }

        AddArgument(arguments, current, ref tokenStarted);
        return [.. arguments];
    }

    private static void AddArgument(
        List<string> arguments,
        StringBuilder current,
        ref bool tokenStarted)
    {
        if (!tokenStarted)
        {
            return;
        }

        arguments.Add(current.ToString());
        current.Clear();
        tokenStarted = false;
    }
}
