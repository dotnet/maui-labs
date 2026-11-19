namespace AndroidX.Compose;

/// <summary>
/// Deterministic slot-table key derived from a <c>[CallerLineNumber]</c>
/// / <c>[CallerFilePath]</c> pair. Used by every group/lambda key the
/// facade hands to Compose so the same call site produces the same
/// <c>int</c> in every process — a hard requirement for
/// <see cref="ComposeExtensions.RememberSaveable{T}(Func{T}, int, string)"/>,
/// whose <c>SaveableStateRegistry</c> key is built from the full chain
/// of ancestor group keys leading to the call.
/// </summary>
/// <remarks>
/// <see cref="HashCode.Combine{T1,T2}(T1,T2)"/> and
/// <see cref="string.GetHashCode()"/> are randomized per process on
/// modern .NET, so they can't be used here — the saved key embedded
/// in <c>onSaveInstanceState</c> would never match the key recomputed
/// after the activity is recreated, and every <c>rememberSaveable</c>
/// slot would silently reinitialise on restore. This helper uses
/// FNV-1a 32-bit over the UTF-16 code units of the path, then mixes
/// in the line number, giving a stable identifier with the same
/// collision footprint as a regular hash.
/// </remarks>
internal static class SourceLocationKey
{
    const uint FnvOffset = 2166136261u;
    const uint FnvPrime  = 16777619u;

    public static int Compute(int line, string file)
    {
        uint hash = FnvOffset;
        if (file is not null)
        {
            for (int i = 0; i < file.Length; i++)
            {
                hash ^= file[i];
                hash *= FnvPrime;
            }
        }
        // Mix the line number in by XORing the full 32-bit value and
        // running one more FNV step, so distinct lines in the same
        // file land in distinct buckets.
        hash ^= (uint)line;
        hash *= FnvPrime;
        return unchecked((int)hash);
    }
}
