namespace Microsoft.Maui.Essentials.AI;

/// <summary>Describes progress reported while downloading Gemini Nano model assets.</summary>
public sealed class GeminiNanoDownloadUpdate
{
	internal GeminiNanoDownloadUpdate(GeminiNanoDownloadState state, long bytes)
	{
		State = state;
		BytesToDownload = state == GeminiNanoDownloadState.Started ? bytes : null;
		BytesDownloaded = state == GeminiNanoDownloadState.Progress ? bytes : null;
	}

	/// <summary>Gets the current download state.</summary>
	public GeminiNanoDownloadState State { get; }

	/// <summary>Gets the total download size when <see cref="State"/> is <see cref="GeminiNanoDownloadState.Started"/>.</summary>
	public long? BytesToDownload { get; }

	/// <summary>Gets the downloaded byte count when <see cref="State"/> is <see cref="GeminiNanoDownloadState.Progress"/>.</summary>
	public long? BytesDownloaded { get; }
}

/// <summary>Identifies the kind of Gemini Nano download update.</summary>
public enum GeminiNanoDownloadState
{
	/// <summary>The download started.</summary>
	Started,

	/// <summary>The download advanced.</summary>
	Progress,

	/// <summary>The download completed.</summary>
	Completed,
}
