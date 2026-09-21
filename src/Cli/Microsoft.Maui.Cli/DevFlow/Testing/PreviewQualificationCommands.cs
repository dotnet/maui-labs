using System.CommandLine;
using System.Text.Json;

namespace Microsoft.Maui.Cli.DevFlow.Testing;

internal static class PreviewQualificationCommands
{
    public static Command Create()
    {
        var command = new Command("qualification", "Experimental diagnostic metric checks; never certifies a platform");
        var input = new Argument<string>("input") { Description = "Exact schema-1 JSON metric input; at most 1 MiB" };
        var assess = new Command("assess", "Read supplied metrics without app, device, broker, or network access") { input };
        assess.SetAction(async (parse, cancellationToken) =>
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("DEVFLOW_PREVIEW_QUALIFICATION"),
                    "true", StringComparison.OrdinalIgnoreCase) ||
                (Environment.GetEnvironmentVariable("DEVFLOW_PREVIEW_KILL_SWITCHES") ?? "")
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Contains("qualification", StringComparer.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("preview-disabled: explicitly enable DEVFLOW_PREVIEW_QUALIFICATION.");
                return 1;
            }
            try
            {
                var result = await AssessFileAsync(parse.GetValue(input)!, cancellationToken).ConfigureAwait(false);
                Console.WriteLine(JsonSerializer.Serialize(result, PreviewQualificationJsonContext.Default.PreviewQualificationResult));
                // Numeric gates may pass, but this command never reports successful qualification.
                return 2;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                Console.Error.WriteLine("qualification-input-invalid: supply one bounded complete schema-1 metric document.");
                return 1;
            }
        });
        command.Add(assess);
        return command;
    }

    internal static async Task<PreviewQualificationResult> AssessFileAsync(string path, CancellationToken cancellationToken)
    {
        const int limit = 1024 * 1024;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = new byte[limit + 1];
        var length = await stream.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, cancellationToken)
            .ConfigureAwait(false);
        if (length > limit)
            throw new ArgumentException("Qualification input exceeds 1 MiB.");
        var input = JsonSerializer.Deserialize(bytes.AsSpan(0, length),
            PreviewQualificationJsonContext.Default.PreviewQualificationInput)
            ?? throw new JsonException();
        return PreviewQualification.Evaluate(input);
    }
}
