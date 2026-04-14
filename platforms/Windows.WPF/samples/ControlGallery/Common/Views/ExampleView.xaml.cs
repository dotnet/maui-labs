using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;

namespace ControlGallery.Common.Views;

public partial class ExampleView : ContentView
{
	public ExampleView()
	{
		InitializeComponent();

        BindingContext = this;
	}

    public static readonly BindableProperty HeaderTextProperty = BindableProperty.Create("HeaderText", typeof(string), typeof(ControlExample), null);
    public string HeaderText
    {
        get { return (string)GetValue(HeaderTextProperty); }
        set { SetValue(HeaderTextProperty, value); }
    }

    public static readonly BindableProperty ExampleProperty = BindableProperty.Create("Example", typeof(View), typeof(ControlExample), null);
    public View Example
    {
        get { return (View)GetValue(ExampleProperty); }
        set { SetValue(ExampleProperty, value); }
    }

    public static readonly BindableProperty XamlProperty = BindableProperty.Create("Xaml", typeof(string), typeof(ControlExample), null);
    public string Xaml
    {
        get { return (string)GetValue(XamlProperty); }
        set { SetValue(XamlProperty, value); }
    }

    public static readonly BindableProperty XamlSourceProperty = BindableProperty.Create("XamlSource", typeof(object), typeof(ControlExample), null);
    public Uri XamlSource
    {
        get { return (Uri)GetValue(XamlSourceProperty); }
        set { SetValue(XamlSourceProperty, value); }
    }

    public static readonly BindableProperty CSharpProperty = BindableProperty.Create("CSharp", typeof(string), typeof(ControlExample), null);
    public string CSharp
    {
        get { return (string)GetValue(CSharpProperty); }
        set { SetValue(CSharpProperty, value); }
    }

    public static readonly BindableProperty CSharpSourceProperty = BindableProperty.Create("CSharpSource", typeof(object), typeof(ControlExample), null);
    public Uri CSharpSource
    {
        get { return (Uri)GetValue(CSharpSourceProperty); }
        set { SetValue(CSharpSourceProperty, value); }
    }

    public static readonly BindableProperty StylesProperty = BindableProperty.Create("Styles", typeof(string), typeof(ControlExample), null);
    public string Styles
    {
        get { return (string)GetValue(StylesProperty); }
        set { SetValue(StylesProperty, value); }
    }

    public static readonly BindableProperty LanguageProperty = BindableProperty.Create("Language", typeof(string), typeof(ControlExample), null);
    public string Language
    {
        get { return (string)GetValue(CSharpSourceProperty); }
        set { SetValue(LanguageProperty, value); }
    }

    void Button_Clicked(System.Object sender, System.EventArgs e)
    {
        string code = (rCsharp.IsChecked) ? CSharp : Xaml;
        Clipboard.SetTextAsync(code);
        Toast.Make("Copied", ToastDuration.Short, 18).Show();
    }
}
