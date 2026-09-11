// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Minimal local shim of `Microsoft.Shared.Diagnostics.Throw` from dotnet/extensions'
// `src/Shared/Throw/Throw.cs`, trimmed to the members actually used by the vendored
// `Microsoft.Extensions.DocumentExtraction(.Abstractions)` sources in this repo
// (`Throw.IfNull` and `Throw.IfNullOrWhitespace`). Linked into both projects; do not
// edit the copied upstream implementation files to accommodate this shim.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

#pragma warning disable CA1716
namespace Microsoft.Shared.Diagnostics;
#pragma warning restore CA1716

/// <summary>
/// Defines static methods used to throw exceptions.
/// </summary>
internal static class Throw
{
    /// <summary>
    /// Throws an <see cref="ArgumentNullException"/> if the specified argument is <see langword="null"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NotNull]
    public static T IfNull<T>([NotNull] T argument, [CallerArgumentExpression(nameof(argument))] string paramName = "")
    {
        if (argument is null)
        {
            ArgumentNullException(paramName);
        }

        return argument;
    }

    /// <summary>
    /// Throws either an <see cref="ArgumentNullException"/> or an <see cref="ArgumentException"/>
    /// if the specified string is <see langword="null"/> or whitespace respectively.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NotNull]
    public static string IfNullOrWhitespace([NotNull] string? argument, [CallerArgumentExpression(nameof(argument))] string paramName = "")
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            if (argument == null)
            {
                ArgumentNullException(paramName);
            }
            else
            {
                ArgumentException(paramName, "Argument is whitespace");
            }
        }

        return argument;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [DoesNotReturn]
    private static void ArgumentNullException(string paramName)
        => throw new ArgumentNullException(paramName);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [DoesNotReturn]
    private static void ArgumentException(string paramName, string? message)
        => throw new ArgumentException(message, paramName);
}
