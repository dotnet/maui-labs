using System;

namespace CometBaristaNotes.Models;

public class OperationResult<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
    public string? RecoveryAction { get; init; }
    public string? ErrorCode { get; init; }

    public static OperationResult<T> Ok(T data, string message = "Operation completed successfully")
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data), "Success result must include data");

        return new OperationResult<T>
        {
            Success = true,
            Data = data,
            Message = message,
            ErrorMessage = null,
            RecoveryAction = null
        };
    }

    public static OperationResult<T> Fail(string errorMessage, string? recoveryAction = null, string? errorCode = null)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            throw new ArgumentException("Error message is required for failure result", nameof(errorMessage));

        return new OperationResult<T>
        {
            Success = false,
            Data = default,
            Message = errorMessage,
            ErrorMessage = errorMessage,
            RecoveryAction = recoveryAction,
            ErrorCode = errorCode
        };
    }
}
