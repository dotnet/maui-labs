using Microsoft.Maui.Graphics.Converters;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ControlGallery.Pages;

public partial class PickerPage : ContentPage
{
	public PickerPage()
	{
		InitializeComponent();

        BindingContext = new SimpleColorPickerPageViewModel();
	}
}

public class SimpleColorPickerPageViewModel : INotifyPropertyChanged
{
    ColorTypeConverter colorTypeConverter = new ColorTypeConverter();

    public IList<string> ColorNames
    {
        get
        {
            return typeof(Colors).GetRuntimeFields()
                                .Where(f => f.IsPublic && f.IsStatic)
                                .Select(f => f.Name).ToList();
        }
    }

    string selectedColorName;
    public string SelectedColorName
    {
        get { return selectedColorName; }
        set
        {
            if (selectedColorName != value)
            {
                selectedColorName = value;
                OnPropertyChanged();
                OnPropertyChanged("SelectedColor");
            }
        }
    }

    public Color SelectedColor
    {
        get
        {
            if (string.IsNullOrWhiteSpace(selectedColorName))
            {
                return Colors.White;
            }
            return (Color)colorTypeConverter.ConvertFromInvariantString(selectedColorName);
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChangedEventHandler handler = PropertyChanged;
        if (handler != null)
        {
            handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}