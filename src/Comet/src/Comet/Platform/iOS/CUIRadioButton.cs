using System;
using UIKit;

namespace Comet.iOS
{
	public class CUIRadioButton : UIButton
	{
		private const float CONTENT_SPACING = 10;

		private static UIImage SelectedImage = UIImage.GetSystemImage("largecircle.fill.circle")
			.ImageWithRenderingMode(UIImageRenderingMode.AlwaysTemplate);
		private static UIImage DeselectedImage = UIImage.GetSystemImage("circle")
			.ImageWithRenderingMode(UIImageRenderingMode.AlwaysTemplate);

		public event EventHandler IsCheckedChanged;

		private UIColor _foregroundColor = UIColor.SystemBlue;
		private string _title;
		private bool _isChecked;

		public CUIRadioButton(bool isChecked = false)
		{
			_isChecked = isChecked;
			HorizontalAlignment = UIControlContentHorizontalAlignment.Left;
			ApplyConfiguration();
			TouchUpInside += (sender, e) => { if (!IsChecked) IsChecked = true; };
		}

		public bool IsChecked
		{
			get => _isChecked;
			set
			{
				if (_isChecked != value)
				{
					_isChecked = value;
					ApplyConfiguration();
					IsCheckedChanged?.Invoke(this, new EventArgs());
				}
			}
		}

		// UIButtonConfiguration takes over all rendering — legacy SetTitle/SetTitleColor
		// are ignored once Configuration is set. Override them to update through the
		// configuration instead.
		public override void SetTitle(string title, UIControlState forState)
		{
			if (forState == UIControlState.Normal)
			{
				_title = title;
				ApplyConfiguration();
			}
		}

		public override void SetTitleColor(UIColor color, UIControlState forState)
		{
			if (forState == UIControlState.Normal && color is not null)
			{
				_foregroundColor = color;
				ApplyConfiguration();
			}
		}

		void ApplyConfiguration()
		{
			var config = UIButtonConfiguration.PlainButtonConfiguration;
			config.Image = _isChecked ? SelectedImage : DeselectedImage;
			config.ImagePadding = CONTENT_SPACING;
			config.BaseForegroundColor = _foregroundColor;
			config.ContentInsets = new NSDirectionalEdgeInsets(4, 0, 4, 0);
			if (_title is not null)
				config.Title = _title;
			Configuration = config;
		}
	}
}