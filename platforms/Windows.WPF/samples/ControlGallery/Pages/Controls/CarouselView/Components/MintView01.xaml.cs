namespace CarouselGallery.Views.Components
{
    public partial class MintView01
    {
        public MintView01()
        {
            InitializeComponent();
        }

        private async void Button_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.CurrentPage.DisplayAlertAsync("You Did It!",
                "Thanks for tapping. There is no activity to see at this time. Have a nice day!",
                "Bye!");
        }
    }
}