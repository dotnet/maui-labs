namespace CometBaristaNotes.Components;

/// <summary>
/// Combines a CircularAvatar with a camera overlay badge.
/// On tap, invokes the onImagePicked callback — the caller handles the actual MediaPicker logic.
/// </summary>
public class ProfileImagePicker : View
{
	private readonly string? _imagePath;
	private readonly double _size;
	private readonly Action<string>? _onImagePicked;

	public ProfileImagePicker(string? imagePath = null, double size = 120, Action<string>? onImagePicked = null)
	{
		_imagePath = imagePath;
		_size = size;
		_onImagePicked = onImagePicked;
	}

	[Body]
	View body()
	{
		var badgeSize = _size * 0.3;

		return ZStack(
			new CircularAvatar(_imagePath, _size),
			// Camera badge at bottom-right
			ZStack(
				new Comet.BoxView(CoffeeColors.Primary)
					.Modifier(CoffeeModifiers.AvatarCircle((float)badgeSize)),
				Text(Icons.PhotoCamera)
					.Modifier(CoffeeModifiers.Icon(badgeSize * 0.5, Colors.White))
			)
			.Modifier(CoffeeModifiers.FrameSize((float)badgeSize, (float)badgeSize))
			.Alignment(Alignment.BottomTrailing)
		)
		.Modifier(CoffeeModifiers.FrameSize((float)_size, (float)_size))
		.OnTap(_ => _onImagePicked?.Invoke(_imagePath ?? string.Empty));
	}
}
