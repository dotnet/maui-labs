using System;
using System.Collections.Generic;

namespace AndroidX.Compose.SourceGenerators;

/// <summary>
/// JNI method-signature parser.
/// Produces a list of <see cref="JniType"/> entries for the parameter
/// section between the leading <c>(</c> and trailing <c>)</c>.
/// </summary>
internal static class JniSignature
{
    /// <summary>
    /// Try to parse <paramref name="signature"/>'s parameter list. Returns
    /// <c>true</c> on success and writes the parsed parameter types and
    /// return-type token to the out parameters; on failure writes a
    /// human-readable reason to <paramref name="error"/>.
    /// </summary>
    public static bool TryParse(string signature, out IReadOnlyList<JniType> parameters, out string returnType, out string? error)
    {
        parameters = Array.Empty<JniType>();
        returnType = string.Empty;
        error = null;

        if (string.IsNullOrEmpty(signature) || signature[0] != '(')
        {
            error = "signature must start with '('";
            return false;
        }

        var list = new List<JniType>();
        int i = 1;
        while (i < signature.Length && signature[i] != ')')
        {
            if (!TryReadOne(signature, ref i, out var type, out error))
                return false;
            list.Add(type);
        }

        if (i >= signature.Length || signature[i] != ')')
        {
            error = "missing ')' terminator";
            return false;
        }
        i++;
        if (i >= signature.Length)
        {
            error = "missing return type";
            return false;
        }

        returnType = signature.Substring(i);
        parameters = list;
        return true;
    }

    static bool TryReadOne(string s, ref int i, out JniType type, out string? error)
    {
        type = default;
        error = null;
        int arrayDepth = 0;
        while (i < s.Length && s[i] == '[')
        {
            arrayDepth++;
            i++;
        }
        if (i >= s.Length)
        {
            error = "ran off end while reading type";
            return false;
        }
        char c = s[i];
        switch (c)
        {
            case 'Z': case 'B': case 'C': case 'S': case 'I': case 'J': case 'F': case 'D':
                i++;
                type = new JniType(c, null, arrayDepth);
                return true;
            case 'L':
                int end = s.IndexOf(';', i + 1);
                if (end < 0)
                {
                    error = "object type missing ';' terminator";
                    return false;
                }
                var name = s.Substring(i + 1, end - i - 1);
                i = end + 1;
                type = new JniType('L', name, arrayDepth);
                return true;
            default:
                error = $"unexpected JNI type code '{c}'";
                return false;
        }
    }
}

/// <summary>One parameter slot from a parsed JNI signature.</summary>
internal readonly struct JniType
{
    public JniType(char code, string? className, int arrayDepth)
    {
        Code = code;
        ClassName = className;
        ArrayDepth = arrayDepth;
    }

    /// <summary>One of <c>Z B C S I J F D L</c>.</summary>
    public char Code { get; }

    /// <summary>For <c>L</c> types only: the JNI internal class name (no leading <c>L</c>, no trailing <c>;</c>).</summary>
    public string? ClassName { get; }

    /// <summary>0 for plain types, &gt;0 for arrays.</summary>
    public int ArrayDepth { get; }

    public bool IsObject => Code == 'L' || ArrayDepth > 0;

    /// <summary>The C# zero-literal to pass when this slot is being defaulted.</summary>
    public string ZeroLiteral => ArrayDepth > 0
        ? "global::System.IntPtr.Zero"
        : Code switch
        {
            'Z' => "false",
            'B' or 'C' or 'S' or 'I' => "0",
            'J' => "0L",
            'F' => "0f",
            'D' => "0d",
            'L' => "global::System.IntPtr.Zero",
            _ => "default",
        };
}
