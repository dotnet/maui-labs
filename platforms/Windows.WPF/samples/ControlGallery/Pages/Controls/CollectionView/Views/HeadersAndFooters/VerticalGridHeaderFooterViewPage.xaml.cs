using CollectionViewDemos.ViewModels;


namespace CollectionViewDemos.Views
{
    public partial class VerticalGridHeaderFooterViewPage : ContentPage
    {
        public VerticalGridHeaderFooterViewPage()
        {
            InitializeComponent();
            BindingContext = new MonkeysViewModel();
        }
    }
}
