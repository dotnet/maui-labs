namespace CometMailApp.Models;

public class EmailMessage
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public string Sender { get; set; } = "";
	public string SenderEmail { get; set; } = "";
	public string Subject { get; set; } = "";
	public string Preview { get; set; } = "";
	public string Body { get; set; } = "";
	public DateTime ReceivedAt { get; set; } = DateTime.Now;
	public bool IsRead { get; set; }
	public bool IsStarred { get; set; }
}
