#!/usr/bin/env dotnet run
// Analyze a speedscope JSON trace and categorize startup CPU time by component.
//
// Usage:
//   dotnet run -- <trace.speedscope.json> [--app-namespaces NS1,NS2] [--top N] [--json]
//
// Reads a speedscope v0.0.1 JSON file (produced by dotnet-trace --format speedscope)
// and outputs a breakdown of CPU sample time by component category.
//
// Categories (checked in precedence order):
//   1. App Code       — matches user-supplied --app-namespaces prefixes
//   2. MAUI Framework — Microsoft.Maui.*, Microsoft.Extensions.DependencyInjection.*
//   3. .NET Bindings  — Java.Interop.*, Android.* (managed), ObjCRuntime.*, UIKit.* (managed)
//   4. .NET Runtime   — System.*, coreclr!, clrjit!, System.Private.CoreLib
//   5. BCL/3rd-party  — any remaining managed frames
//   6. Platform/OS    — java.*, android.*, dalvik.*, objc_*, native frames
//   7. Unknown        — anything else

using System.Text.Json;

// --- Category definitions ---

const string CatApp = "App Code";
const string CatMaui = "MAUI Framework";
const string CatBindings = ".NET Bindings";
const string CatRuntime = ".NET Runtime";
const string CatBcl = "BCL / Third-party";
const string CatPlatform = "Platform / OS";
const string CatUnknown = "Unknown";

string[] displayOrder = [CatApp, CatMaui, CatBindings, CatRuntime, CatBcl, CatPlatform, CatUnknown];

string[] mauiPrefixes = ["Microsoft.Maui.", "Microsoft.Extensions.DependencyInjection"];
string[] bindingsPrefixes = ["Java.Interop.", "Android.", "AndroidX.", "Google.", "ObjCRuntime.", "CoreAnimation.", "CoreGraphics."];
string[] bindingsManagedPrefixes = ["UIKit.", "Foundation.", "AppKit.", "CoreFoundation."];
string[] runtimePrefixes = ["System.", "Internal.", "Microsoft.Extensions.Logging.", "Microsoft.Extensions.Options.", "Microsoft.Extensions.Configuration."];
string[] runtimeNativeMarkers = ["coreclr!", "clrjit!", "libcoreclr", "libclrjit", "mono_", "libmonosgen", "System.Private.CoreLib"];
string[] platformPrefixes = ["java.", "javax.", "android.", "androidx.", "dalvik.", "com.android.", "com.google.", "objc_", "libsystem_", "libdyld", "libdispatch", "dyld", "[native", "[unknown", "0x"];

// --- Parse arguments ---

string? traceFile = null;
string[] appNamespaces = [];
int topN = 15;
bool jsonOutput = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--app-namespaces" when i + 1 < args.Length:
            appNamespaces = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            break;
        case "--top" when i + 1 < args.Length:
            topN = int.Parse(args[++i]);
            break;
        case "--json":
            jsonOutput = true;
            break;
        default:
            if (!args[i].StartsWith('-'))
                traceFile = args[i];
            break;
    }
}

if (traceFile is null)
{
    Console.Error.WriteLine("Usage: dotnet run -- <trace.speedscope.json> [--app-namespaces NS1,NS2] [--top N] [--json]");
    return 1;
}

// --- Categorize a frame ---

string Categorize(string name)
{
    if (string.IsNullOrWhiteSpace(name))
        return CatUnknown;

    // 1. App code (highest priority)
    foreach (var ns in appNamespaces)
        if (name.StartsWith(ns, StringComparison.Ordinal))
            return CatApp;

    // 2. MAUI Framework
    foreach (var p in mauiPrefixes)
        if (name.StartsWith(p, StringComparison.Ordinal))
            return CatMaui;

    // 3. .NET Bindings
    foreach (var p in bindingsPrefixes)
        if (name.StartsWith(p, StringComparison.Ordinal))
            return CatBindings;
    foreach (var p in bindingsManagedPrefixes)
        if (name.StartsWith(p, StringComparison.Ordinal))
            return CatBindings;

    // 4. .NET Runtime
    foreach (var m in runtimeNativeMarkers)
        if (name.Contains(m, StringComparison.Ordinal))
            return CatRuntime;
    foreach (var p in runtimePrefixes)
        if (name.StartsWith(p, StringComparison.Ordinal))
            return CatRuntime;

    // 5. Platform/OS
    var lower = name.ToLowerInvariant();
    foreach (var p in platformPrefixes)
        if (lower.StartsWith(p, StringComparison.Ordinal))
            return CatPlatform;

    // 6. BCL / Third-party (remaining managed frames)
    if (name.Contains('.') && !name.StartsWith('['))
        return CatBcl;

    return CatUnknown;
}

// --- Parse and analyze ---

using var stream = File.OpenRead(traceFile);
using var doc = JsonDocument.Parse(stream);
var root = doc.RootElement;

// Read shared frames
var sharedFrames = new List<string>();
if (root.TryGetProperty("shared", out var shared) && shared.TryGetProperty("frames", out var sharedFramesEl))
{
    foreach (var frame in sharedFramesEl.EnumerateArray())
        sharedFrames.Add(frame.GetProperty("name").GetString() ?? "");
}

