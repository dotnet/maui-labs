using System;
using System.Collections.Generic;

using System.IO;
using CollectionViewDemos.ViewModels;

namespace Flexibility.Shared
{
    public partial class PhotosPage : ContentPage
    {
        MonkeysViewModel vm;

        public PhotosPage()
        {
            InitializeComponent();
            BindingContext = vm = new MonkeysViewModel();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
        }

    }
}
