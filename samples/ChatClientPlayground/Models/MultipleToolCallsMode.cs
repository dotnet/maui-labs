namespace ChatClientPlayground.Models;

/// <summary>Represents an optional boolean setting for multiple tool calls.</summary>
public enum MultipleToolCallsMode
{
    /// <summary>Leaves the provider option unset.</summary>
    Default,

    /// <summary>Explicitly allows multiple tool calls.</summary>
    Allow,

    /// <summary>Explicitly disallows multiple tool calls.</summary>
    Disallow,
}
