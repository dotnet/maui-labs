namespace CometBaristaNotes.Models;

public class OperationResult<T>
{
	public bool Success { get; set; }
	public T? Data { get; set; }
	public string? Message { get; set; } = null;
	public string? ErrorMessage { get; set; } = null;

	public static OperationResult<T> Ok(T data, string? message = null)
		=> new() { Success = true, Data = data, Message = message };

	public static OperationResult<T> Fail(string error)
		=> new() { Success = false, ErrorMessage = error };
}
