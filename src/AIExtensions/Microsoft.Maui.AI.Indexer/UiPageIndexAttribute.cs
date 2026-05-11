namespace Microsoft.Maui.AI.Indexer;

/// <summary>
/// Marks a generated class as a UI page index containing a Markdown representation.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class UiPageIndexAttribute : Attribute
{
    public UiPageIndexAttribute(string pageName)
    {
        PageName = pageName;
    }

    public string PageName { get; }
    public string? Route { get; set; }
    public string? FilePath { get; set; }
}
