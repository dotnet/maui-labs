using System;
using System.Text.RegularExpressions;

namespace Flexibility.Shared
{
	public partial class LoginPage : ContentPage
	{
		public LoginPage()
		{
			InitializeComponent();

		}

		protected override void OnAppearing()
		{
			base.OnAppearing();

			var stateGroup = new VisualStateGroup
			{
				Name = "StrengthStates",
				TargetType = typeof(Label)
			};

			stateGroup.States.Add(CreateState("Blank", "", Colors.White));
			stateGroup.States.Add(CreateState("VeryWeak", "\uf023", Colors.Red));
			stateGroup.States.Add(CreateState("Weak", "\uf023 \uf023", Colors.Orange));
			stateGroup.States.Add(CreateState("Medium", "\uf023 \uf023 \uf023", Colors.Yellow));
			stateGroup.States.Add(CreateState("String", "\uf023 \uf023 \uf023 \uf023", Colors.Green));
			stateGroup.States.Add(CreateState("VeryStrong", "\uf023 \uf023 \uf023 \uf023 \uf023", Colors.Green));

			VisualStateManager.SetVisualStateGroups(this.StrengthIndicator, new VisualStateGroupList { stateGroup });
		}

		static VisualState CreateState(string strength, string text, Color color)
		{
			var textSetter = new Setter { Value = text, Property = Label.TextProperty };
			var colorSetter = new Setter { Value = color, Property = Label.TextColorProperty };

			return new VisualState
			{
				Name = strength,
				TargetType = typeof(Label),
				Setters = { textSetter, colorSetter }
			};
		}

		async void Handle_Clicked(object sender, System.EventArgs e)
		{
			var isValid = true;

			if (string.IsNullOrEmpty(UserNameEntry.Text) || UserNameEntry.Text.Length < 5)
			{
				VisualStateManager.GoToState(UserNameEntry, "Invalid");
				isValid = false;
			}

			if (string.IsNullOrEmpty(PasswordEntry.Text) || PasswordEntry.Text.Length < 5)
			{
				VisualStateManager.GoToState(PasswordEntry, "Invalid");
				isValid = false;
			}

			if (isValid)
				await DisplayAlertAsync("Welcome to Visual State Manager", "", "Thanks!");
		}

		void Handle_TextChanged(object sender, TextChangedEventArgs e)
		{
			var strength = PasswordAdvisor.CheckStrength(e.NewTextValue);
			var strengthName = Enum.GetName(typeof(PasswordScore), strength);
			VisualStateManager.GoToState(this.StrengthIndicator, strengthName);
		}

		private string strength;

		public string Strength
		{
			get => strength;
			set
			{
				strength = value;
				OnPropertyChanged(nameof(Strength));
			}
		}
	}

	public enum PasswordScore
	{
		Blank = 0,
		VeryWeak = 1,
		Weak = 2,
		Medium = 3,
		Strong = 4,
		VeryStrong = 5
	}

	public class PasswordAdvisor
	{
		public static PasswordScore CheckStrength(string password)
		{
			int score = 0;

			if (password.Length > 4)
				score++;

			if (password.Length >= 8)
				score++;
			if (password.Length >= 12)
				score++;
			if (Regex.Match(password, @"^(?=.*\d).+$", RegexOptions.ECMAScript).Success)
				score++;
			if (Regex.Match(password, @"^(?=.*[a-z]).+$", RegexOptions.ECMAScript).Success &&
						Regex.Match(password, @"^(?=.*[A-Z]).+$", RegexOptions.ECMAScript).Success)
				score++;
			if (Regex.Match(password, @"^(?=.*[!,@,#,$,%,^,&,*,?,_,~,-,£,(,)]).+$", RegexOptions.ECMAScript).Success)
				score++;

			return (PasswordScore)score;
		}
	}
}
