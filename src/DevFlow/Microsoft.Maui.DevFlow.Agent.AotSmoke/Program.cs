using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core.Network;
using Microsoft.Maui.DevFlow.Logging;

if (JsonSerializer.IsReflectionEnabledByDefault)
    throw new InvalidOperationException("This smoke must run with JSON reflection disabled.");

var output = Path.GetFullPath(args.Length > 0 ? args[0] : ".artifacts/devflow-aot-smoke");
Directory.CreateDirectory(output);
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
var ct = timeout.Token;
var agentPort = FindPort();
var brokerPort = FindPort();
using var broker = new AgentHttpServer(brokerPort);
var registered = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
broker.MapWebSocket("/ws/agent", async (_, stream, _, token) =>
{
    var message = await AgentHttpServer.WebSocketReadTextAsync(stream, token);
    using var document = JsonDocument.Parse(message!);
    registered.TrySetResult(document.RootElement.Clone());
    await AgentHttpServer.WebSocketSendTextAsync(stream,
        $$"""{"type":"registered","id":"aot-smoke","port":{{agentPort}}}""", token);
    await AgentHttpServer.WebSocketReadTextAsync(stream, token);
});
var leaseRequests = 0;
broker.MapPost("/api/leases/aot-smoke", request =>
{
    using var document = JsonDocument.Parse(request.Body!);
    Check(document.RootElement.TryGetProperty("action", out _), "broker lease request casing");
    Interlocked.Increment(ref leaseRequests);
    if (document.RootElement.TryGetProperty("leaseId", out var leaseId) && leaseId.GetString() == "denied")
        return Task.FromResult(new HttpResponse
        {
            Body = """{"ok":true,"allowed":false,"heldByOther":true,"holderKind":null,"label":null,"authority":"broker"}"""
        });
    return Task.FromResult(new HttpResponse
    {
        Body = """{"OK":true,"Allowed":true,"YouHold":true,"leaseId":"smoke","authority":"broker"}"""
    });
}, requiresMutationLease: false);
var recordingRequests = 0;
broker.MapPost("/api/recordings/aot-smoke", request =>
{
    using var document = JsonDocument.Parse(request.Body!);
    Check(document.RootElement.TryGetProperty("action", out _), "broker recording request casing");
    Interlocked.Increment(ref recordingRequests);
    return Task.FromResult(new HttpResponse
    {
        Body = """{"ok":true,"recording":false,"authority":"broker"}"""
    });
}, requiresMutationLease: false);
broker.Start();

using var registration = new BrokerRegistration("aot-smoke", "net10.0", "host", "AOT smoke", brokerPort);
var agentOptions = new AgentOptions { Port = agentPort, EnableProfiler = true };
var routes = agentOptions.RegisterExtension("com.devflow.smoke", "Source-generated extension contracts");
routes.MapPost("/echo", request => Task.FromResult(HttpResponse.Json(request.BodyAs<ExtensionReply>()!)));
routes.MapGet("/unsupported", _ => Task.FromResult(HttpResponse.Json(new MissingContract("unsupported"))));
routes.MapGet("/unsupported-nested", _ => Task.FromResult(HttpResponse.Json(new object[] { new MissingContract("nested") })));
routes.MapGet("/polymorphic", _ => Task.FromResult(HttpResponse.Json(new DerivedExtension { Count = 7 })));
routes.MapGet("/string-number", _ => Task.FromResult(HttpResponse.Json(new StringNumberExtension())));
routes.MapGet("/named-number", _ => Task.FromResult(HttpResponse.Json(new NamedNumberExtension())));
routes.MapGet("/named-iterator", _ => Task.FromResult(HttpResponse.Json(new NamedIteratorExtension())));
routes.MapGet("/string-iterator", _ => Task.FromResult(HttpResponse.Json(new StringIteratorExtension())));
routes.MapGet("/named-dictionary", _ => Task.FromResult(HttpResponse.Json(
    new NamedDictionaryExtension(new System.Collections.Hashtable { ["Number"] = double.NaN }))));
routes.MapGet("/ambiguous-dictionary", _ => Task.FromResult(HttpResponse.Json(
    new NamedDictionaryExtension(new MultipleContractDictionary { ["Number"] = double.NaN }))));
