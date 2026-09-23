namespace ChatClientPlayground.Models;

/// <summary>Identifies the provider category selected by the user.</summary>
public enum ChatClientKind
{
    /// <summary>A provider that runs locally on the current device.</summary>
    Local,

    /// <summary>A remotely hosted provider configured through local user secrets.</summary>
    Cloud,
}
