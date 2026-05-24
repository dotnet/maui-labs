namespace Microsoft.Maui.AI.Attributes;

/// <summary>
/// Marks a service type whose <see cref="ExportAIFunctionAttribute"/>-decorated methods should
/// be included in an <see cref="AIToolContext"/>. Apply this attribute to a partial class that
/// inherits from <see cref="AIToolContext"/> — the source generator will scan the specified type
/// and emit tool creation code.
/// </summary>
/// <remarks>
/// Multiple <see cref="AIToolSourceAttribute"/> instances can be applied to a single context
/// to aggregate tools from several service types.
/// <para>
/// Use <see cref="IncludeTools"/> or <see cref="ExcludeTools"/> to control which tools from
/// the source type are included. When <see cref="IncludeTools"/> is set, only listed tools
/// are emitted. When <see cref="ExcludeTools"/> is set, listed tools are omitted. Setting both
/// is a compile-time error.
/// </para>
/// <code>
/// [AIToolSource(typeof(PollServiceTools), IncludeTools = [
///     nameof(PollServiceTools.GetPollResults),
///     nameof(PollServiceTools.GetAllPollResults)])]
/// public partial class ReadOnlyPollContext : AIToolContext { }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class AIToolSourceAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance targeting the specified service type.
    /// </summary>
    /// <param name="sourceType">
    /// The type whose <see cref="ExportAIFunctionAttribute"/>-decorated methods
    /// should be discovered by the source generator.
    /// </param>
    public AIToolSourceAttribute(Type sourceType)
    {
        SourceType = sourceType ?? throw new ArgumentNullException(nameof(sourceType));
    }

    /// <summary>
    /// Gets the service type whose methods are scanned for <see cref="ExportAIFunctionAttribute"/>.
    /// </summary>
    public Type SourceType { get; }

    /// <summary>
    /// When set, only the listed method/property names are included as tools.
    /// Use <c>nameof()</c> for compile-time safety. Cannot be combined with <see cref="ExcludeTools"/>.
    /// </summary>
    public string[]? IncludeTools { get; set; }

    /// <summary>
    /// When set, the listed method/property names are excluded from the tool set.
    /// Use <c>nameof()</c> for compile-time safety. Cannot be combined with <see cref="IncludeTools"/>.
    /// </summary>
    public string[]? ExcludeTools { get; set; }
}
