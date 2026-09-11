using Android.App;
using Android.OS;
using Android.Runtime;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.Testing;

public abstract class MauiTestInstrumentation : Instrumentation
{
    public const string ArgumentsExtra = "mtp-arguments";

    private Bundle? _arguments;

    protected MauiTestInstrumentation(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected abstract MauiTestApp CreateMauiTestApp();

    public override void OnCreate(Bundle? arguments)
    {
        _arguments = arguments;
        base.OnCreate(arguments);
        Start();
    }

    public override async void OnStart()
    {
        base.OnStart();

        var bundle = new Bundle();
        try
        {
            using var app = CreateMauiTestApp();
            var writablePath = global::Android.App.Application.Context
                .GetExternalFilesDir(null)?.AbsolutePath ?? Path.GetTempPath();
            var resultsPath = Path.Combine(
                writablePath,
                app.Services.GetRequiredService<MauiTestAppOptions>().ResultsDirectoryName);
            var consumer = new MauiTestResultConsumer();
            consumer.TestCompleted += result =>
            {
                var status = new Bundle();
                status.PutString("event", "finish");
                status.PutString("test", result.Uid);
                status.PutString("name", result.Name);
                status.PutString("class", result.ClassName);
                status.PutString("outcome", result.Outcome);
                if (result.Message is not null)
                {
                    status.PutString("message-b64", Encode(result.Message));
                }
                if (result.StackTrace is not null)
                {
                    status.PutString("stack-b64", Encode(result.StackTrace));
                }
                var statusCode = result.Outcome switch
                {
                    "failed" => (Result)(-2),
                    "skipped" => (Result)(-3),
                    _ => (Result)0,
                };
                SendStatus(statusCode, status);
            };

            var argumentsJson = _arguments?.GetString(ArgumentsExtra);
            var arguments = string.IsNullOrWhiteSpace(argumentsJson)
                ? MauiTestArgumentParser.Parse(_arguments?.GetString("args"))
                : JsonSerializer.Deserialize(
                    argumentsJson,
                    MauiTestJsonSerializerContext.Default.StringArray)
                    ?? throw new FormatException(
                        $"Instrumentation extra '{ArgumentsExtra}' must contain a JSON string array.");
            var result = await app.RunAsync(arguments, resultsPath, consumer);
            bundle.PutInt("passed", result.Passed);
            bundle.PutInt("failed", result.Failed);
            bundle.PutInt("skipped", result.Skipped);
            bundle.PutString("resultsPath", result.TrxReportPath);
            Finish(Result.Ok, bundle);
        }
        catch (Exception ex)
        {
            bundle.PutString("error", ex.ToString());
            Finish(Result.Canceled, bundle);
        }
        finally
        {
            _arguments?.Dispose();
            _arguments = null;
        }
    }

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
}

[JsonSerializable(typeof(string[]))]
internal partial class MauiTestJsonSerializerContext : JsonSerializerContext
{
}
