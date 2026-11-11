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
    internal static UsageDetails? AddUsage(ChatResponseUpdate update, UsageDetails? current)
    {
        foreach (var content in update.Contents)
        {
            if (content is UsageContent usage)
            {
                current ??= new UsageDetails();
                current.Add(usage.Details);
            }
        }

        return current;
    }
}