routes.MapGet("/converter-failure", _ => Task.FromResult(HttpResponse.Json(new FailingCollection())));
using var agent = new SmokeAgent(agentOptions);
agent.SetBrokerRegistration(registration);
using var logs = new FileLogProvider(Path.Combine(output, "logs"));
agent.SetLogProvider(logs);
logs.Writer.Write(new FileLogEntry(DateTime.UtcNow, "Information", "Smoke", "reflection-disabled transport"));
Check(await registration.TryRegisterAsync() == agentPort, "broker registration assigns a port");
var hello = await registered.Task.WaitAsync(ct);
Check(hello.GetProperty("type").GetString() == "register", "registration type");
Check(hello.TryGetProperty("sessionId", out var session) && session.ValueKind == JsonValueKind.Null,
    "registration preserves null fields");
agent.StartServerOnly(new DelegateAgentDispatcher(() => false, action => action()));
using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{agentPort}"), Timeout = TimeSpan.FromSeconds(5) };
http.DefaultRequestHeaders.Add("X-DevFlow-Lease", "smoke");

var status = await Get("/api/v1/agent/status");
Check(status.GetProperty("agent").GetProperty("name").GetString() == "Microsoft.Maui.DevFlow.Agent", "status");
await Get("/api/v1/agent/capabilities");
await Get("/api/v1/storage/roots");
var lease = await Post("/api/v1/agent/lease", """{"ACTION":"acquire","LEASEID":"smoke"}""");
Check(lease.GetProperty("authority").GetString() == "broker", "broker-authoritative lease");
await Post("/api/v1/agent/recording", """{"action":"status"}""");
Check(recordingRequests > 0 && leaseRequests > 0, "broker control transports");
using (var deniedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ui/actions/tap"))
{
    deniedRequest.Headers.Add("X-DevFlow-Lease", "denied");
    deniedRequest.Content = new StringContent("""{"elementId":"counter"}""", Encoding.UTF8, "application/json");
    using var response = await http.SendAsync(deniedRequest, ct);
    Check(response.StatusCode == HttpStatusCode.Conflict, "lease denial status");
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    var details = document.RootElement.GetProperty("details");
    Check(!details.TryGetProperty("Label", out _) && !details.TryGetProperty("HolderKind", out _), "lease error omits null object properties");
    Check(details.GetProperty("Authority").GetString() == "broker", "lease error preserves PascalCase details");
}

var tree = await Get("/api/v1/ui/tree");
Check(tree[0].GetProperty("children")[0].GetProperty("text").GetString() == "Before", "tree nested DTOs");
await Post("/api/v1/ui/actions/tap", """{"ELEMENTID":"counter"}""");
var element = await Get("/api/v1/ui/elements/counter");
Check(element.GetProperty("text").GetString() == "After", "case-insensitive tap DTO and element");
var property = await Get("/api/v1/ui/elements/counter/properties/Text");
Check(property.GetProperty("value").GetString() == "After", "property envelope");
using (var screenshot = await http.GetAsync("/api/v1/ui/screenshot", ct))
{
    screenshot.EnsureSuccessStatusCode();
    Check(screenshot.Content.Headers.ContentType?.MediaType == "image/png", "screenshot content type");
    Check((await screenshot.Content.ReadAsByteArrayAsync(ct)).AsSpan().SequenceEqual(SmokeAgent.Png), "binary screenshot transport");
}
var logEntries = await Get("/api/v1/logs");
Check(logEntries[0].GetProperty("m").GetString() == "reflection-disabled transport", "log DTO");

agent.NetworkStore.Add(new NetworkRequestEntry { Id = "fixture", Method = "GET", Url = "https://example.invalid/fixture" });
await Get("/api/v1/network/requests");
await Get("/api/v1/network/requests/fixture");
await Get("/api/v1/webview/contexts");
agent.RegisterCdpWebView(_ => Task.FromResult("""{"id":99995,"result":{"frameId":"frame"}}"""), () => true);
await Post("/api/v1/webview/navigate", """{"url":"https://example.invalid"}""");
await Post("/api/v1/ui/actions/batch", """{"actions":[{"action":"tap","elementId":"counter"}]}""");
await Get("/api/v1/profiler/capabilities");
await Post("/api/v1/profiler/sessions", """{}""");
await Post("/api/v1/profiler/markers", """{"name":"smoke"}""");
await Post("/api/v1/profiler/spans", """{"name":"smoke","kind":"test"}""");
await Get("/api/v1/profiler/hotspots");

await Socket("/ws/v1/network", "replay");
await Socket("/ws/v1/logs", "replay");
await Socket("/ws/v1/ui/events", "lifecycle");
await Socket("/ws/v1/profiler", "error");
await Socket("/ws/v1/profiler?sessionId=current", "batch");

using (var missing = await http.GetAsync("/missing", ct))
{
    Check(missing.StatusCode == HttpStatusCode.NotFound, "404 status");
    using var error = JsonDocument.Parse(await missing.Content.ReadAsStringAsync(ct));
    Check(error.RootElement.GetProperty("success").GetBoolean() == false, "404 error JSON");
}
using var dynamicDocument = JsonDocument.Parse("""{"document":true}""");
var primitives = new Dictionary<string, object?>
{
    ["null"] = null, ["byte"] = (byte)1, ["sbyte"] = (sbyte)-1, ["short"] = (short)-2, ["ushort"] = (ushort)2,
    ["int"] = -3, ["uint"] = 3u, ["long"] = -4L, ["ulong"] = ulong.MaxValue, ["float"] = 1.25f, ["double"] = 1.5,
    ["decimal"] = 2.5m, ["bool"] = true, ["char"] = 'x', ["string"] = "quote \" and newline\n",
    ["date"] = DateTime.UtcNow, ["offset"] = DateTimeOffset.UtcNow, ["guid"] = Guid.Empty,
    ["bytes"] = new byte[] { 1, 2 }, ["sequence"] = Enumerable.Range(0, 3).Select(n => (object)n),
    ["node"] = System.Text.Json.Nodes.JsonNode.Parse("""{"nested":[true,null]}"""),
    ["document"] = dynamicDocument,
    ["enum"] = DayOfWeek.Monday, ["half"] = (Half)1.5, ["int128"] = (Int128)42, ["uint128"] = (UInt128)42,
    ["dateOnly"] = new DateOnly(2026, 9, 21), ["timeOnly"] = new TimeOnly(12, 30), ["span"] = TimeSpan.FromSeconds(3),
    ["uri"] = new Uri("https://example.invalid"), ["version"] = new Version(1, 2)
};
using (var document = JsonDocument.Parse(AgentJson.Serialize(primitives)))
{
    Check(document.RootElement.GetProperty("bytes").GetString() == "AQI=", "base64 bytes");
    Check(document.RootElement.GetProperty("null").ValueKind == JsonValueKind.Null, "dictionary null");
    Check(document.RootElement.GetProperty("sequence").GetArrayLength() == 3, "iterator arrays");
    Check(document.RootElement.GetProperty("document").GetProperty("document").GetBoolean(), "JsonDocument values");
}
try
{
    AgentJson.Serialize(new MissingContract("unsupported"));
    throw new InvalidOperationException("Unregistered types must fail explicitly.");
}
catch (NotSupportedException ex)
{
    Check(ex.Message.Contains("RegisterContext", StringComparison.Ordinal), "explicit metadata error");
}
foreach (var path in new[] { "unsupported", "unsupported-nested" })
{
    using var unsupported = await http.GetAsync($"/api/v1/ext/com.devflow.smoke/{path}", ct);
    Check(unsupported.StatusCode == HttpStatusCode.InternalServerError, "unsupported extension response status");
    var body = await unsupported.Content.ReadAsStringAsync(ct);
    Check(body.Contains("RegisterContext", StringComparison.Ordinal),
        "unsupported extension error includes metadata guidance");
    using var error = JsonDocument.Parse(body);
    Check(error.RootElement.GetProperty("reason").GetString() == "json-metadata-missing",
        "unsupported extension error preserves metadata reason");
}
AgentJson.RegisterContext(ExtensionJsonContext.Default);
var namedIterator = await Get("/api/v1/ext/com.devflow.smoke/named-iterator");
Check(namedIterator.GetProperty("Value")[0].GetString() == "NaN", "containing member permits named floating point in an iterator over HTTP");
var stringIterator = await Get("/api/v1/ext/com.devflow.smoke/string-iterator");
Check(stringIterator.GetProperty("Value")[0].GetString() == "12", "containing member writes iterator numbers as strings over HTTP");
var namedDictionary = await Get("/api/v1/ext/com.devflow.smoke/named-dictionary");
Check(namedDictionary.GetProperty("Value").GetProperty("Number").GetString() == "NaN",
    "containing member permits named floating point in a dynamic dictionary over HTTP");
foreach (var path in new[] { "ambiguous-dictionary", "converter-failure" })
{
    using var response = await http.GetAsync($"/api/v1/ext/com.devflow.smoke/{path}", ct);
    Check(response.StatusCode == HttpStatusCode.InternalServerError, "unsupported JSON contract returns an HTTP response");
    using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    Check(error.RootElement.GetProperty("reason").GetString() == "json-serialization-unsupported",
        "unsupported JSON contracts are not mislabeled as missing metadata");
    Check(!string.IsNullOrWhiteSpace(error.RootElement.GetProperty("error").GetString()),
        "unsupported JSON diagnostic is preserved");
}
AgentJson.RegisterContext(ExplicitCollectionJsonContext.Default);
var explicitDictionary = await Get("/api/v1/ext/com.devflow.smoke/ambiguous-dictionary");
Check(explicitDictionary.GetProperty("Value").GetProperty("Number").GetString() == "NaN",
    "concrete metadata resolves dictionary ambiguity without losing member number handling");
var namedNumber = await Get("/api/v1/ext/com.devflow.smoke/named-number");
Check(namedNumber.GetProperty("Value").GetString() == "NaN", "boxed member permits named floating point over HTTP");
var stringNumber = await Get("/api/v1/ext/com.devflow.smoke/string-number");
Check(stringNumber.GetProperty("Value").GetString() == "12", "boxed member writes a string over HTTP");
var polymorphic = await Get("/api/v1/ext/com.devflow.smoke/polymorphic");
Check(polymorphic.GetProperty("$kind").GetString() == "derived", "ancestor polymorphic discriminator over HTTP");
Check(AgentJson.Deserialize<BaseExtension>(polymorphic.GetRawText()) is DerivedExtension { Count: 7 },
    "abstract extension base roundtrip");
using (var nested = JsonDocument.Parse(HttpResponse.Json(new MemberExtension
{
    Value = new object[] { new DerivedExtension { Count = 7 }, new StringNumberExtension(), new NamedNumberExtension() }
        .Select(value => value)
}).Body!))
{
    var values = nested.RootElement.GetProperty("Value");
    Check(values[0].GetProperty("$kind").GetString() == "derived", "nested polymorphic discriminator");
    Check(values[1].GetProperty("Value").GetString() == "12", "nested boxed number string");
    Check(values[2].GetProperty("Value").GetString() == "NaN", "nested boxed named number");
}
using (var converted = JsonDocument.Parse(HttpResponse.Json(new ConvertedSequence { 1, 2 }).Body!))
    Check(converted.RootElement.GetString() == "explicit:2", "registered converter precedes dynamic ancestors");
var extension = new ExtensionReply("registered", 42);
using (var dictionary = JsonDocument.Parse(HttpResponse.Json(new Dictionary<int, string> { [1] = "one" }).Body!))
    Check(dictionary.RootElement.GetProperty("1").GetString() == "one", "registered non-string dictionary metadata");
using (var document = JsonDocument.Parse(HttpResponse.Json(extension).Body!))
    Check(document.RootElement.GetProperty("name").GetString() == "registered", "extension source-generated metadata");
Check(new HttpRequest { Body = """{"NAME":"read","COUNT":3}""" }.BodyAs<ExtensionReply>()?.Count == 3,
    "extension request metadata and case-insensitive input");
var echo = await Post("/api/v1/ext/com.devflow.smoke/echo", """{"NAME":"posted","COUNT":5}""");
Check(echo.GetProperty("name").GetString() == "posted" && echo.GetProperty("Count").GetInt32() == 5,
    "registered extension roundtrip over HTTP");
agent.CheckRequestMetadata();
var mauiBackendChecked = false;
#if MAUI_BACKEND_SMOKE
await MauiBackendSmoke.RunAsync();
mauiBackendChecked = true;
#endif

await agent.StopAsync();
await broker.StopAsync();
File.WriteAllText(Path.Combine(output, "result.json"),
    AgentJson.Serialize(new Dictionary<string, object?>
    {
        ["passed"] = true, ["reflectionEnabled"] = JsonSerializer.IsReflectionEnabledByDefault,
        ["nativeAot"] = !System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported,
        ["mauiBackendChecked"] = mauiBackendChecked, ["nativeRenderingVerified"] = false,
        ["polymorphicRoundtrip"] = true, ["boxedNumberHandlingOverHttp"] = true,
        ["containingMemberNumberHandlingOverHttp"] = true,
        ["dictionaryNumberHandlingOverHttp"] = true, ["unsupportedJsonContractHttpErrors"] = true,
        ["leaseRequests"] = leaseRequests, ["recordingRequests"] = recordingRequests
    }));
Console.WriteLine("PASS: reflection-disabled HTTP, broker registration/lease/recording, tree/action/property/PNG/log transports, WebSockets, dynamic values, generated extension contracts.");

async Task<JsonElement> Get(string path)
{
    using var response = await http.GetAsync(path, ct);
    var text = await response.Content.ReadAsStringAsync(ct);
    Check(response.IsSuccessStatusCode, $"{path}: {(int)response.StatusCode} {text}");
    using var document = JsonDocument.Parse(text);
    return document.RootElement.Clone();
}
async Task<JsonElement> Post(string path, string body)
{
    using var response = await http.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"), ct);
    var text = await response.Content.ReadAsStringAsync(ct);
    Check(response.IsSuccessStatusCode, $"{path}: {(int)response.StatusCode} {text}");
    using var document = JsonDocument.Parse(text);
    return document.RootElement.Clone();
}
async Task Socket(string path, string expectedType)
{
    using var socket = new ClientWebSocket();
    await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{agentPort}{path}"), ct);
    var buffer = new byte[65536];
    var result = await socket.ReceiveAsync(buffer, ct);
    using var document = JsonDocument.Parse(buffer.AsMemory(0, result.Count));
    Check(document.RootElement.GetProperty("type").GetString() == expectedType, path);
    socket.Abort();
}
static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static int FindPort()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
}

