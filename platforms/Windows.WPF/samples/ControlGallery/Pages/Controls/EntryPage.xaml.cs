using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;

namespace ControlGallery.Pages
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class EntryPage : ContentPage
    {
        public EntryPage()
        {
            InitializeComponent();
        }

        void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            Debug.WriteLine($"text: {e.NewTextValue}");
        }

        private async void Entry_Completed(object sender, EventArgs e)
        {
            await this.DisplayAlertAsync("Completed", "Captured completed event", "Okay");
        }
    }
}