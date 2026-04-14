using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;

namespace ControlGallery.Pages
{
    public partial class AppThemePage : ContentPage
    {
        public AppThemePage()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
			CurrentThemeLabel.Text = Application.Current.UserAppTheme.ToString();
		}

        void RadioButton_CheckedChanged(System.Object sender, CheckedChangedEventArgs e)
        {
            AppTheme val = (AppTheme)((RadioButton)sender).Value;
            if (App.Current.UserAppTheme == val)
                return;

            App.Current.UserAppTheme = val;
            CurrentThemeLabel.Text = Application.Current.UserAppTheme.ToString();
        }



    }
}