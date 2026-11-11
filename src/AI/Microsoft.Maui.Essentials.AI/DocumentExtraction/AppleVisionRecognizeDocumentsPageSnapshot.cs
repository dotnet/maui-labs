using System.Text.Json;

namespace Microsoft.Maui.Essentials.AI;

internal sealed record AppleVisionRecognizeDocumentsPageSnapshot(int PageNumber, int TotalPages, JsonElement Snapshot);
