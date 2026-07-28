using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.AI.Attributes;

namespace AIExtensions.Sample.DIParameters;

/// <summary>
/// Options the AI model supplies as part of the tool call. A plain record
/// (not an interface/abstract class) so the generator schemas it by default
/// — no explicit attribute is needed to keep it in the tool schema.
/// </summary>
public sealed record TranslationOptions(
    bool Verbose = false);

public class TranslatorService(ILogger<TranslatorService> logger)
{
    [Description("Translates a phrase using the configured translator.")]
    [ExportAIFunction("translate")]
    public string Translate(
        [Description("The text to translate")] string text,
        // Explicit DI: [FromServices] resolves from IServiceProvider and
        // excludes the parameter from the tool schema.
        [FromServices] ITranslator translator,
        // Explicit keyed DI: resolved via [FromKeyedServices].
        [FromKeyedServices("premium")] IModelProvider model,
        // A plain record — no attribute needed. The AI fills it in per call.
        TranslationOptions options,
        // Direct CancellationToken support — never appears in the schema.
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Log what DI resolved and what the model filled in, so the sample
        // demonstrates parameter binding even when the LLM paraphrases the
        // tool result.
        if (options.Verbose)
        {
            logger.LogInformation(
                "translate tool called: text={Text}, translator={Translator}, model={Model}, options={Options}",
                text,
                translator.GetType().Name,
                model.Name,
                options);
        }

        return translator.Translate(text, options.Verbose);
    }
}
