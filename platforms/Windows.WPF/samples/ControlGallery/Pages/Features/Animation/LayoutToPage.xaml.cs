namespace ControlGallery.Pages.Features.Animation;

public partial class LayoutToPage : ContentPage
{
    private Point _initialPosition;
    private Point _initalTranslate;
    private Size _imageSize;
    private Point _movedPosition;

    private bool _isMoved;

    private Random _random;

    public LayoutToPage()
    {
        InitializeComponent();
        _random = new Random();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        _initialPosition = new Point(this.Image.X, this.Image.Y);
        _initalTranslate = new Point(this.TranslationX, this.TranslationY);
        _imageSize = new Size(this.Image.Width, this.Image.Height);
    }

    private async void OnLayoutToClicked(object sender, EventArgs e)
    {
        if (!_isMoved)
        {
            _movedPosition = AsNegative() ? new Point(-GetRandom(), -GetRandom()) : new Point(GetRandom(), GetRandom());
            var rect = new Rect(_movedPosition, _imageSize);

            //this.Image.ScaleTo(1.5);
            //this.Image.FadeTo(0.2);
            //this.Image.RotateTo(180);
            await this.Image.TranslateToAsync(rect.X, rect.Y);
            ////await this.Image.TranslateTo(_movedPosition.X, _movedPosition.Y);
            //this.Image.ScaleTo(1);
            //this.Image.FadeTo(1);
            //this.Image.RotateTo(0);

            _isMoved = true;
        }
        else
        {
            var rect = new Rect(_initialPosition, _imageSize);

            //this.Image.ScaleTo(0.5);
            //this.Image.FadeTo(0.5);
            //this.Image.RotateTo(90);
            await this.Image.TranslateToAsync(rect.X, rect.Y);
            //await this.Image.TranslateTo(_initalTranslate.X, _initalTranslate.Y);
            //this.Image.ScaleTo(1.0);
            //this.Image.FadeTo(1);
            //this.Image.RotateTo(0);

            _isMoved = false;
        }


    }


    public double GetRandom() =>
        (double)_random.Next(50, 100);

    public bool AsNegative() =>
        _random.Next(0, 1) == 1;
}