sealed class SmokeAgent(AgentOptions options) : DevFlowAgentService(options)
{
    protected override bool IsUiSupported => true;
    public override bool IsAppBound => true;
    public static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aC1kAAAAASUVORK5CYII=");
    private readonly ElementInfo _button = new() { Id = "counter", Type = "Button", Text = "Before", IsVisible = true };
    protected override Task<HttpResponse> HandleTree(HttpRequest request)
        => Task.FromResult(HttpResponse.Json(new[] { new ElementInfo { Id = "page", Type = "Page", Children = [_button] } }));
    protected override Task<HttpResponse> HandleElement(HttpRequest request) => Task.FromResult(HttpResponse.Json(_button));
    protected override Task<HttpResponse> HandleTap(HttpRequest request)
    {
        if (request.BodyAs<ActionRequest>()?.ElementId != "counter")
            return Task.FromResult(HttpResponse.Error("unexpected element"));
        _button.Text = "After";
        return Task.FromResult(HttpResponse.Ok());
    }
    protected override Task<HttpResponse> HandleProperty(HttpRequest request)
        => Task.FromResult(HttpResponse.Json(new Dictionary<string, object?> { ["value"] = _button.Text }));
    protected override Task<HttpResponse> HandleScreenshot(HttpRequest request) => Task.FromResult(HttpResponse.Png(Png));
    public void CheckRequestMetadata()
    {
        var request = new HttpRequest { Body = "{}" };
        _ = request.BodyAs<ResizeRequest>();
        _ = request.BodyAs<KeyActionRequest>();
        _ = request.BodyAs<GestureActionRequest>();
        _ = request.BodyAs<BatchRequest>();
        _ = request.BodyAs<WebViewDomQueryRequest>();
        _ = request.BodyAs<FillRequest>();
        _ = request.BodyAs<ScrollRequest>();
        _ = request.BodyAs<NavigateRequest>();
        _ = request.BodyAs<SetPropertyRequest>();
        _ = request.BodyAs<InvokeActionRequest>();
        _ = request.BodyAs<WebViewInputClickRequest>();
        _ = request.BodyAs<WebViewInputFillRequest>();
        _ = request.BodyAs<WebViewInputTextRequest>();
        _ = request.BodyAs<PreferenceSetRequest>();
        _ = request.BodyAs<ThemeSetRequest>();
        _ = request.BodyAs<SecureStorageSetRequest>();
        _ = request.BodyAs<FileUploadRequest>();
        _ = request.BodyAs<JobRunRequest>();
    }
}

