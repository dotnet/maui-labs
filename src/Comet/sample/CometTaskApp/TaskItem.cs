namespace CometTaskApp;

public enum TaskPriority
{
	Low,
	Medium,
	High,
	Critical
}

public enum TaskCategory
{
	Personal,
	Work,
	Shopping,
	Health,
	Learning,
	Other
}

public class TaskItem
{
	public string Id { get; set; } = Guid.NewGuid().ToString();
	public string Title { get; set; } = "";
	public string Description { get; set; } = "";
	public bool IsCompleted { get; set; }
	public TaskPriority Priority { get; set; }
	public TaskCategory Category { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.Now;
	public DateTime? DueDate { get; set; }
}
