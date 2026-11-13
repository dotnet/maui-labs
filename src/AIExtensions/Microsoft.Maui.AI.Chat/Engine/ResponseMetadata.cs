// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>Collects the model and usage a turn's response updates report.</summary>
internal static class ResponseMetadata
{
    internal static string? GetModelId(ChatResponseUpdate update, string? current)
        => string.IsNullOrEmpty(update.ModelId) ? current : update.ModelId;

    /// <summary>Adds the update's usage to <paramref name="current"/>, as <c>ToChatResponse</c> does.</summary>
    /// <returns>
    /// A new <see cref="UsageDetails"/> when the update reports usage, so a turn reports the change, or
    /// <paramref name="current"/> when it does not.
    /// </returns>
    internal static UsageDetails? AddUsage(ChatResponseUpdate update, UsageDetails? current)
    {
        UsageDetails? updated = null;
        foreach (var content in update.Contents)
        {
            if (content is UsageContent usage)
            {
                if (updated is null)
                {
                    updated = new UsageDetails();
                    if (current is not null)
                        updated.Add(current);
                }

                updated.Add(usage.Details);
            }
        }

        return updated ?? current;
    }
}