sealed record MissingContract(string Value);
sealed record ExtensionReply([property: JsonPropertyName("name")] string Name, int Count);
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(DerivedExtension), "derived")]
abstract class BaseExtension;
sealed class DerivedExtension : BaseExtension
{
    public int Count { get; set; }
}
sealed class StringNumberExtension
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public object Value { get; set; } = 12;
}
sealed class NamedNumberExtension
{
    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public object Value { get; set; } = double.NaN;
}
sealed class MemberExtension
{
    public object? Value { get; set; }
}
sealed class NamedIteratorExtension
{
    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public object Value { get; } = new[] { double.NaN }.Select(x => (object)x);
}
sealed class StringIteratorExtension
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public object Value { get; } = new[] { 12 }.Select(x => (object)x);
}
sealed class NamedDictionaryExtension(object value)
{
    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public object Value { get; } = value;
}
sealed class MultipleContractDictionary : IDictionary<string, object>, IReadOnlyDictionary<string, object>
{
    private readonly Dictionary<string, object> _values = [];
    public object this[string key] { get => _values[key]; set => _values[key] = value; }
    public ICollection<string> Keys => _values.Keys;
    public ICollection<object> Values => _values.Values;
    IEnumerable<string> IReadOnlyDictionary<string, object>.Keys => Keys;
    IEnumerable<object> IReadOnlyDictionary<string, object>.Values => Values;
    public int Count => _values.Count;
    public bool IsReadOnly => false;
    public void Add(string key, object value) => _values.Add(key, value);
    public bool ContainsKey(string key) => _values.ContainsKey(key);
    public bool Remove(string key) => _values.Remove(key);
    public bool TryGetValue(string key, out object value) => _values.TryGetValue(key, out value!);
    public void Add(KeyValuePair<string, object> item) => ((ICollection<KeyValuePair<string, object>>)_values).Add(item);
    public void Clear() => _values.Clear();
    public bool Contains(KeyValuePair<string, object> item) => ((ICollection<KeyValuePair<string, object>>)_values).Contains(item);
    public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) => ((ICollection<KeyValuePair<string, object>>)_values).CopyTo(array, arrayIndex);
    public bool Remove(KeyValuePair<string, object> item) => ((ICollection<KeyValuePair<string, object>>)_values).Remove(item);
    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _values.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
