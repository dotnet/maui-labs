using Microsoft.Extensions.Logging;

namespace AIExtensions.Sample.DIParameters;

/// <summary>
/// A translator service. Resolved via DI — the <c>[FromServices]</c> attribute
/// on the parameter tells the generator to inject it from the service provider
/// at invocation time rather than expecting the AI model to supply it.
/// </summary>
public interface ITranslator
{
    string Translate(string text, bool verbose = false);
}

/// <summary>
/// Takes <see cref="ILogger{T}"/> as a constructor dependency — this exercises
/// <em>nested</em> DI: the tool method asks for <c>ITranslator</c>, which the
/// container resolves to this type, which in turn needs a logger. The logger
/// is produced by the standard <c>AddLogging()</c> registration and appears
/// in the console output, making the resolution chain visible.
/// </summary>
public sealed class PigLatinTranslator(ILogger<PigLatinTranslator> logger) : ITranslator
{
    public string Translate(string text, bool verbose = false)
    {
        var words = text.Split(' ');

        if (verbose)
        {
            logger.LogInformation(
                "Translating {WordCount} word(s) to pig latin...",
                words.Length);
        }

        return string.Join(' ', words.Select(w =>
            w.Length > 1 ? w[1..] + w[0] + "ay" : w));
    }
}
