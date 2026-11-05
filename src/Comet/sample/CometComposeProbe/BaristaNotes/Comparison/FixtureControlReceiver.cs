using System;
using Android.App;
using Android.Content;
using Android.Runtime;
#if BARISTA_PERF
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BaristaComparison;
using CometBaristaNotes.Comparison;
#endif

namespace CometComposeProbe.BaristaNotes.Comparison;

// Keep the JNI type in every configuration; only comparison manifests register the control.
[Register("com/comet/baristacomparison/FixtureControlReceiver")]
#if BARISTA_PERF
[BroadcastReceiver(Name = "com.comet.baristacomparison.FixtureControlReceiver",
    Enabled = true, Exported = true, Process = ":fixturecontrol", Permission = "android.permission.DUMP")]
#endif
public sealed class FixtureControlReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
#if BARISTA_PERF
        var pending = GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                if (context is null || intent is null)
                    throw new InvalidOperationException("Fixture control requires an Android context and intent.");
                var result = await ExecuteAsync(context, intent);
                pending.SetResult(Result.Ok, result, null);
            }
            catch (Exception error)
            {
                Android.Util.Log.Error("CometFixtureControl", error.ToString());
                pending.SetResult(Result.Canceled, "Rejected: " + error.Message, null);
            }
            finally
            {
                pending.Finish();
            }
        });
#else
        SetResult(Result.Canceled, "Fixture controls are not enabled.", null);
#endif
    }

#if BARISTA_PERF
    static async Task<string> ExecuteAsync(Context context, Intent intent)
    {
        switch (intent.Action)
        {
            case "com.comet.baristacomparison.SELECT":
                if (!intent.HasExtra("namespace") || !intent.HasExtra("count") || !intent.HasExtra("mode"))
                    throw new InvalidDataException("namespace, count and mode are required.");
                var selection = new FixtureSelection(
                    intent.GetStringExtra("namespace") ?? throw new InvalidDataException("Namespace is null."),
                    intent.GetIntExtra("count", -1),
                    intent.GetStringExtra("mode") ?? throw new InvalidDataException("Mode is null."));
                if (!CometFixtureSession.IsReview(selection))
                    CometFixtureSession.Preflight(BaristaFixtureHost.AppData(context), selection,
                        name => BaristaFixtureHost.HasExternalPreferences(context, name));
                var manager = context.GetSystemService(Context.ActivityService) as ActivityManager ??
                    throw new InvalidOperationException("Cannot inspect the app process.");
                var processes = manager.RunningAppProcesses ??
                    throw new InvalidOperationException("Cannot establish that the main process is stopped.");
                if (processes.Any(process => process.ProcessName == context.PackageName))
                    throw new InvalidOperationException("Stop the owned main process before changing selection.");
                Directory.CreateDirectory(BaristaFixtureHost.ControlDirectory(context));
                FixtureStore.AtomicWrite(BaristaFixtureHost.SelectionPath(context),
                    JsonSerializer.SerializeToUtf8Bytes(selection, FixtureJson.Default.FixtureSelection));
                BaristaFixtureHost.WriteStatus(context, selection, "Selected");
                return "Selection saved";

            case "com.comet.baristacomparison.STATUS":
                var status = JsonSerializer.Deserialize(File.ReadAllBytes(BaristaFixtureHost.StatusPath(context)),
                    CometFixtureStatusJson.Default.CometFixtureStatus) ??
                    throw new InvalidDataException("Comparison status is null.");
                ProvisioningReceipt? receipt = null;
                if (!CometFixtureSession.IsReview(status.Selection))
                {
                    var directory = FixtureStore.NamespacePath(BaristaFixtureHost.AppData(context), status.Selection);
                    var receiptPath = Path.Combine(directory, "receipt.json");
                    if (File.Exists(receiptPath))
                        receipt = JsonSerializer.Deserialize(File.ReadAllBytes(receiptPath), FixtureJson.Default.ProvisioningReceipt)
                            ?? throw new InvalidDataException("Comparison receipt is null.");
                }
                return JsonSerializer.Serialize(new CometFixtureReport(status, receipt),
                    CometFixtureStatusJson.Default.CometFixtureReport);

            case "com.comet.baristacomparison.EXPORT":
                var selected = CometFixtureSession.ReadSelection(BaristaFixtureHost.SelectionPath(context));
                if (CometFixtureSession.IsReview(selected))
                    throw new InvalidOperationException("Review data cannot be exported as a fixture.");
                var validate = selected with { Mode = "validate" };
                CometFixtureSession.Preflight(BaristaFixtureHost.AppData(context), validate,
                    name => BaristaFixtureHost.HasExternalPreferences(context, name));
                var store = new FixtureStore(BaristaFixtureHost.AppData(context), validate);
                var fixture = FixtureValidation.Parse(
                    BaristaFixtureHost.LoadInput(context, validate.DrinkCount), validate.DrinkCount);
                var actual = await store.ValidateAsync(fixture, new CometFixtureAdapter(store.DatabasePath));
                return "Exported " + BaristaFixtureHost.Export(context, validate, actual);

            case "com.comet.baristacomparison.READ_EXPORT":
                var exportSelection = CometFixtureSession.ReadSelection(BaristaFixtureHost.SelectionPath(context));
                var chunk = CometFixtureExports.Read(BaristaFixtureHost.AppData(context), exportSelection,
                    intent.GetStringExtra("token") ?? throw new InvalidDataException("Export token is missing."),
                    intent.GetIntExtra("offset", -1), intent.GetIntExtra("length", -1));
                return JsonSerializer.Serialize(chunk, CometFixtureStatusJson.Default.CometFixtureChunk);

            default:
                throw new InvalidDataException("Unknown fixture control action.");
        }
    }
#endif
}
