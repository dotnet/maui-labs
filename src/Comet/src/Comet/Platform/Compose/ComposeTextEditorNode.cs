#nullable enable
#if ANDROID
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;

namespace Comet.Platform.Compose
{
	/// <summary>Multiline Material 3 TextField used by Comet TextEditor.</summary>
	sealed class ComposeTextEditorNode : ComposeNode
	{
		readonly MutableState<string> _text = new(string.Empty);
		readonly MutableState<string> _placeholder = new(string.Empty);
		readonly MutableState<bool> _borderless = new(false);
		Microsoft.Maui.Graphics.Color? _textColor;
		readonly MutableState<int> _styleVersion = new(0);

		protected override bool PadsOwnContent => true;

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.TextField_Text)
				_text.Value = value.AsString ?? string.Empty;
			else if (id == PropertyIds.TextField_Placeholder)
				_placeholder.Value = value.AsString ?? string.Empty;
			else if (id == PropertyIds.TextField_Borderless)
				_borderless.Value = value.AsBool;
			else if (id == PropertyIds.TextField_TextColor)
			{
				_textColor = value.AsColor;
				_styleVersion.Value++;
			}
		}

		public override Size Measure(double widthConstraint, double heightConstraint)
		{
			var content = TextMeasure.MeasureWrapped(_text.Value, 16, widthConstraint);
			var width = double.IsInfinity(widthConstraint) ? content.Width + 32 : widthConstraint;
			return new Size(width, System.Math.Max(96, content.Height + 32));
		}

		public override void Render(IComposer composer)
		{
			_ = _styleVersion.Value;
			if (_borderless.Value)
			{
				RenderBorderless(composer);
				return;
			}

			var field = new AndroidX.Compose.TextField(
				_text.Value,
				text => Sink?.OnEvent(EventIds.TextChanged, text))
			{
				SingleLine = false,
				MinLines = 3,
			};
			if (_placeholder.Value.Length > 0)
				field.Placeholder = new AndroidX.Compose.Text(_placeholder.Value);
			if (_textColor is { } color)
				field.TextStyle = new AndroidX.Compose.TextStyle { Color = ToComposeColor(color) };

			var modifier = BuildNodeModifier() ?? Modifier.Companion;
			field.Modifier = modifier.OnFocusChanged(state =>
			{
				if (state.IsFocused)
					Sink?.OnEvent(EventIds.Focused);
				else
					Sink?.OnEvent(EventIds.Unfocused);
			});
			field.Render(composer);
		}

		void RenderBorderless(IComposer composer)
		{
			var textColor = _textColor is { } color
				? ToComposeColor(color)
				: AndroidX.Compose.Color.Black;
			var contentModifier = Modifier.Companion.FillMaxSize().OnFocusChanged(state =>
			{
				if (state.IsFocused)
					Sink?.OnEvent(EventIds.Focused);
				else
					Sink?.OnEvent(EventIds.Unfocused);
			});

			var box = new AndroidX.Compose.Box();
			((ComposableNode)box).Modifier = BuildNodeModifier();

			var field = new AndroidX.Compose.BasicTextField(
				_text.Value,
				text =>
				{
					_text.Value = text;
					Sink?.OnEvent(EventIds.TextChanged, text);
				})
			{
				SingleLine = false,
				Modifier = contentModifier,
				TextStyle = new AndroidX.Compose.TextStyle
				{
					Color = textColor,
					FontSize = new AndroidX.Compose.Sp(16),
				},
			};
			box.Add(field);

			var hint = string.IsNullOrEmpty(_text.Value)
				? _placeholder.Value
				: string.Empty;
			box.Add(new AndroidX.Compose.Text(hint)
			{
				Modifier = Modifier.Companion.FillMaxSize(),
				FontSize = new AndroidX.Compose.Sp(16),
				Color = _textColor is { } placeholderColor
					? ToComposeColor(new Microsoft.Maui.Graphics.Color(
						placeholderColor.Red,
						placeholderColor.Green,
						placeholderColor.Blue,
						0.6f))
					: AndroidX.Compose.Color.Gray,
			});

			((ComposableNode)box).Render(composer);
		}
	}
}
#endif
