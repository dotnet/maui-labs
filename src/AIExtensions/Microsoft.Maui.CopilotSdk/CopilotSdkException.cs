namespace Microsoft.Maui.CopilotSdk;

/// <summary>
/// Thrown when the GitHub Copilot runtime reports an error while producing a response.
/// </summary>
public sealed class CopilotSdkException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="CopilotSdkException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public CopilotSdkException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CopilotSdkException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying exception.</param>
    public CopilotSdkException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CopilotSdkException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The runtime error code, when available.</param>
    /// <param name="errorType">The runtime error type, when available.</param>
    public CopilotSdkException(string message, string? errorCode, string? errorType)
        : base(message)
    {
        ErrorCode = errorCode;
        ErrorType = errorType;
    }

    /// <summary>The runtime error code, when available.</summary>
    public string? ErrorCode { get; }

    /// <summary>The runtime error type, when available.</summary>
    public string? ErrorType { get; }
}
