using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.Cli.DevFlow;

/// <summary>
/// Backing logic for <c>maui devflow hook check &lt;event&gt;</c>.
///
/// The dotnet-maui plugin ships tiny platform wrappers (check.sh / check.cmd)
/// that forward to this command. This is where the real work happens:
///   1. If invoked via <c>PostToolUse</c>, peek at the edited file path from
///      stdin and short-circuit if the edit is unrelated to project wiring.
///   2. Enumerate .csproj files in the cwd.
///   3. Ask MSBuild — <c>dotnet msbuild &lt;csproj&gt; -nologo
///      -getProperty:UseMaui,EnableDevFlow -getItem:PackageReference</c> —
///      for authoritative values that already account for
///      Directory.Build.props, Directory.Packages.props, transitive .targets
///      imports, custom SDKs, etc.
///   4. Classify the first matching project as MAUI / flavor / wired.
///   5. Emit a debounced JSON nudge on stdout if the project isn't wired.
///
/// Debounce state lives outside the user's repo — we use
/// <c>${CLAUDE_PLUGIN_DATA}/hook-state/&lt;repo-hash&gt;.json</c> when the
/// plugin host provides it, falling back to the OS temp directory otherwise.
/// This avoids any collision with the repo-level <c>.devflow</c> config file.
///
/// Test override: set <c>MAUI_DEVFLOW_HOOK_STUB=&lt;path-to-json&gt;</c> to
/// feed canned MSBuild output and bypass the <c>dotnet msbuild</c> invocation.
/// </summary>
public static class HookCheckCommand
{
    internal sealed class HookEnvironment
    {
        public required string EventName { get; init; }
        public required string Cwd { get; init; }
        public required TextReader Stdin { get; init; }
        public required TextWriter Stdout { get; init; }
        public required TextWriter Stderr { get; init; }
        public required IReadOnlyDictionary<string, string?> Env { get; init; }

        /// <summary>
        /// When false, <see cref="RunCoreAsync"/> will not attempt to read
        /// <see cref="Stdin"/>. The public <c>RunAsync</c> entry point sets
        /// this based on <see cref="Console.IsInputRedirected"/> so that
        /// interactive invocations (TTY on stdin) don't block waiting for
        /// input that will never arrive.
        /// </summary>
        public bool ReadStdin { get; init; } = true;

        public Func<string, JsonNode?>? MsbuildEvaluator { get; init; }
    }

    public static async Task<int> RunAsync(string eventName, string? cwd = null)
    {
        var env = new HookEnvironment
        {
            EventName = eventName,
            Cwd = cwd ?? Environment.GetEnvironmentVariable("CLAUDE_PROJECT_DIR") ?? Directory.GetCurrentDirectory(),
            Stdin = Console.In,
            Stdout = Console.Out,
            Stderr = Console.Error,
            Env = CaptureEnv(),
            ReadStdin = Console.IsInputRedirected
        };

        try
        {
            return await RunCoreAsync(env).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Never block the host on hook failure.
            try { env.Stderr.WriteLine($"[maui-devflow hook] {ex.Message}"); } catch { }
            return 0;
        }
    }

    internal static async Task<int> RunCoreAsync(HookEnvironment env)
    {
        // Drain stdin (best effort). PostToolUse payload is optional, and
        // when stdin is a TTY (interactive invocation) we must not block
        // waiting for input.
        JsonNode? stdinPayload = null;
        if (env.ReadStdin)
        {
            try
            {
                var raw = await env.Stdin.ReadToEndAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(raw))
                    stdinPayload = JsonNode.Parse(raw);
            }
            catch { /* stdin may be empty or non-JSON */ }
        }

        if (string.Equals(env.EventName, "PostToolUse", StringComparison.OrdinalIgnoreCase) && stdinPayload is not null)
        {
            var editedPath = ExtractEditedPath(stdinPayload);
            if (!string.IsNullOrEmpty(editedPath) && !IsRelevantToWiring(editedPath))
                return 0;
        }

        var csprojs = ListCsprojs(env.Cwd);
        if (csprojs.Count == 0) return 0;

