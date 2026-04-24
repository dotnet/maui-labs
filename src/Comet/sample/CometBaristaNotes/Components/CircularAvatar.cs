namespace CometBaristaNotes.Components;

/// <summary>
/// Displays a rounded image or a placeholder person icon in a colored circle.
/// </summary>
public class CircularAvatar : View
{
	private readonly string? _imagePath;
	private readonly double _size;

	public CircularAvatar(string? imagePath = null, double size = 80)
	{
		_imagePath = imagePath;
		_size = size;
	}

	[Body]
	View body()
	{
		var hasImage = !string.IsNullOrEmpty(_imagePath) && File.Exists(_imagePath);

		if (hasImage)
		{
			return Image(_imagePath!)
				.Aspect(Aspect.AspectFill)
				.Modifier(CoffeeModifiers.AvatarCircle((float)_size));
		}

		// Placeholder: person icon inside a colored circle
		return ZStack(
			new Comet.BoxView(CoffeeColors.Primary)
				.Modifier(CoffeeModifiers.AvatarCircle((float)_size)),
			Text(Icons.Person)
				.Modifier(CoffeeModifiers.Icon(_size * 0.5, Colors.White))
		).Modifier(CoffeeModifiers.FrameSize((float)_size, (float)_size));
	}
}
