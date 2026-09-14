using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace Microsoft.Maui.AI.Chat.Recording;

public enum ChatRecordingMode { Live, Record, Replay }
public enum ReplayTiming { None, Recorded }

/// <summary>Configures deterministic recording and replay at the <see cref="IChatClient"/> boundary.</summary>
public sealed class ChatRecordingOptions
{
    public ChatRecordingMode Mode { get; set; } = ChatRecordingMode.Live;
    public string? FixturePath { get; set; }
    public Stream? FixtureStream { get; set; }
    public ChatRecording? Recording { get; set; }
    public bool StrictSanitizer { get; set; } = true;
    /// <summary>Requires request matching to include the otherwise volatile message ID.</summary>
    public bool RequireMessageId { get; set; }
    public bool AllowAguiThreadId { get; set; }
    public int InlineDataThresholdBytes { get; set; } = 64 * 1024;
    public ReplayTiming ReplayTiming { get; set; }
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
    public double TimingScale { get; set; } = 1;
    public TimeSpan? MaximumReplayDelay { get; set; }
    public string? Adapter { get; set; }
    public IList<IChatRecordingContentCodec> ContentCodecs { get; } = new List<IChatRecordingContentCodec>();
    public IList<IChatRecordingRawCodec> RawCodecs { get; } = new List<IChatRecordingRawCodec>();
}

public sealed class ChatRecording
{
    public string Format { get; set; } = "maui-ai-chat-recording";
    public int SchemaVersion { get; set; } = 1;
    public JsonObject Metadata { get; set; } = new()
    {
        ["meaiVersion"] = typeof(IChatClient).Assembly.GetName().Version?.ToString(),
        ["pipelineBoundary"] = "provider",
        ["sanitizerProfile"] = "strict-v1",
    };
    public List<RecordedChatInteraction> Interactions { get; set; } = [];
    public JsonObject Manifest { get; set; } = new() { ["redacted"] = 0, ["rejected"] = 0 };
    [System.Text.Json.Serialization.JsonIgnore]
    internal string? FixtureDirectory { get; set; }
}

public sealed class RecordedChatInteraction
{
    public long Sequence { get; set; }
    public string? Name { get; set; }
    public JsonObject Request { get; set; } = new();
    public List<RecordedChatUpdate> Updates { get; set; } = [];
    public string Outcome { get; set; } = "completed";
    public string? ErrorType { get; set; }
    public string? ErrorMessage { get; set; }
    public bool Legacy { get; set; }
}

public sealed class RecordedChatUpdate
{
    public long Sequence { get; set; }
    public JsonObject Value { get; set; } = new();
    public long? DelayMs { get; set; }
}

public interface IChatRecordingContentCodec
{
    bool CanWrite(AIContent content);
    JsonObject Write(AIContent content);
    bool CanRead(string type);
    AIContent Read(JsonObject value);
}

public interface IChatRecordingRawCodec
{
    bool CanWrite(object value);
    JsonNode? Write(object value);
    bool CanRead(string type);
    object? Read(string type, JsonNode? value);
}

public sealed class ChatRecordingMismatchException : InvalidOperationException
{
    public ChatRecordingMismatchException(int interaction, string path, JsonNode? expected, JsonNode? actual)
        : base($"Recording interaction {interaction} mismatched at {path}. Expected {expected?.ToJsonString() ?? "null"}; actual {actual?.ToJsonString() ?? "null"}.")
    {
        Interaction = interaction; Path = path;
    }
    public int Interaction { get; }
    public string Path { get; }
}

public sealed class RecordedChatException : Exception
{
    public RecordedChatException(int interaction, string? recordedType, string? message)
        : base($"Recorded chat interaction {interaction} failed ({recordedType ?? "Exception"}): {message}") =>
        (Interaction, RecordedType) = (interaction, recordedType);
    public int Interaction { get; }
    public string? RecordedType { get; }
}

/// <summary>Owns a tape cursor and supports naming turns and checking for unused interactions.</summary>
public sealed class ChatRecordingSession
{
    private int _next;
    internal string? CurrentTurn { get; private set; }
    public ChatRecordingSession(ChatRecording? recording = null) => Recording = recording ?? new ChatRecording();
    public ChatRecording Recording { get; }
    public void BeginTurn(string? name = null) => CurrentTurn = name;
    internal int Reserve() => _next++;
    public void AssertFullyReplayed()
    {
        if (_next != Recording.Interactions.Count)
            throw new InvalidOperationException($"Recording has {Recording.Interactions.Count - _next} unconsumed interaction(s).");
    }
}

