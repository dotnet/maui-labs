using System;
using AppKit;
using Foundation;
using ObjCRuntime;

namespace Comet.MacOS
{
	public class CUIRadioNSButton : NSButton
	{
		public event EventHandler IsCheckedChanged;

		private bool _isChecked;

		public CUIRadioNSButton(bool isChecked = false)
		{
			SetButtonType(NSButtonType.Radio);
			BezelStyle = NSBezelStyle.Rounded;
			Title = "";
			_isChecked = isChecked;
			State = _isChecked ? NSCellStateValue.On : NSCellStateValue.Off;
			Target = this;
			Action = new Selector("onClicked:");
		}

		[Export("onClicked:")]
		void OnClicked(NSObject sender)
		{
			var newValue = State == NSCellStateValue.On;
			if (_isChecked != newValue)
			{
				_isChecked = newValue;
				IsCheckedChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		public bool IsChecked
		{
			get => _isChecked;
			set
			{
				if (_isChecked != value)
				{
					_isChecked = value;
					State = _isChecked ? NSCellStateValue.On : NSCellStateValue.Off;
					IsCheckedChanged?.Invoke(this, EventArgs.Empty);
				}
			}
		}

		public void SetLabel(string label)
		{
			Title = label ?? "";
			InvalidateIntrinsicContentSize();
		}

		public void SetTextColor(NSColor color)
		{
			if (color is null || string.IsNullOrEmpty(Title))
				return;

			var attributes = new NSDictionary(
				NSStringAttributeKey.ForegroundColor, color);
			AttributedTitle = new NSAttributedString(Title, attributes);
		}
	}
}
