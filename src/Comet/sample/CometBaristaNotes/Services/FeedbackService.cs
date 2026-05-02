namespace CometBaristaNotes.Services;

public class FeedbackService : IFeedbackService
{
	private readonly SemaphoreSlim _semaphore = new(1, 1);

	public Task ShowSuccess(string message) => ShowAlert("Success", message);
	public Task ShowError(string message) => ShowAlert("Error", message);
	public Task ShowInfo(string message) => ShowAlert("Info", message);
	public Task ShowWarning(string message) => ShowAlert("Warning", message);

	private async Task ShowAlert(string title, string message)
	{
		await _semaphore.WaitAsync();
		try
		{
			await PageHelper.DisplayAlertAsync(title, message, "OK");
		}
		finally
		{
			_semaphore.Release();
		}
	}
}
