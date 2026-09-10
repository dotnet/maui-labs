using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Microsoft.Shared.Diagnostics;

internal static class Throw
{
    [return: NotNull]
    public static T IfNull<T>(
        [NotNull] T argument,
        [CallerArgumentExpression(nameof(argument))] string paramName = "")
    {
        if (argument is null)
        {
            throw new ArgumentNullException(paramName);
        }

        return argument;
    }

    [return: NotNull]
    public static string IfNullOrWhitespace(
        [NotNull] string? argument,
        [CallerArgumentExpression(nameof(argument))] string paramName = "")
    {
        if (argument is null)
        {
            throw new ArgumentNullException(paramName);
        }

        if (string.IsNullOrWhiteSpace(argument))
        {
            throw new ArgumentException("Argument is whitespace", paramName);
        }

        return argument;
    }
}
