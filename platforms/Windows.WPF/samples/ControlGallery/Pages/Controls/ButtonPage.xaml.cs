using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ControlGallery.Pages;

public partial class ButtonPage : ContentPage
{
    int counter;

    public int Counter
    {
        get
        {
            return counter;
        }
        set
        {
            counter = value;
            OnPropertyChanged();
        }
    }

    public ButtonPage()
    {
        InitializeComponent();

        BindingContext = this;
    }

    void Button_Clicked(System.Object sender, System.EventArgs e)
    {
        Counter++;
        //ShowCount();
    }

    async void ShowCount()
    {
        CounterLbl.CancelAnimations();
        await CounterLbl.FadeToAsync(1, 200);
        await CounterLbl.FadeToAsync(1, 600);// just to buffer before fade out
        await CounterLbl.FadeToAsync(0, 200);
    }

    async void Button_Clicked_1(System.Object sender, System.EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync(nameof(ProfileReportPage));
        }catch(Exception ex)
        {
            Debug.WriteLine($"Crap: {ex.Message}");
        }
    }
}