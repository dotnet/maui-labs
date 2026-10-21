#if BARISTA_PERF
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using BaristaComparison;
using CometBaristaNotes.Comparison;
using CometBaristaNotes.Services;
using CometSamples.BaristaNotes;

namespace CometComposeProbe.BaristaNotes.Comparison;

internal static class BaristaFixtureHost
{
    static bool _initialized;
    static bool _started;
    static CometFixtureSession? _session;
    static Task _operation = Task.CompletedTask;

    internal static string AppData(Context context) => context.FilesDir?.AbsolutePath ??
        throw new InvalidOperationException("Android app storage is not available.");

    internal static string ControlDirectory(Context context) =>
        Path.Combine(AppData(context), "barista-comparison-control");

    internal static string SelectionPath(Context context) => Path.Combine(ControlDirectory(context), "selection.json");
    internal static string StatusPath(Context context) => Path.Combine(ControlDirectory(context), "status.json");

    internal static bool HasExternalPreferences(Context context, string name) =>
        File.Exists(Path.Combine(context.ApplicationInfo?.DataDir ??
            throw new InvalidOperationException("Android data directory is missing."), "shared_prefs", name + ".xml"));

    internal static void Initialize(Context context)
    {
        if (_initialized)
            return;
        var path = SelectionPath(context);
        if (File.Exists(path))
        {
            var selection = CometFixtureSession.ReadSelection(path);
            if (!CometFixtureSession.IsReview(selection))
                _session = new CometFixtureSession(AppData(context), selection, BaristaAppStorage.Current,
                    name => HasExternalPreferences(context, name));
        }
        _initialized = true;
    }

    internal static Task Start(Activity activity, BaristaNotesApp root)
    {
        if (_session is null || _started)
            return _operation;
        _started = true;
        var session = _session;
        if (root.HasActiveActivityFilters)
            throw new InvalidOperationException("A fixture must start with the source Activity All filter.");
        var context = activity.ApplicationContext ??
            throw new InvalidOperationException("Android application context is missing.");
        WriteStatus(context, session.Selection, session.Selection.Mode == "run" ? "RunSelected" : "Running");
        // Only domain mutations use the activity thread. File parsing and fresh readback stay off it.
        var adapter = new UiAdapter(new CometFixtureAdapter(session.Store.DatabasePath, root.Services),
            action => activity.RunOnUiThread(action));
        _operation = Task.Run(async () =>
        {
            try
            {
                var actual = await session.StartAsync(adapter, count => LoadInput(context, count));
                if (actual is not null)
                {
                    var exported = Export(context, session.Selection, actual);
                    WriteStatus(context, session.Selection, "RanToCompletion", readback: exported);
                }
            }
            catch (Exception error)
            {
                WriteStatus(context, session.Selection, "Faulted", error.ToString());
                throw;
            }
        });
        return _operation;
    }

    internal static Task WaitForIdleAsync() => _operation;

    internal static byte[] LoadInput(Context context, int count)
    {
        _ = FixtureValidation.ExpectedInputHash(count);
        using var input = context.Assets!.Open($"barista-fixtures/barista-perf-v2-{count}.json");
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    internal static string Export(Context context, FixtureSelection selection, FixtureReadback actual)
    {
        var directory = Path.Combine(ControlDirectory(context), "exports");
        Directory.CreateDirectory(directory);
        var token = Guid.NewGuid().ToString("N");
        var path = Path.Combine(directory, selection.Namespace + "-" + token + ".json");
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        output.Write(FixtureValidation.Canonical(actual));
        output.Flush(true);
        return token;
    }

    internal static void WriteStatus(Context context, FixtureSelection selection, string state,
        string? error = null, string? readback = null)
    {
        Directory.CreateDirectory(ControlDirectory(context));
        FixtureStore.AtomicWrite(StatusPath(context), JsonSerializer.SerializeToUtf8Bytes(
            new CometFixtureStatus(selection, state, error, readback), CometFixtureStatusJson.Default.CometFixtureStatus));
    }

    sealed class UiAdapter(IFixtureAdapter inner, Action<Action> dispatch) : IFixtureAdapter
    {
        Task<T> Invoke<T>(Func<Task<T>> action)
        {
            var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            dispatch(async () =>
            {
                try { result.SetResult(await action()); }
                catch (Exception error) { result.SetException(error); }
            });
            return result.Task;
        }

        public Task<string> CreateBeanAsync(FixtureBean value) => Invoke(() => inner.CreateBeanAsync(value));
        public Task<string> CreateBagAsync(FixtureBag value, FixtureIds ids) => Invoke(() => inner.CreateBagAsync(value, ids));
        public Task<string> CreateEquipmentAsync(FixtureEquipment value) => Invoke(() => inner.CreateEquipmentAsync(value));
        public Task<string> CreatePersonAsync(FixturePerson value) => Invoke(() => inner.CreatePersonAsync(value));
        public Task<string> CreateDrinkAsync(FixtureDrink value, FixtureIds ids) => Invoke(() => inner.CreateDrinkAsync(value, ids));
        public Task ApplyPreferencesAsync(FixtureDefinition fixture, FixtureIds ids) =>
            Invoke(async () => { await inner.ApplyPreferencesAsync(fixture, ids); return true; });
        public Task<FixtureReadback> ReadAsync(FixtureIds ids) => inner.ReadAsync(ids);
    }
}
#endif
