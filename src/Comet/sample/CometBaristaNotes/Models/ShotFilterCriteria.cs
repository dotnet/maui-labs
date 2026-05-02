namespace CometBaristaNotes.Models;

public class ShotFilterCriteria
{
	public List<int> BeanIds { get; set; } = new();
	public List<int> MadeForIds { get; set; } = new();
	public List<int> Ratings { get; set; } = new();

	public bool HasFilters =>
		BeanIds.Count > 0 || MadeForIds.Count > 0 || Ratings.Count > 0;

	public int FilterCount =>
		BeanIds.Count + MadeForIds.Count + Ratings.Count;

	public ShotFilterCriteria Clone() => new()
	{
		BeanIds = new List<int>(BeanIds),
		MadeForIds = new List<int>(MadeForIds),
		Ratings = new List<int>(Ratings),
	};

	public void Clear()
	{
		BeanIds.Clear();
		MadeForIds.Clear();
		Ratings.Clear();
	}
}