[JsonConverter(typeof(FailingCollectionConverter))]
sealed class FailingCollection : List<object>;
sealed class FailingCollectionConverter : JsonConverter<FailingCollection>
{
    public override FailingCollection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException("Explicit converter failure.");
    public override void Write(Utf8JsonWriter writer, FailingCollection value, JsonSerializerOptions options)
        => throw new NotSupportedException("Explicit converter failure.");
}
[JsonConverter(typeof(SequenceConverter))]
sealed class ConvertedSequence : List<int>;
sealed class SequenceConverter : JsonConverter<ConvertedSequence>
{
    public override ConvertedSequence Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException("Write-only smoke converter.");
    public override void Write(Utf8JsonWriter writer, ConvertedSequence value, JsonSerializerOptions options)
        => writer.WriteStringValue($"explicit:{value.Count}");
}
[JsonSerializable(typeof(ExtensionReply))]
[JsonSerializable(typeof(Dictionary<int, string>))]
[JsonSerializable(typeof(BaseExtension))]
[JsonSerializable(typeof(DerivedExtension))]
[JsonSerializable(typeof(StringNumberExtension))]
[JsonSerializable(typeof(NamedNumberExtension))]
[JsonSerializable(typeof(MemberExtension))]
[JsonSerializable(typeof(NamedIteratorExtension))]
[JsonSerializable(typeof(StringIteratorExtension))]
[JsonSerializable(typeof(NamedDictionaryExtension))]
[JsonSerializable(typeof(FailingCollection))]
[JsonSerializable(typeof(ConvertedSequence))]
internal partial class ExtensionJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(MultipleContractDictionary))]
internal partial class ExplicitCollectionJsonContext : JsonSerializerContext;
