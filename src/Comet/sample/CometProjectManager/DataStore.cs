using Comet.Reactive;
using CometProjectManager.Models;

namespace CometProjectManager;

/// <summary>
/// In-memory data store matching the MAUI template's SeedData.json exactly.
/// All state changes trigger reactive UI updates via Signal<T>.
/// </summary>
public class DataStore
{
	public static DataStore Instance { get; } = new();

	private int _nextProjectId = 5;
	private int _nextTaskId = 13;
	private int _nextCategoryId = 5;
	private int _nextTagId = 6;

	// Match the MAUI template seed data categories
	public readonly Signal<List<Category>> Categories = new(new List<Category>
	{
		new() { ID = 1, Title = "work", ColorHex = "#3068df" },
		new() { ID = 2, Title = "education", ColorHex = "#8800FF" },
		new() { ID = 3, Title = "self", ColorHex = "#FF3300" },
		new() { ID = 4, Title = "relationships", ColorHex = "#FF9900" },
	});

	// Match the MAUI template seed data tags
	public readonly Signal<List<Tag>> Tags = new(new List<Tag>
	{
		new() { ID = 1, Title = "work", ColorHex = "#3068df" },
		new() { ID = 2, Title = "personal", ColorHex = "#FF4500" },
		new() { ID = 3, Title = "health", ColorHex = "#32CD32" },
		new() { ID = 4, Title = "family", ColorHex = "#1E90FF" },
		new() { ID = 5, Title = "friends", ColorHex = "#FF69B4" },
	});

	public readonly Signal<List<Project>> Projects;
	public readonly Signal<List<ProjectTask>> AllTasks;

	public DataStore()
	{
		// Match the MAUI template SeedData.json exactly
		var tasks = new List<ProjectTask>
		{
			new() { ID = 1, Title = "Survey Employees", IsCompleted = false, ProjectID = 1 },
			new() { ID = 2, Title = "Analyze Survey Results", IsCompleted = false, ProjectID = 1 },
			new() { ID = 3, Title = "Develop Action Plan", IsCompleted = false, ProjectID = 1 },
			new() { ID = 4, Title = "Read a Book", IsCompleted = false, ProjectID = 2 },
			new() { ID = 5, Title = "Attend a Workshop", IsCompleted = false, ProjectID = 2 },
			new() { ID = 6, Title = "Practice a Hobby", IsCompleted = false, ProjectID = 2 },
			new() { ID = 7, Title = "Morning Yoga", IsCompleted = false, ProjectID = 3 },
			new() { ID = 8, Title = "Evening Run", IsCompleted = false, ProjectID = 3 },
			new() { ID = 9, Title = "Healthy Cooking Class", IsCompleted = false, ProjectID = 3 },
			new() { ID = 10, Title = "Plan a Family Reunion", IsCompleted = false, ProjectID = 4 },
			new() { ID = 11, Title = "Organize a Friends' Get-together", IsCompleted = false, ProjectID = 4 },
			new() { ID = 12, Title = "Weekly Phone Calls", IsCompleted = false, ProjectID = 4 },
		};

		AllTasks = new Signal<List<ProjectTask>>(tasks);

		var projects = new List<Project>
		{
			new() { ID = 1, Name = "Balance", Description = "Improve work-life balance.",
				Icon = "\uea28", CategoryID = 1,
				Tags = new() { new() { ID = 1, Title = "work", ColorHex = "#3068df" } } },
			new() { ID = 2, Name = "Personal", Description = "Learn to speak another language.",
				Icon = "\uf8fe", CategoryID = 2,
				Tags = new() { new() { ID = 2, Title = "personal", ColorHex = "#FF4500" } } },
			new() { ID = 3, Name = "Fitness", Description = "Promote health and fitness activities",
				Icon = "\uf837", CategoryID = 3,
				Tags = new() { new() { ID = 3, Title = "health", ColorHex = "#32CD32" } } },
			new() { ID = 4, Name = "Family and Friends", Description = "Strengthen relationships with family and friends.",
				Icon = "\uf5a9", CategoryID = 4,
				Tags = new() { new() { ID = 4, Title = "family", ColorHex = "#1E90FF" }, new() { ID = 5, Title = "friends", ColorHex = "#FF69B4" } } },
		};

		foreach (var project in projects)
			project.Tasks = tasks.Where(t => t.ProjectID == project.ID).ToList();

		Projects = new Signal<List<Project>>(projects);
	}

