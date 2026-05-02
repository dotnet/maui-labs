namespace CometProjectManager.Models;

public class Category
{
	public int ID { get; set; }
	public string Title { get; set; } = string.Empty;
	public string ColorHex { get; set; } = "#FF0000";

	public Color Color => Microsoft.Maui.Graphics.Color.FromArgb(ColorHex);
	public override string ToString() => Title;
}

public class Tag
{
	public int ID { get; set; }
	public string Title { get; set; } = string.Empty;
	public string ColorHex { get; set; } = "#0000FF";
	public bool IsSelected { get; set; }

	public Color DisplayColor => Microsoft.Maui.Graphics.Color.FromArgb(ColorHex);
	public override string ToString() => Title;
}

public class ProjectTask
{
	public int ID { get; set; }
	public string Title { get; set; } = string.Empty;
	public bool IsCompleted { get; set; }
	public int ProjectID { get; set; }
}

public class Project
{
	public int ID { get; set; }
	public string Name { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string Icon { get; set; } = "Folder";
	public int CategoryID { get; set; }
	public Category? Category { get; set; }
	public List<ProjectTask> Tasks { get; set; } = new();
	public List<Tag> Tags { get; set; } = new();

	public override string ToString() => Name;
}

public class CategoryChartData
{
	public string Title { get; set; } = string.Empty;
	public int Count { get; set; }
	public Color Color { get; set; } = Colors.Gray;

	public CategoryChartData(string title, int count, Color color)
	{
		Title = title;
		Count = count;
		Color = color;
	}
}