var categoryTotals = new Dictionary<string, double>();
var selfTimes = new Dictionary<string, double>();

if (root.TryGetProperty("profiles", out var profiles))
{
    foreach (var profile in profiles.EnumerateArray())
    {
        // Determine frames source
        var frames = sharedFrames;
        if (frames.Count == 0 && profile.TryGetProperty("frames", out var profileFrames))
        {
            frames = new List<string>();
            foreach (var frame in profileFrames.EnumerateArray())
                frames.Add(frame.GetProperty("name").GetString() ?? "");
        }

        var profileType = profile.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : "";

        if (profileType is "sampled" or "evented" && profile.TryGetProperty("samples", out var samples))
        {
            var weights = profile.TryGetProperty("weights", out var weightsEl) ? weightsEl : default;
            int sampleIdx = 0;

            foreach (var sample in samples.EnumerateArray())
            {
                double weight = 1;
                if (weights.ValueKind == JsonValueKind.Array)
                {
                    int wi = 0;
                    foreach (var w in weights.EnumerateArray())
                    {
                        if (wi == sampleIdx) { weight = w.GetDouble(); break; }
                        wi++;
                    }
                }

                // The last frame in the stack is the leaf (self)
                int lastIdx = -1;
                foreach (var frameIdx in sample.EnumerateArray())
                    lastIdx = frameIdx.GetInt32();

                if (lastIdx >= 0 && lastIdx < frames.Count)
                {
                    var name = frames[lastIdx];
                    var cat = Categorize(name);

                    selfTimes.TryGetValue(name, out var prev);
                    selfTimes[name] = prev + weight;

                    categoryTotals.TryGetValue(cat, out var catPrev);
                    categoryTotals[cat] = catPrev + weight;
                }

                sampleIdx++;
            }
        }
    }
}

double grandTotal = categoryTotals.Values.Sum();
if (grandTotal == 0)
{
    Console.Error.WriteLine("No samples found in the trace.");
    return 1;
}

// --- Build entries ---

var entries = selfTimes
    .Select(kv => (Name: kv.Key, SelfSamples: (int)kv.Value, Category: Categorize(kv.Key)))
    .ToList();

// --- Output ---

if (jsonOutput)
{
    using var ms = new MemoryStream();
    using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
    {
        writer.WriteStartObject();
        writer.WriteNumber("total_samples", (int)grandTotal);

        writer.WriteStartArray("categories");
        foreach (var cat in displayOrder)
        {
            if (categoryTotals.TryGetValue(cat, out var count) && count > 0)
            {
                writer.WriteStartObject();
                writer.WriteString("category", cat);
                writer.WriteNumber("samples", (int)count);
                writer.WriteNumber("percentage", Math.Round(count / grandTotal * 100, 1));
                writer.WriteEndObject();
            }
        }
        writer.WriteEndArray();

        writer.WriteStartObject("top_methods");
        foreach (var cat in displayOrder)
        {
            var catEntries = entries
                .Where(e => e.Category == cat)
                .OrderByDescending(e => e.SelfSamples)
                .Take(topN)
                .ToList();
            if (catEntries.Count == 0) continue;

            writer.WriteStartArray(cat);
            foreach (var e in catEntries)
            {
                writer.WriteStartObject();
                writer.WriteString("name", e.Name);
                writer.WriteNumber("self_samples", e.SelfSamples);
                writer.WriteNumber("percentage", Math.Round(e.SelfSamples / grandTotal * 100, 1));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    Console.WriteLine(System.Text.Encoding.UTF8.GetString(ms.ToArray()));
}
else
{
    Console.WriteLine(new string('=', 72));
    Console.WriteLine("MAUI STARTUP CPU DISTRIBUTION");
    Console.WriteLine(new string('=', 72));
    Console.WriteLine();
    Console.WriteLine($"{"Category",-25} {"Samples",10} {"Pct",8}");
    Console.WriteLine(new string('-', 45));

    foreach (var cat in displayOrder)
    {
        if (categoryTotals.TryGetValue(cat, out var count) && count > 0)
        {
            var pct = count / grandTotal * 100;
            Console.WriteLine($"{cat,-25} {count,10:F0} {pct,7:F1}%");
        }
    }

    Console.WriteLine(new string('-', 45));
    Console.WriteLine($"{"Total",-25} {grandTotal,10:F0} {"100.0%",8}");
    Console.WriteLine();

    foreach (var cat in displayOrder)
    {
        var catEntries = entries
            .Where(e => e.Category == cat)
            .OrderByDescending(e => e.SelfSamples)
            .Take(topN)
            .ToList();

        if (catEntries.Count == 0) continue;

        Console.WriteLine($"── Top {Math.Min(topN, catEntries.Count)} in {cat} ──");
        foreach (var e in catEntries)
        {
            var pct = e.SelfSamples / grandTotal * 100;
            var displayName = e.Name.Length <= 80 ? e.Name : e.Name[..77] + "...";
            Console.WriteLine($"  {e.SelfSamples,8} ({pct,5:F1}%)  {displayName}");
        }
        Console.WriteLine();
    }
}

return 0;
