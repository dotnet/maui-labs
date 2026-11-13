namespace BespokeCollections.Core;

public sealed record NativeCellProof(long Key, long ImageKey, string Caption, MasonryRect Frame, long Epoch);

public sealed record NativeMasonrySnapshot(
    string Platform,
    string Host,
    string Layout,
    bool Connected,
    bool UpdatePending,
    int ItemCount,
    int Realized,
    int Created,
    int Binds,
    int Recycled,
    int PeakRealized,
    int GeometryCandidates,
    double Offset,
    MasonryRect Viewport,
    long DecodedBytes,
    long PeakDecodedBytes,
    int StaleResultsRejected,
    int ImageErrors,
    NativeCellProof[] Cells);

public sealed record PrototypeReport(
    string Status,
    string Platform,
    string Configuration,
    string[] Assertions,
    int WarmCreated,
    int RepeatCreated,
    NativeMasonrySnapshot Native,
    NativeMasonrySnapshot ReplacementNative,
    int PagesLoaded,
    int RetainedItems,
    int EvictedItems,
    long EncodedBytes,
    long ManagedBytes,
    string PerformanceQualification,
    Guid RunId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    bool ScrollWorkload);

public sealed record PrototypeFailure(
    string Status,
    string Error,
    NativeMasonrySnapshot? Native,
    Guid RunId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    bool ScrollWorkload);

public sealed class PrototypeCounters
{
    public int Created;
    public int Binds;
    public int Recycled;
    public int PeakRealized;
    public int Candidates;
    public int Stale;
    public int Errors;
    public long DecodedBytes;
    public long PeakDecodedBytes;

    public void Realized(int count) => PeakRealized = Math.Max(PeakRealized, count);

    public void AddDecoded(long bytes)
    {
        if (bytes < 0 || DecodedBytes + bytes > 16 * 1024 * 1024)
            throw new InvalidOperationException("The decoded thumbnail budget was exceeded.");
        DecodedBytes += bytes;
        PeakDecodedBytes = Math.Max(PeakDecodedBytes, DecodedBytes);
    }

    public void RemoveDecoded(long bytes) => DecodedBytes -= bytes;
}