	public string Today => DateTime.Now.ToString("dddd, MMM d");

	public List<CategoryChartData> GetCategoryChartData()
	{
		var categories = Categories.Value ?? new();
		var projects = Projects.Value ?? new();
		return categories.Select(c =>
		{
			var taskCount = projects.Where(p => p.CategoryID == c.ID).SelectMany(p => p.Tasks).Count();
			return new CategoryChartData(c.Title, taskCount, c.Color);
		}).ToList();
	}

	public void ToggleTaskComplete(int taskId)
	{
		var tasks = new List<ProjectTask>(AllTasks.Value!);
		var task = tasks.FirstOrDefault(t => t.ID == taskId);
		if (task != null)
		{
			task.IsCompleted = !task.IsCompleted;
			AllTasks.Value = tasks;
			RefreshProjects();
		}
	}

	public void AddTask(ProjectTask task)
	{
		task.ID = _nextTaskId++;
		var tasks = new List<ProjectTask>(AllTasks.Value!) { task };
		AllTasks.Value = tasks;
		RefreshProjects();
	}

	public void DeleteTask(int taskId)
	{
		var tasks = new List<ProjectTask>(AllTasks.Value!);
		tasks.RemoveAll(t => t.ID == taskId);
		AllTasks.Value = tasks;
		RefreshProjects();
	}

	public void CleanCompletedTasks()
	{
		var tasks = new List<ProjectTask>(AllTasks.Value!);
		tasks.RemoveAll(t => t.IsCompleted);
		AllTasks.Value = tasks;
		RefreshProjects();
	}

	public void AddProject(Project project)
	{
		project.ID = _nextProjectId++;
		var projects = new List<Project>(Projects.Value!) { project };
		Projects.Value = projects;
	}

	public void SaveProject(Project project)
	{
		var projects = new List<Project>(Projects.Value!);
		var idx = projects.FindIndex(p => p.ID == project.ID);
		if (idx >= 0)
			projects[idx] = project;
		Projects.Value = projects;
	}

	public void DeleteProject(int projectId)
	{
		var projects = new List<Project>(Projects.Value!);
		projects.RemoveAll(p => p.ID == projectId);
		Projects.Value = projects;

		var tasks = new List<ProjectTask>(AllTasks.Value!);
		tasks.RemoveAll(t => t.ProjectID == projectId);
		AllTasks.Value = tasks;
	}

	public void AddCategory(Category category)
	{
		category.ID = _nextCategoryId++;
		Categories.Value = new List<Category>(Categories.Value!) { category };
	}

	public void DeleteCategory(int categoryId)
	{
		var cats = new List<Category>(Categories.Value!);
		cats.RemoveAll(c => c.ID == categoryId);
		Categories.Value = cats;
	}

	public void SaveCategories(List<Category> categories)
	{
		Categories.Value = new List<Category>(categories);
	}

	public void AddTag(Tag tag)
	{
		tag.ID = _nextTagId++;
		Tags.Value = new List<Tag>(Tags.Value!) { tag };
	}

	public void DeleteTag(int tagId)
	{
		var tags = new List<Tag>(Tags.Value!);
		tags.RemoveAll(t => t.ID == tagId);
		Tags.Value = tags;
	}

	public void SaveTags(List<Tag> tags)
	{
		Tags.Value = new List<Tag>(tags);
	}

	public void ResetData()
	{
		// Re-create seed data
		var store = new DataStore();
		Categories.Value = store.Categories.Value;
		Tags.Value = store.Tags.Value;
		AllTasks.Value = store.AllTasks.Value;
		Projects.Value = store.Projects.Value;
	}

	/// <summary>
	/// Re-sync project task lists and trigger reactive UI update.
	/// </summary>
	public void RefreshProjects()
	{
		var projects = new List<Project>(Projects.Value!);
		var tasks = AllTasks.Value ?? new();
		foreach (var project in projects)
			project.Tasks = tasks.Where(t => t.ProjectID == project.ID).ToList();
		Projects.Value = projects;
	}
}