public static class ChatRecordingStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static ChatRecording Load(string path)
    {
        using var stream = File.OpenRead(path);
        var recording = Load(stream);
        recording.FixtureDirectory = Path.GetDirectoryName(Path.GetFullPath(path));
        return recording;
    }
    public static ChatRecording Load(Stream stream)
    {
        var node = JsonNode.Parse(stream) ?? throw new InvalidDataException("Recording is empty.");
        if (node is JsonArray legacy)
            return LoadLegacy(legacy);
        return DeserializeCurrent(node);
    }
    public static async Task<ChatRecording> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var recording = await LoadAsync(stream, cancellationToken).ConfigureAwait(false);
        recording.FixtureDirectory = Path.GetDirectoryName(Path.GetFullPath(path));
        return recording;
    }
    public static async Task<ChatRecording> LoadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Recording is empty.");
        if (node is JsonArray array)
            return LoadLegacy(array);
        return DeserializeCurrent(node);
    }
    public static void Save(ChatRecording recording, string path)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ValidateSchema(recording);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        WriteBlobs(recording, path);
        using var stream = File.Create(path);
        Save(recording, stream);
    }
    public static void Save(ChatRecording recording, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ValidateSchema(recording);
        JsonSerializer.Serialize(stream, recording, JsonOptions);
    }
    public static async Task SaveAsync(ChatRecording recording, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ValidateSchema(recording);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        WriteBlobs(recording, path);
        await using var stream = File.Create(path);
        await SaveAsync(recording, stream, cancellationToken).ConfigureAwait(false);
    }
    public static Task SaveAsync(ChatRecording recording, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ValidateSchema(recording);
        return JsonSerializer.SerializeAsync(stream, recording, JsonOptions, cancellationToken);
    }

    private static ChatRecording LoadLegacy(JsonArray arrays)
    {
        var recording = new ChatRecording();
        foreach (var item in arrays)
        {
            var interaction = new RecordedChatInteraction { Sequence = recording.Interactions.Count, Legacy = true, Name = $"legacy-{recording.Interactions.Count}", Request = new JsonObject { ["legacy"] = true } };
            if (item is JsonArray updates)
                foreach (var update in updates)
                    interaction.Updates.Add(new RecordedChatUpdate { Sequence = interaction.Updates.Count, Value = update?.AsObject() ?? new JsonObject() });
            recording.Interactions.Add(interaction);
        }
        return recording;
    }

    private static ChatRecording DeserializeCurrent(JsonNode node)
    {
        var obj = node as JsonObject ?? throw new InvalidDataException("Invalid recording.");
        if (!obj.Any(pair => string.Equals(pair.Key, "metadata", StringComparison.OrdinalIgnoreCase)) ||
            !obj.Any(pair => string.Equals(pair.Key, "manifest", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Recording v1 requires metadata and manifest.");
        var result = obj.Deserialize<ChatRecording>(JsonOptions) ?? throw new InvalidDataException("Invalid recording.");
        ValidateSchema(result);
        return result;
    }

    private static void WriteBlobs(ChatRecording recording, string path)
    {
        var threshold = recording.Metadata["inlineDataThresholdBytes"]?.GetValue<int>() ?? int.MaxValue;
        var root = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var directoryName = Path.GetFileName(path) + ".blobs";
        foreach (var interaction in recording.Interactions)
        {
            ReplaceLargeData(interaction.Request, root, directoryName, threshold);
            foreach (var update in interaction.Updates)
                ReplaceLargeData(update.Value, root, directoryName, threshold);
        }
        recording.FixtureDirectory = root;
    }

    private static void ReplaceLargeData(JsonNode? node, string root, string directoryName, int threshold)
    {
        if (node is JsonObject obj)
        {
            if (obj["type"]?.GetValue<string>() == "data" && obj["data"]?.GetValue<string>() is { } encoded)
            {
                var bytes = Convert.FromBase64String(encoded);
                if (bytes.Length > threshold)
                {
                    var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                    var relative = Path.Combine(directoryName, hash).Replace(Path.DirectorySeparatorChar, '/');
                    var fullPath = Path.Combine(root, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                    if (!File.Exists(fullPath)) File.WriteAllBytes(fullPath, bytes);
                    obj.Remove("data");
                    obj["blob"] = new JsonObject { ["sha256"] = hash, ["path"] = relative, ["length"] = bytes.Length, ["mediaType"] = obj["mediaType"]?.DeepClone() };
                }

            }
            foreach (var item in obj.ToList()) ReplaceLargeData(item.Value, root, directoryName, threshold);
        }
        else if (node is JsonArray array)
            foreach (var item in array) ReplaceLargeData(item, root, directoryName, threshold);
    }

    internal static void ValidateSchema(ChatRecording recording)
    {
        if (recording.Format != "maui-ai-chat-recording" || recording.SchemaVersion != 1)
            throw new InvalidDataException("Unsupported chat recording schema. Only v1 recordings can be saved or loaded.");
        if (recording.Metadata is null || recording.Manifest is null)
            throw new InvalidDataException("Recording v1 requires metadata and manifest.");
        for (var i = 0; i < recording.Interactions.Count; i++)
        {
            var interaction = recording.Interactions[i];
            if (interaction.Sequence != i)
                throw new InvalidDataException($"Interaction sequence {interaction.Sequence} is out of order; expected {i}.");
            for (var j = 0; j < interaction.Updates.Count; j++)
                if (interaction.Updates[j].Sequence != j)
                    throw new InvalidDataException($"Interaction {i} update sequence {interaction.Updates[j].Sequence} is out of order; expected {j}.");
        }
    }
}

/// <summary>Records an inner client while preserving its service lookup and disposal behavior.</summary>
public sealed class RecordingChatClient : DelegatingChatClient
{
    private readonly ChatRecordingOptions _options;
    private readonly ChatRecordingSession _session;
    public RecordingChatClient(IChatClient innerClient, ChatRecordingOptions options, ChatRecordingSession? session = null) : base(innerClient)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _session = session ?? new ChatRecordingSession(options.Recording);
        _options.Recording = _session.Recording;
        if (!string.IsNullOrWhiteSpace(options.Adapter)) _session.Recording.Metadata["adapter"] = options.Adapter;
        _session.Recording.Metadata["inlineDataThresholdBytes"] = options.InlineDataThresholdBytes;
    }
    public ChatRecordingSession Session => _session;
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in GetStreamingResponseAsync(messages, options, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            updates.Add(update);
        return await updates.ToAsyncEnumerable().ToChatResponseAsync(cancellationToken).ConfigureAwait(false);
    }
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_options.Mode != ChatRecordingMode.Record)
        {
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
                yield return update;
            yield break;
        }
        var interaction = new RecordedChatInteraction
        {
            Sequence = _session.Recording.Interactions.Count,
            Name = _session.CurrentTurn,
            Request = ChatRecordingJson.Request(messages, options, _options),
        };
        _session.Recording.Interactions.Add(interaction);
        var started = _options.TimeProvider.GetTimestamp();
        var completed = false;
        await using var enumerator = base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    interaction.Outcome = "canceled";
                    throw;
                }
                catch (Exception ex)
                {
                    interaction.Outcome = "error";
                    interaction.ErrorType = ex.GetType().FullName;
                    interaction.ErrorMessage = ex.Message;
                    throw;
                }
                if (!hasNext) break;
                var update = enumerator.Current;
                var delay = _options.TimeProvider.GetElapsedTime(started).TotalMilliseconds;
                interaction.Updates.Add(new RecordedChatUpdate
                {
                    Sequence = interaction.Updates.Count,
                    Value = ChatRecordingJson.Update(update, _options),
                    DelayMs = (long)Math.Max(0, delay),
                });
                started = _options.TimeProvider.GetTimestamp();
                yield return update;
            }
            interaction.Outcome = "completed"; completed = true;
        }
        finally
        {
            if (!completed && interaction.Outcome == "completed") interaction.Outcome = "abandoned";
        }
    }
}

