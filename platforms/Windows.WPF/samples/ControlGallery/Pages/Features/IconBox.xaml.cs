namespace ControlGallery.Pages;

public partial class IconBox : Border
{
	public IconBox()
	{
		InitializeComponent();
		BindingContext = this;
	}

	public static readonly BindableProperty IconProperty =
            BindableProperty.Create(nameof(Icon), typeof(string), typeof(IconBox), default(string));

        public string Icon
        {
            get { return (string)GetValue(IconProperty); }
            set { SetValue(IconProperty, value); }
        }
}

