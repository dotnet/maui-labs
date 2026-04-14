
namespace Flexibility.Shared;

public partial class AchievementView : Grid
{

	public static readonly BindableProperty IconProperty =
		BindableProperty.Create(nameof(Icon), typeof(string), typeof(AchievementView), "&#xf13d;");

	public string Icon
	{
		get { return (string)GetValue(IconProperty); }
		set { SetValue(IconProperty, value); OnPropertyChanged(nameof(Icon)); }
	}

	public static readonly BindableProperty AchievementProperty =
		BindableProperty.Create(nameof(Achievement), typeof(string), typeof(AchievementView), "First Win");

	public string Achievement
	{
		get { return (string)GetValue(AchievementProperty); }
		set { SetValue(AchievementProperty, value); OnPropertyChanged(nameof(Achievement)); }
	}

	private bool isAchieved;
	public bool IsAchieved
	{
		set
		{
			isAchieved = value;
		}
	}

	public AchievementView()
	{
		InitializeComponent();

		BindingContext = this;
	}


}