/// <summary>Replays a recording without invoking a provider.</summary>
public sealed class ReplayChatClient : IChatClient
{
    private readonly ChatRecordingOptions _options;
    private readonly ChatRecordingSession _session;
    private bool _disposed;
    public ReplayChatClient(ChatRecordingOptions options, ChatRecordingSession? session = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        var tape = options.Recording ?? (options.FixturePath is not null ? ChatRecordingStore.Load(options.FixturePath) : options.FixtureStream is not null ? ChatRecordingStore.Load(options.FixtureStream) : throw new ArgumentException("A recording, fixture path, or fixture stream is required.", nameof(options)));
        ChatRecordingStore.ValidateSchema(tape);
        if (_options.FixturePath is null && tape.FixtureDirectory is { } directory)
            _options.FixturePath = Path.Combine(directory, "recording.json");
        _session = session ?? new ChatRecordingSession(tape);
    }
    public ChatRecordingSession Session => _session;
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in GetStreamingResponseAsync(messages, options, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false)) updates.Add(update);
        return await updates.ToAsyncEnumerable().ToChatResponseAsync(cancellationToken).ConfigureAwait(false);
    }
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var index = _session.Reserve();
        if (index >= _session.Recording.Interactions.Count) throw new InvalidOperationException($"Recording ended before interaction {index}.");
        var interaction = _session.Recording.Interactions[index];
        if (!interaction.Legacy) ChatRecordingJson.AssertRequestEqual(index, interaction.Request, ChatRecordingJson.Request(messages, options, _options), _options);
        foreach (var recorded in interaction.Updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_options.ReplayTiming == ReplayTiming.Recorded && recorded.DelayMs is > 0)
            {
                var delay = TimeSpan.FromMilliseconds(recorded.DelayMs.Value * Math.Max(0, _options.TimingScale));
                if (_options.MaximumReplayDelay is { } cap && delay > cap) delay = cap;
                if (delay > TimeSpan.Zero) await Task.Delay(delay, _options.TimeProvider, cancellationToken).ConfigureAwait(false);
            }
            yield return ChatRecordingJson.ReadUpdate(recorded.Value, _options);
        }
        if (interaction.Outcome == "error") throw new RecordedChatException(index, interaction.ErrorType, interaction.ErrorMessage);
        if (interaction.Outcome is "canceled" or "abandoned") yield break;
    }
    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(IChatClient) && serviceKey is null ? this : null;
    public void Dispose() => _disposed = true;
}

