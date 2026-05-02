namespace CometTrackizerApp.Models;

public record BudgetByCategory(Category Category, double MonthBills, double MonthBudget);

public enum Category
{
	[Display(Name = "Auto & Transport")]
	AutoTransport,

	Entertainment,

	Security
}