        JsonNode? mauiEval = null;
        foreach (var csproj in csprojs)
        {
            var ev = EvaluateCsproj(env, csproj);
            if (IsMauiProject(ev)) { mauiEval = ev; break; }
        }
        if (mauiEval is null) return 0;

        if (IsDevFlowWired(mauiEval))
        {
            // Already wired — no nudge. Don't record debounce state either so
            // un-wiring cleanly re-arms the nudge next time.
            return 0;
        }

        var flavor = DetectFlavor(mauiEval);
        var flavorLabel = flavor switch
        {
            "blazor-gtk" => "Blazor GTK",
            "gtk"        => "GTK",
            "blazor"     => "Blazor hybrid",
            _            => "standard"
        };
        var message =
            $"🔧 MAUI project detected ({flavorLabel}) but DevFlow is not wired. " +
            $"Say \"set up DevFlow\" and I'll run the maui-devflow-setup skill, " +
            $"or run `maui devflow diagnose` for details.";
        Emit(env, $"unwired-{flavor}", message);
        return 0;
    }

    // ---------- stdin helpers ----------

    private static string? ExtractEditedPath(JsonNode payload)
    {
        var input = payload["tool_input"] ?? payload["toolInput"];
        if (input is null) return null;
        return input["file_path"]?.GetValue<string>()
            ?? input["filePath"]?.GetValue<string>()
            ?? input["path"]?.GetValue<string>();
    }

    private static bool IsRelevantToWiring(string editedPath)
    {
        var name = Path.GetFileName(editedPath);
        if (string.IsNullOrEmpty(name)) return false;
        var lower = name.ToLowerInvariant();
        return lower == "mauiprogram.cs"
            || lower.EndsWith(".csproj", StringComparison.Ordinal)
            || lower == "directory.packages.props"
            || lower == "directory.build.props"
            || lower == "directory.build.targets";
    }

    // ---------- project enumeration ----------

    private static List<string> ListCsprojs(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly).ToList();
        }
        catch { return new List<string>(); }
    }

    // ---------- MSBuild evaluation ----------

    internal static JsonNode? EvaluateCsproj(HookEnvironment env, string csproj)
    {
        // Stub override for tests.
        if (env.Env.TryGetValue("MAUI_DEVFLOW_HOOK_STUB", out var stubPath) && !string.IsNullOrEmpty(stubPath))
        {
            try
            {
                var raw = File.ReadAllText(stubPath);
                return JsonNode.Parse(raw);
            }
            catch { return null; }
        }

        if (env.MsbuildEvaluator is not null)
            return env.MsbuildEvaluator(csproj);

        try
        {
            var psi = new ProcessStartInfo("dotnet",
                new[] { "msbuild", csproj, "-nologo",
                        "-getProperty:UseMaui,EnableDevFlow",
                        "-getItem:PackageReference" })
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return null;

            var output = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(15_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }

            if (process.ExitCode != 0) return null;

            var text = output.ToString().Trim();
            if (string.IsNullOrEmpty(text)) return null;
            return JsonNode.Parse(text);
        }
        catch { return null; }
    }

    // ---------- classification ----------

    internal static IReadOnlyList<string> PackageIdentities(JsonNode? eval)
    {
        if (eval is null) return Array.Empty<string>();
        var list = new List<string>();
        var refs = eval["Items"]?["PackageReference"];
        if (refs is JsonArray arr)
        {
            foreach (var item in arr)
            {
                var id = item?["Identity"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(id)) list.Add(id);
            }
        }
        return list;
    }

    internal static bool IsMauiProject(JsonNode? eval)
    {
        if (eval is null) return false;
        var useMaui = eval["Properties"]?["UseMaui"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(useMaui) && string.Equals(useMaui, "true", StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var id in PackageIdentities(eval))
        {
            if (id.StartsWith("Microsoft.Maui.", StringComparison.OrdinalIgnoreCase)) return true;
            if (id.StartsWith("Platform.Maui.", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    internal static bool IsDevFlowWired(JsonNode? eval)
    {
        if (eval is null) return false;
        foreach (var id in PackageIdentities(eval))
        {
            if (id.StartsWith("Microsoft.Maui.DevFlow.", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    internal static string DetectFlavor(JsonNode? eval)
    {
        var ids = PackageIdentities(eval);
        var hasBlazor = ids.Any(id => id.Equals("Microsoft.AspNetCore.Components.WebView.Maui", StringComparison.OrdinalIgnoreCase));
        var hasGtk = ids.Any(id =>
            id.StartsWith("Platform.Maui.Linux.Gtk", StringComparison.OrdinalIgnoreCase) ||
            id.Equals("Microsoft.Maui.DevFlow.Agent.Gtk", StringComparison.OrdinalIgnoreCase) ||
            id.Equals("Microsoft.Maui.DevFlow.Blazor.Gtk", StringComparison.OrdinalIgnoreCase));
        if (hasGtk && hasBlazor) return "blazor-gtk";
        if (hasGtk) return "gtk";
        if (hasBlazor) return "blazor";
        return "standard";
    }

    // ---------- emit / debounce ----------

    private static void Emit(HookEnvironment env, string state, string message)
    {
        var stateFile = ResolveDebounceStateFile(env);
        if (stateFile is not null)
        {
            try
            {
                if (File.Exists(stateFile))
                {
                    var prev = JsonNode.Parse(File.ReadAllText(stateFile));
                    var prevState = prev?["lastState"]?.GetValue<string>();
                    var prevEvent = prev?["lastEvent"]?.GetValue<string>();
                    if (prevState == state && prevEvent == env.EventName)
                        return;
                }
            }
            catch { /* treat as no prior state */ }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
                var payload = new JsonObject
                {
                    ["lastState"] = state,
                    ["lastEvent"] = env.EventName,
                    ["at"] = DateTimeOffset.UtcNow.ToString("o")
                };
                File.WriteAllText(stateFile, payload.ToJsonString());
            }
            catch { /* best-effort */ }
        }

        // Dual schema: `context` is the broadly-accepted field, while
        // `hookSpecificOutput.additionalContext` matches the SessionStart
        // convention that both Claude Code and Copilot CLI understand.
        var output = new JsonObject
        {
            ["context"] = message,
            ["hookSpecificOutput"] = new JsonObject
            {
                ["hookEventName"] = env.EventName,
                ["additionalContext"] = message
            }
        };
        env.Stdout.Write(output.ToJsonString());
    }

    /// <summary>
    /// Pick a debounce state file path that lives outside the user's repo.
    /// Preference order: <c>CLAUDE_PLUGIN_DATA</c> (per-user persistent plugin
    /// state), otherwise a per-repo file under the OS temp directory. Never
    /// writes inside the repo itself so there's no clash with the
    /// <c>.devflow</c> config file and no need to touch the user's gitignore.
    /// </summary>
    private static string? ResolveDebounceStateFile(HookEnvironment env)
    {
        var repoHash = HashRepoPath(env.Cwd);

        env.Env.TryGetValue("CLAUDE_PLUGIN_DATA", out var pluginData);
        if (!string.IsNullOrEmpty(pluginData))
            return Path.Combine(pluginData!, "hook-state", repoHash + ".json");

        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "maui-devflow-hooks");
            return Path.Combine(tempDir, repoHash + ".json");
        }
        catch
        {
            return null;
        }
    }

    private static string HashRepoPath(string cwd)
    {
        var normalized = Path.GetFullPath(cwd).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (OperatingSystem.IsWindows())
            normalized = normalized.ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var sb = new StringBuilder(16);
        for (int i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();
    }

    // ---------- env capture ----------

    private static IReadOnlyDictionary<string, string?> CaptureEnv()
    {
        var keys = new[]
        {
            "MAUI_DEVFLOW_HOOK_STUB",
            "CLAUDE_PLUGIN_DATA",
            "CLAUDE_PROJECT_DIR"
        };
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in keys)
            dict[k] = Environment.GetEnvironmentVariable(k);
        return dict;
    }
}
