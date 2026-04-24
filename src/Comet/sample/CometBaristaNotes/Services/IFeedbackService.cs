namespace CometBaristaNotes.Services;

public interface IFeedbackService
{
	Task ShowSuccess(string message);
	Task ShowError(string message);
	Task ShowInfo(string message);
	Task ShowWarning(string message);
}
