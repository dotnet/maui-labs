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
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class RadioButtonPage : ContentPage
    {
        public List<Quality> QualityOptions {get;set; } = new List<Quality>
        {
            new Quality{Description="Low"},
            new Quality{Description="Medium"},
            new Quality{Description="High"}
        };

        public RadioButtonPage()
        {
            
            InitializeComponent();
            BindingContext = this;

        }
    }

    public class Quality
    {
        public string Description { get;set;}
    }
}