internal static class ChatRecordingJson
{
    private static readonly HashSet<string> AllowedAdditional = ["agui_thread_id"];
    internal static JsonObject Request(IEnumerable<ChatMessage> messages, ChatOptions? options, ChatRecordingOptions settings)
    {
        var result = new JsonObject { ["messages"] = new JsonArray(messages.Select(x => Message(x, settings)).ToArray()), ["options"] = Options(options, settings) };
        return Sanitize(result, settings);
    }
    private static JsonObject Message(ChatMessage message, ChatRecordingOptions settings)
    {
        var result = new JsonObject
        {
            ["role"] = message.Role.Value, ["authorName"] = message.AuthorName,
            ["contents"] = new JsonArray(message.Contents.Select(x => Content(x, settings)).ToArray()), ["raw"] = Raw(message.RawRepresentation, settings),
            ["additional"] = Additional(message.AdditionalProperties, settings),
        };
        if (settings.RequireMessageId)
            result["messageId"] = message.MessageId;
        return result;
    }
    private static JsonObject Options(ChatOptions? o, ChatRecordingOptions settings)
    {
        if (o is null) return new();
        if (o.RawRepresentationFactory is not null) Reject(settings, "RawRepresentationFactory cannot be recorded.");
        var node = new JsonObject
        {
            ["conversationId"] = o.ConversationId, ["instructions"] = TextValue(o.Instructions, settings), ["temperature"] = o.Temperature,
            ["maxOutputTokens"] = o.MaxOutputTokens, ["topP"] = o.TopP, ["topK"] = o.TopK, ["frequencyPenalty"] = o.FrequencyPenalty,
            ["presencePenalty"] = o.PresencePenalty, ["seed"] = o.Seed, ["modelId"] = o.ModelId,
            ["allowMultipleToolCalls"] = o.AllowMultipleToolCalls, ["allowBackgroundResponses"] = o.AllowBackgroundResponses,
            ["stopSequences"] = o.StopSequences is null ? null : new JsonArray(o.StopSequences.Select(static value => JsonValue.Create(value)).ToArray()),
            ["tools"] = new JsonArray((o.Tools ?? []).Select(static tool => Tool(tool)).ToArray()), ["additional"] = Additional(o.AdditionalProperties, settings),
        };
        return node;
    }
    private static JsonObject Tool(AITool tool)
    {
        var result = new JsonObject { ["name"] = tool.Name, ["description"] = tool.Description };
        if (tool is AIFunctionDeclaration function)
        {
            result["inputSchema"] = function.JsonSchema is { } input ? JsonNode.Parse(input.GetRawText()) : null;
            result["returnSchema"] = function.ReturnJsonSchema is { } output ? JsonNode.Parse(output.GetRawText()) : null;
        }
        return result;
    }
    internal static JsonObject Update(ChatResponseUpdate u, ChatRecordingOptions settings) => Sanitize(new JsonObject
    {
        ["authorName"] = u.AuthorName, ["role"] = u.Role?.Value, ["responseId"] = u.ResponseId, ["messageId"] = u.MessageId,
        ["conversationId"] = u.ConversationId, ["createdAt"] = u.CreatedAt?.ToString("O"), ["finishReason"] = u.FinishReason?.Value,
        ["modelId"] = u.ModelId, ["contents"] = new JsonArray(u.Contents.Select(x => Content(x, settings)).ToArray()),
        ["additional"] = Additional(u.AdditionalProperties, settings), ["raw"] = Raw(u.RawRepresentation, settings),
    }, settings);
    private static JsonObject Content(AIContent content, ChatRecordingOptions settings)
    {
        JsonObject serialized;
        foreach (var codec in settings.ContentCodecs)
            if (codec.CanWrite(content))
                return Sanitize(codec.Write(content), settings);
        serialized = content switch
        {
            TextContent text => new JsonObject { ["type"] = "text", ["text"] = TextValue(text.Text, settings) },
            TextReasoningContent reasoning when reasoning.ProtectedData is null => new JsonObject { ["type"] = "reasoning", ["text"] = TextValue(reasoning.Text, settings) },
            TextReasoningContent => throw new InvalidOperationException("Protected reasoning data cannot be recorded."),
            UriContent uri => Uri(uri, settings),
            DataContent data => new JsonObject { ["type"] = "data", ["mediaType"] = data.MediaType, ["name"] = data.Name, ["data"] = Convert.ToBase64String(data.Data.Span) },
            ToolApprovalRequestContent approval => new JsonObject { ["type"] = "toolApprovalRequest", ["requestId"] = approval.RequestId, ["toolCall"] = Content(approval.ToolCall, settings) },
            ToolApprovalResponseContent approval => new JsonObject { ["type"] = "toolApprovalResponse", ["requestId"] = approval.RequestId, ["approved"] = approval.Approved, ["reason"] = approval.Reason, ["toolCall"] = Content(approval.ToolCall, settings) },
            ImageGenerationToolCallContent image => new JsonObject { ["type"] = "imageGenerationCall", ["callId"] = image.CallId },
            ImageGenerationToolResultContent image => new JsonObject { ["type"] = "imageGenerationResult", ["callId"] = image.CallId, ["outputs"] = image.Outputs is null ? null : new JsonArray(image.Outputs.Select(x => Content(x, settings)).ToArray()) },
            InputRequestContent input => new JsonObject { ["type"] = "inputRequest", ["requestId"] = input.RequestId },
            InputResponseContent input => new JsonObject { ["type"] = "inputResponse", ["requestId"] = input.RequestId },
            FunctionCallContent call => new JsonObject { ["type"] = "functionCall", ["callId"] = call.CallId, ["name"] = call.Name, ["arguments"] = SafeValue(call.Arguments, settings), ["informationalOnly"] = call.InformationalOnly },
            FunctionResultContent result => new JsonObject { ["type"] = "functionResult", ["callId"] = result.CallId, ["result"] = SafeValue(result.Result, settings) },
            ToolCallContent call => new JsonObject { ["type"] = "toolCall", ["callId"] = call.CallId },
            ToolResultContent result => new JsonObject { ["type"] = "toolResult", ["callId"] = result.CallId },
            UsageContent usage => new JsonObject { ["type"] = "usage", ["details"] = JsonSerializer.SerializeToNode(usage.Details) },
            RichTextContent rich => new JsonObject { ["type"] = "richText", ["text"] = rich.Text, ["nodes"] = new JsonArray(rich.Nodes.Select((node, index) => Node(node, $"$.nodes[{index}]")).ToArray()) },
            ErrorContent error => new JsonObject { ["type"] = "error", ["message"] = error.Message, ["code"] = error.ErrorCode, ["details"] = error.Details },
            _ => throw new NotSupportedException($"Recording does not support AI content type '{content.GetType().FullName}'. Register an {nameof(IChatRecordingContentCodec)}.")
        };
        return Sanitize(serialized, settings);
    }
    private static JsonObject? Additional(AdditionalPropertiesDictionary? additional, ChatRecordingOptions settings)
    {
        if (additional is null || additional.Count == 0) return null;
        var result = new JsonObject();
        foreach (var pair in additional)
        {
            if (settings.AllowAguiThreadId && pair.Key == "agui_thread_id") { result[pair.Key] = SafeValue(pair.Value, settings); continue; }
            Reject(settings, $"Additional property '{pair.Key}' is not permitted by strict-v1.");
        }
        return result;
    }
    private static JsonObject Uri(UriContent uri, ChatRecordingOptions settings)
    {
        if (!IsSafeUri(uri.Uri))
        {
            Reject(settings, "URI content must be an ordinary HTTP(S) URI without credentials, query, fragment, or provider endpoint.");
            return new JsonObject { ["type"] = "uri", ["uri"] = "about:blank", ["mediaType"] = uri.MediaType };
        }
        return new JsonObject { ["type"] = "uri", ["uri"] = uri.Uri.AbsoluteUri, ["mediaType"] = uri.MediaType };
    }
    private static string? TextValue(string? value, ChatRecordingOptions settings)
    {
        if (value is null) return null;
        if (!Regex.IsMatch(value, @"(?i)\b(bearer\s+\S+|api[_-]?key\s*[=:]|authorization\s*[=:]|cookie\s*[=:]|connection\s*string\s*[=:]|x-ms-[\w-]*\s*[=:]|trace(parent|state)\s*[=:])"))
            return value;
        Redact(settings, "Potential secret or trace header cannot be persisted.");
        return "[REDACTED]";
    }
    private static void Redact(ChatRecordingOptions settings, string message)
    {
        if (settings.Recording?.Manifest["redacted"]?.GetValue<int>() is { } redacted)
            settings.Recording.Manifest["redacted"] = redacted + 1;
        Reject(settings, message);
    }
    private static void Reject(ChatRecordingOptions settings, string message)
    {
        if (settings.Recording?.Manifest["rejected"]?.GetValue<int>() is { } rejected)
            settings.Recording.Manifest["rejected"] = rejected + 1;
        if (settings.StrictSanitizer) throw new InvalidOperationException(message);
    }
    private static JsonNode? Raw(object? raw, ChatRecordingOptions settings)
    {
        if (raw is null) return null;
        foreach (var codec in settings.RawCodecs)
            if (codec.CanWrite(raw))
                return new JsonObject { ["type"] = raw.GetType().FullName, ["value"] = codec.Write(raw) };
        Reject(settings, $"Raw representation '{raw.GetType().FullName}' cannot be recorded without an {nameof(IChatRecordingRawCodec)}.");
        return null;
    }
    private static object? ReadRaw(JsonNode? node, ChatRecordingOptions settings)
    {
        if (node is null) return null;
        var obj = node.AsObject();
        var type = obj["type"]?.GetValue<string>() ?? throw new InvalidDataException("Raw representation lacks type.");
        foreach (var codec in settings.RawCodecs) if (codec.CanRead(type)) return codec.Read(type, obj["value"]);
        Reject(settings, $"Recording contains raw representation '{type}' without a registered {nameof(IChatRecordingRawCodec)}.");
        return null;
    }
    private static JsonNode? SafeValue(object? value, ChatRecordingOptions settings)
    {
        switch (value)
        {
            case null: return null;
            case string text: return JsonValue.Create(TextValue(text, settings));
            case bool boolean: return JsonValue.Create(boolean);
            case byte number: return JsonValue.Create(number);
            case short number: return JsonValue.Create(number);
            case int number: return JsonValue.Create(number);
            case long number: return JsonValue.Create(number);
            case float number: return JsonValue.Create(number);
            case double number: return JsonValue.Create(number);
            case decimal number: return JsonValue.Create(number);
            case JsonElement element: return Sanitize(JsonNode.Parse(element.GetRawText()), settings);
            case JsonNode node: return Sanitize(node.DeepClone(), settings);
            case IDictionary<string, object?> dictionary:
                return Sanitize(new JsonObject(dictionary.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => SafeValue(x.Value, settings))), settings);
            case System.Collections.IEnumerable values:
                return Sanitize(new JsonArray(values.Cast<object?>().Select(x => SafeValue(x, settings)).ToArray()), settings);
            default:
                Reject(settings, $"Value of type '{value.GetType().FullName}' is not a safe canonical scalar, JSON node, dictionary, or list.");
                return null;
        }
    }
    internal static ChatResponseUpdate ReadUpdate(JsonObject node, ChatRecordingOptions settings)
    {
        var result = new ChatResponseUpdate { AuthorName = node["authorName"]?.GetValue<string>(), ResponseId = node["responseId"]?.GetValue<string>(), MessageId = node["messageId"]?.GetValue<string>(), ConversationId = node["conversationId"]?.GetValue<string>(), ModelId = node["modelId"]?.GetValue<string>(), RawRepresentation = ReadRaw(node["raw"], settings), AdditionalProperties = ReadAdditional(node["additional"], settings) };
        if (node["role"]?.GetValue<string>() is { } role) result.Role = new ChatRole(role);
        if (node["createdAt"]?.GetValue<string>() is { } time) result.CreatedAt = DateTimeOffset.Parse(time, null, System.Globalization.DateTimeStyles.RoundtripKind);
        if (node["finishReason"]?.GetValue<string>() is { } finish) result.FinishReason = new ChatFinishReason(finish);
        if (node["contents"] is JsonArray contents) foreach (var value in contents) result.Contents.Add(ReadContent(value!.AsObject(), settings));
        return result;
    }
    private static AdditionalPropertiesDictionary? ReadAdditional(JsonNode? node, ChatRecordingOptions settings)
    {
        if (node is null)
            return null;
        var result = new AdditionalPropertiesDictionary();
        foreach (var pair in node.AsObject())
        {
            if (!AllowedAdditional.Contains(pair.Key))
            {
                Reject(settings, $"Additional property '{pair.Key}' is not permitted by strict-v1.");
                continue;
            }
            result[pair.Key] = ReadSafeValue(pair.Value);
        }
        return result;
    }
    private static AIContent ReadContent(JsonObject node, ChatRecordingOptions settings)
    {
        var type = node["type"]?.GetValue<string>() ?? throw new InvalidDataException("Content lacks type.");
        foreach (var codec in settings.ContentCodecs) if (codec.CanRead(type)) return codec.Read(node);
        return type switch
        {
            "text" => new TextContent(node["text"]?.GetValue<string>()),
            "reasoning" => new TextReasoningContent(node["text"]?.GetValue<string>()),
            "uri" => new UriContent(node["uri"]!.GetValue<string>(), node["mediaType"]?.GetValue<string>()),
            "data" => new DataContent(ReadData(node, settings), node["mediaType"]!.GetValue<string>()) { Name = node["name"]?.GetValue<string>() },
            "inputRequest" => new RecordedInputRequestContent(node["requestId"]!.GetValue<string>()),
            "inputResponse" => new RecordedInputResponseContent(node["requestId"]!.GetValue<string>()),
            "functionCall" => new FunctionCallContent(node["callId"]!.GetValue<string>(), node["name"]!.GetValue<string>(), ReadSafeValue(node["arguments"]) as Dictionary<string, object?>) { InformationalOnly = node["informationalOnly"]?.GetValue<bool>() ?? false },
            "functionResult" => new FunctionResultContent(node["callId"]!.GetValue<string>(), ReadSafeValue(node["result"])),
            "toolCall" => new ToolCallContent(node["callId"]!.GetValue<string>()),
            "toolResult" => new ToolResultContent(node["callId"]!.GetValue<string>()),
            "toolApprovalRequest" => new ToolApprovalRequestContent(node["requestId"]!.GetValue<string>(), (ToolCallContent)ReadContent(node["toolCall"]!.AsObject(), settings)),
            "toolApprovalResponse" => new ToolApprovalResponseContent(node["requestId"]!.GetValue<string>(), node["approved"]!.GetValue<bool>(), (ToolCallContent)ReadContent(node["toolCall"]!.AsObject(), settings)) { Reason = node["reason"]?.GetValue<string>() },
            "imageGenerationCall" => new ImageGenerationToolCallContent(node["callId"]!.GetValue<string>()),
            "imageGenerationResult" => new ImageGenerationToolResultContent(node["callId"]!.GetValue<string>()) { Outputs = node["outputs"] is JsonArray outputs ? outputs.Select(x => ReadContent(x!.AsObject(), settings)).ToList() : null },
            "usage" => new UsageContent(node["details"]?.Deserialize<UsageDetails>() ?? new UsageDetails()),
            "richText" => new RichTextContent(node["text"]?.GetValue<string>() ?? string.Empty, node["nodes"] is JsonArray nodes ? nodes.Select(x => ReadNode(x!.AsObject())).ToList() : []),
            "error" => new ErrorContent(node["message"]?.GetValue<string>()) { ErrorCode = node["code"]?.GetValue<string>(), Details = node["details"]?.GetValue<string>() },
            _ => UnknownContent(type, settings)
        };
    }
    private static AIContent UnknownContent(string type, ChatRecordingOptions settings)
    {
        Reject(settings, $"Recording contains unsupported AI content type '{type}'. Register an {nameof(IChatRecordingContentCodec)}.");
        return new ErrorContent("[REDACTED unsupported content]");
    }
    private static byte[] ReadData(JsonObject node, ChatRecordingOptions settings)
    {
        if (node["data"]?.GetValue<string>() is { } encoded) return Convert.FromBase64String(encoded);
        var blob = node["blob"]?.AsObject() ?? throw new InvalidDataException("Data content has neither inline data nor blob.");
        var relative = blob["path"]?.GetValue<string>() ?? throw new InvalidDataException("Blob has no path.");
        var root = settings.FixturePath is null ? null : Path.GetDirectoryName(Path.GetFullPath(settings.FixturePath));
        if (root is null) throw new InvalidDataException("Recording blobs cannot be resolved from a fixture stream; load the recording from a fixture path.");
        if (Path.IsPathRooted(relative))
            throw new InvalidDataException($"Blob path '{relative}' must be relative to the fixture.");
        var rootPath = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, relative));
        if (!fullPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException($"Blob path '{relative}' escapes the fixture directory.");
        var bytes = File.ReadAllBytes(fullPath);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (hash != blob["sha256"]?.GetValue<string>() || bytes.Length != blob["length"]?.GetValue<int>())
            throw new InvalidDataException($"Blob '{relative}' failed hash or length verification.");
        return bytes;
    }
    private static bool IsSafeUri(Uri uri) =>
        uri.IsAbsoluteUri &&
        (uri.Scheme == System.Uri.UriSchemeHttp || uri.Scheme == System.Uri.UriSchemeHttps) &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment) &&
        !Regex.IsMatch(uri.Host, @"(?i)(^|\.)(api\.)?(openai|azure|anthropic|googleapis|amazonaws)(\.|$)");
    private static object? ReadSafeValue(JsonNode? node)
    {
        if (node is null)
            return null;
        if (node is JsonObject obj)
            return obj.ToDictionary(pair => pair.Key, pair => ReadSafeValue(pair.Value));
        if (node is JsonArray array)
            return array.Select(ReadSafeValue).ToList();
        var element = node.Deserialize<JsonElement>();
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number => element.GetDouble(),
            _ => throw new InvalidDataException($"Unsupported JSON value kind '{element.ValueKind}'."),
        };
    }
    private static readonly Regex SensitivePropertyName = new(
        @"(?i)^(authorization|cookie|api[_-]?key|connection[_-]?string|x-ms-[\w-]*|trace(parent|state))$",
        RegexOptions.CultureInvariant);
    private static JsonObject Sanitize(JsonObject node, ChatRecordingOptions settings)
    {
        SanitizeNode(node, settings, "$", false);
        return node;
    }
    private static JsonNode? Sanitize(JsonNode? node, ChatRecordingOptions settings)
    {
        SanitizeNode(node, settings, "$", false);
        return node;
    }
    private static void SanitizeNode(JsonNode? node, ChatRecordingOptions settings, string path, bool sensitive)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj.ToList())
                    SanitizeNode(pair.Value, settings, path + "." + pair.Key, sensitive || SensitivePropertyName.IsMatch(pair.Key));
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                    SanitizeNode(array[i], settings, $"{path}[{i}]", sensitive);
                break;
            case JsonValue value when value.TryGetValue<string>(out var text):
                if (sensitive)
                {
                    Redact(settings, $"Sensitive property at {path} cannot be persisted.");
                    value.ReplaceWith(TextValue("[REDACTED]", settings));
                }
                else
                    value.ReplaceWith(TextValue(text, settings));
                break;
        }
    }
    private sealed class RecordedInputRequestContent(string requestId) : InputRequestContent(requestId);
    private sealed class RecordedInputResponseContent(string requestId) : InputResponseContent(requestId);
    private static JsonObject Node(RichTextNode node, string path)
    {
        var result = new JsonObject { ["kind"] = node.GetType().Name, ["children"] = new JsonArray(node.Children.Select((child, index) => Node(child, $"{path}.children[{index}]")).ToArray()) };
        switch (node)
        {
            case TextNode n: result["text"] = n.Text; break;
            case HeadingNode n: result["level"] = n.Level; break;
            case CodeBlockNode n: result["code"] = n.Code; result["language"] = n.Language; break;
            case InlineCodeNode n: result["code"] = n.Code; break;
            case LinkNode n: result["url"] = n.Url; result["title"] = n.Title; break;
            case ImageNode n: result["url"] = n.Url; result["alt"] = n.Alt; result["title"] = n.Title; break;
            case ListNode n: result["ordered"] = n.Ordered; result["start"] = n.Start; break;
            case ListItemNode n: result["checked"] = n.Checked; break;
            case HtmlNode n: result["value"] = n.Value; break;
            case TableNode n: result["alignment"] = new JsonArray(n.Alignment.Select(value => JsonValue.Create((int)value)).ToArray()); break;
            case DefinitionNode n: result["label"] = n.Label; result["url"] = n.Url; result["title"] = n.Title; break;
            case LinkReferenceNode n: result["label"] = n.Label; result["referenceKind"] = (int)n.ReferenceKind; break;
            case ImageReferenceNode n: result["label"] = n.Label; result["alt"] = n.Alt; result["referenceKind"] = (int)n.ReferenceKind; break;
            case FootnoteDefinitionNode n: result["label"] = n.Label; break;
            case FootnoteReferenceNode n: result["label"] = n.Label; break;
            case ParagraphNode or BlockQuoteNode or EmphasisNode or StrongNode or StrikethroughNode or LineBreakNode or ThematicBreakNode or TableRowNode or TableCellNode or FootnoteNode: break;
            default: throw new NotSupportedException($"Recording does not support rich text node '{node.GetType().FullName}' at {path}.");
        }
        return result;
    }
    private static RichTextNode ReadNode(JsonObject node)
    {
        var kind = node["kind"]?.GetValue<string>() ?? throw new InvalidDataException("Rich text node lacks kind.");
        RichTextNode result = kind switch
        {
            nameof(TextNode) => new TextNode(node["text"]?.GetValue<string>() ?? string.Empty),
            nameof(HeadingNode) => new HeadingNode(node["level"]?.GetValue<int>() ?? 1),
            nameof(CodeBlockNode) => new CodeBlockNode(node["code"]?.GetValue<string>() ?? string.Empty, node["language"]?.GetValue<string>()),
            nameof(InlineCodeNode) => new InlineCodeNode(node["code"]?.GetValue<string>() ?? string.Empty),
            nameof(LinkNode) => new LinkNode(node["url"]?.GetValue<string>() ?? string.Empty, node["title"]?.GetValue<string>()),
            nameof(ImageNode) => new ImageNode(node["url"]?.GetValue<string>() ?? string.Empty, node["alt"]?.GetValue<string>(), node["title"]?.GetValue<string>()),
            nameof(ListNode) => new ListNode(node["ordered"]?.GetValue<bool>() ?? false, node["start"]?.GetValue<int?>()),
            nameof(ListItemNode) => new ListItemNode { Checked = node["checked"]?.GetValue<bool?>() },
            nameof(TableNode) => new TableNode { Alignment = node["alignment"] is JsonArray alignment ? alignment.Select(value => (TableColumnAlignment)(value?.GetValue<int>() ?? 0)).ToArray() : [] },
            nameof(HtmlNode) => new HtmlNode(node["value"]?.GetValue<string>() ?? string.Empty),
            nameof(DefinitionNode) => new DefinitionNode { Label = node["label"]?.GetValue<string>() ?? string.Empty, Url = node["url"]?.GetValue<string>() ?? string.Empty, Title = node["title"]?.GetValue<string>() },
            nameof(LinkReferenceNode) => new LinkReferenceNode { Label = node["label"]?.GetValue<string>() ?? string.Empty, ReferenceKind = (ReferenceKind)(node["referenceKind"]?.GetValue<int>() ?? 0) },
            nameof(ImageReferenceNode) => new ImageReferenceNode { Label = node["label"]?.GetValue<string>() ?? string.Empty, Alt = node["alt"]?.GetValue<string>(), ReferenceKind = (ReferenceKind)(node["referenceKind"]?.GetValue<int>() ?? 0) },
            nameof(FootnoteDefinitionNode) => new FootnoteDefinitionNode { Label = node["label"]?.GetValue<string>() ?? string.Empty },
            nameof(FootnoteReferenceNode) => new FootnoteReferenceNode { Label = node["label"]?.GetValue<string>() ?? string.Empty },
            nameof(ParagraphNode) => new ParagraphNode(), nameof(BlockQuoteNode) => new BlockQuoteNode(), nameof(EmphasisNode) => new EmphasisNode(),
            nameof(StrongNode) => new StrongNode(), nameof(StrikethroughNode) => new StrikethroughNode(), nameof(LineBreakNode) => new LineBreakNode(),
            nameof(ThematicBreakNode) => new ThematicBreakNode(), nameof(TableRowNode) => new TableRowNode(), nameof(TableCellNode) => new TableCellNode(),
            nameof(FootnoteNode) => new FootnoteNode(),
            _ => throw new NotSupportedException($"Recording contains unsupported rich text node '{kind}'."),
        };
        if (node["children"] is JsonArray children) foreach (var child in children) result.AddChild(ReadNode(child!.AsObject()));
        return result;
    }
    internal static void AssertRequestEqual(int interaction, JsonNode expected, JsonNode actual, ChatRecordingOptions settings)
    {
        var expectedCopy = expected.DeepClone();
        var actualCopy = actual.DeepClone();
        NormalizeBlobs(expectedCopy, settings);
        if (!settings.RequireMessageId)
        {
            RemoveMessageIds(expectedCopy);
            RemoveMessageIds(actualCopy);
        }
        Compare(interaction, "$", expectedCopy, actualCopy);
    }
    private static void NormalizeBlobs(JsonNode? node, ChatRecordingOptions settings)
    {
        if (node is JsonObject obj)
        {
            if (obj["type"]?.GetValue<string>() == "data" && obj["blob"] is JsonObject)
            {
                obj["data"] = Convert.ToBase64String(ReadData(obj, settings));
                obj.Remove("blob");
            }
            foreach (var value in obj)
                NormalizeBlobs(value.Value, settings);
        }
        else if (node is JsonArray array)
            foreach (var value in array)
                NormalizeBlobs(value, settings);
    }
    private static void RemoveMessageIds(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj["messages"] is JsonArray messages)
                foreach (var message in messages.OfType<JsonObject>())
                    message.Remove("messageId");
            foreach (var value in obj)
                RemoveMessageIds(value.Value);
        }
        else if (node is JsonArray array)
            foreach (var value in array)
                RemoveMessageIds(value);
    }
    private static void Compare(int interaction, string path, JsonNode? expected, JsonNode? actual)
    {
        if (expected is JsonObject eo && actual is JsonObject ao)
        {
            foreach (var key in eo.Select(x => x.Key).Union(ao.Select(x => x.Key)).OrderBy(x => x))
                Compare(interaction, path + "." + key, eo[key], ao[key]);
            return;
        }
        if (expected is JsonArray ea && actual is JsonArray aa)
        {
            if (ea.Count != aa.Count) throw new ChatRecordingMismatchException(interaction, path + ".length", JsonValue.Create(ea.Count), JsonValue.Create(aa.Count));
            for (var i = 0; i < ea.Count; i++) Compare(interaction, $"{path}[{i}]", ea[i], aa[i]);
            return;
        }
        if (!JsonNode.DeepEquals(expected, actual)) throw new ChatRecordingMismatchException(interaction, path, expected, actual);
    }
}
