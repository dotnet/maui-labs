namespace CometTrackizerApp.Components;

public class RoundedEntryView : View
{
	readonly string _labelText;
	readonly string _text;
	readonly bool _isPassword;
	readonly Action<string>? _onTextChanged;

	public RoundedEntryView(string labelText, string text, bool isPassword, Action<string>? onTextChanged = null)
	{
		_labelText = labelText;
		_text = text;
		_isPassword = isPassword;
		_onTextChanged = onTextChanged;
	}

	[Body]
	View body() => VStack(4,
		TrackizerTheme.BodySmall(_labelText)
			.Color(TrackizerTheme.Grey50),

		Border(
			_isPassword
				? SecureField(_text, _labelText)
					.Color(TrackizerTheme.White)
					.OnTextChanged(_onTextChanged)
					.Frame(height: 48)
					.Margin(new Thickness(5, 0))
				: TextField(_text, _labelText)
					.Color(TrackizerTheme.White)
					.OnTextChanged(_onTextChanged)
					.Frame(height: 48)
					.Margin(new Thickness(5, 0))
		)
		.RoundedBorder(radius: 16, color: TrackizerTheme.Grey70, strokeSize: 1)
	);
}
