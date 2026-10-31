// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.AI.Chat.Recording;

namespace Microsoft.Maui.AI.Chat.Tests.TestHelpers;

internal static class RecordingLoader
{
    internal static ChatRecording Load(string recordingFileName)
    {
        var basePath = GetPath(recordingFileName);
        if (!File.Exists(basePath))
        {
            throw new FileNotFoundException(
                $"Recording baseline not found: {basePath}. " +
                "Ensure the file is copied to output (CopyToOutputDirectory = PreserveNewest).");
        }

        return ChatRecordingStore.Load(basePath);
    }

    internal static ReplayChatClient CreateReplayClient(
        string recordingFileName) =>
        new(new ChatRecordingOptions
        {
            Mode = ChatRecordingMode.Replay,
            FixturePath = GetPath(recordingFileName),
        });

    private static string GetPath(string recordingFileName) =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Baselines",
            recordingFileName);
}
