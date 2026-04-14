using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using ControlGallery.Models;
using CollectionViewDemos.ViewModels;


namespace CollectionViewDemos.Views
{
    public partial class CollectionViewPage : ContentPage
    {
        public CollectionViewPage()
        {
            InitializeComponent();

            BindingContext = new CollectionViewPageViewModel();
        }
    }
}
