using Microsoft.Maui.Storage;

namespace CometBaristaNotes.Pages;

public class ProfileFormPageState
{
	public string Name { get; set; } = "";
	public string AvatarPath { get; set; } = "";
	public string Error { get; set; } = "";
	public bool IsLoaded { get; set; }
}

public class ProfileFormPage : Component<ProfileFormPageState>
{
	const double AvatarSize = 120;

	readonly int _profileId;
	readonly IDataStore _store;

	public ProfileFormPage(int profileId = 0)
	{
		_profileId = profileId;
		_store = IPlatformApplication.Current?.Services.GetService<IDataStore>()
			?? InMemoryDataStore.Instance;
	}

	void LoadProfile()
	{
		if (_profileId <= 0)
		{
			SetState(s => s.IsLoaded = true);
			return;
		}

		var store = _store;

		var profile = store.GetProfile(_profileId);
		SetState(s =>
		{
			if (profile != null)
			{
				s.Name = profile.Name;
				s.AvatarPath = profile.AvatarPath ?? "";
			}
			s.IsLoaded = true;
		});
	}

	void Save()
	{
		if (string.IsNullOrWhiteSpace(State.Name))
		{
			SetState(s => s.Error = "Please enter a profile name");
			return;
		}
		SetState(s => s.Error = "");

		var store = _store;

		if (_profileId > 0)
		{
			store.UpdateProfile(new UserProfile
			{
				Id = _profileId,
				Name = State.Name,
				AvatarPath = string.IsNullOrEmpty(State.AvatarPath) ? null : State.AvatarPath,
			});
		}
		else
		{
			store.CreateProfile(new UserProfile
			{
				Name = State.Name,
				AvatarPath = string.IsNullOrEmpty(State.AvatarPath) ? null : State.AvatarPath,
			});
		}

		Navigation?.Pop();
	}

	async void Delete()
	{
		if (_profileId <= 0) return;

		var confirmed = await Services.PageHelper.DisplayAlertAsync(
			"Delete Profile?",
			$"Are you sure you want to delete '{State.Name}'? This action cannot be undone.",
			"Delete",
			"Cancel");
		if (!confirmed) return;

		_store.DeleteProfile(_profileId);
		Navigation?.Pop();
	}

	async void PickPhoto()
	{
		try
		{
			var results = await Microsoft.Maui.Media.MediaPicker.Default.PickPhotosAsync();
			var result = results?.FirstOrDefault();
			if (result == null) return;

			var profilesDir = System.IO.Path.Combine(FileSystem.AppDataDirectory, "profiles");
			System.IO.Directory.CreateDirectory(profilesDir);

			var destPath = System.IO.Path.Combine(profilesDir, $"{(_profileId > 0 ? _profileId : 0)}.jpg");
			using var sourceStream = await result.OpenReadAsync();
			using var destStream = System.IO.File.Create(destPath);
			await sourceStream.CopyToAsync(destStream);

			SetState(s => s.AvatarPath = destPath);
		}
		catch
		{
			// Photo pick cancelled or failed
		}
	}

	public override View Render()
	{
		if (!State.IsLoaded)
			LoadProfile();

		var isEdit = _profileId > 0;

		var avatarSection = isEdit
			? VStack(CoffeeColors.SpacingS,
				new ProfileImagePicker(State.AvatarPath, AvatarSize, _ => PickPhoto())
			  )
			  .Alignment(Alignment.Center)
			  .Padding(new Thickness(0, CoffeeColors.SpacingS))
			: VStack(CoffeeColors.SpacingS,
				new CircularAvatar(null, AvatarSize),
				Text("Save the profile first to add a photo")
					.Modifier(CoffeeModifiers.SecondaryText)
					.HorizontalTextAlignment(TextAlignment.Center)
			  )
			  .Alignment(Alignment.Center)
			  .Padding(new Thickness(0, CoffeeColors.SpacingS));

		var stack = VStack(CoffeeColors.SpacingS,
			FormHelpers.MakeSectionHeader(isEdit ? "EDIT PROFILE" : "NEW PROFILE"),
			avatarSection,
			FormHelpers.MakeFormEntry("Name *", State.Name, "Profile name", v => SetState(s => s.Name = v))
		);

		if (!string.IsNullOrEmpty(State.Error))
		{
			stack.Add(
				Border(
					Text(State.Error)
						.Modifier(CoffeeModifiers.BodyError)
						.Padding(new Thickness(12))
				)
				.Modifier(CoffeeModifiers.ErrorCard)
			);
		}

		stack.Add(FormHelpers.MakePrimaryButton(isEdit ? "Save Changes" : "Create Profile", Save));

		if (isEdit)
			stack.Add(FormHelpers.MakeDangerButton("Delete Profile", Delete));

		return ScrollView(
			stack.Padding(new Thickness(CoffeeColors.SpacingM))
		)
		.Modifier(CoffeeModifiers.PageContainer)
		.Title(isEdit ? "Edit Profile" : "New Profile");
	}
}
