namespace ControlGallery.Common.Views;


/// <summary>
/// Describes a textual substitution in sample content.
/// If enabled (default), then $(Key) is replaced with the stringified value.
/// If disabled, then $(Key) is replaced with the empty string.
/// </summary>
public sealed class ControlExampleSubstitution : BindableObject
{
    public event EventHandler<ControlExampleSubstitution> ValueChanged;

    public string Key { get; set; }

    private object _value = null;
    public object Value
    {
        get { return _value; }
        set
        {
            _value = value;
            ValueChanged?.Invoke(this, null);
        }
    }

    private bool _enabled = true;
    public bool IsEnabled
    {
        get { return _enabled; }
        set
        {
            _enabled = value;
            ValueChanged?.Invoke(this, null);
        }
    }

    public string ValueAsString()
    {
        if (!IsEnabled)
        {
            return string.Empty;
        }

        object value = Value;

        // For solid color brushes, use the underlying color.
        if (value is SolidColorBrush)
        {
            value = ((SolidColorBrush)value).Color;
        }

        if (value == null)
        {
            return string.Empty;
        }

        return value.ToString();
    }

    void ScreenshotButton_Clicked(System.Object sender, System.EventArgs e)
    {
        // TODO implement screenshot if desired
    }

    void ControlPaddingBox_Unfocused(System.Object sender, Microsoft.Maui.Controls.FocusEventArgs e)
    {
        
    }
}

[ContentProperty("Example")]
public sealed partial class ControlExample : ContentView
{
    public static readonly BindableProperty HeaderTextProperty = BindableProperty.Create("HeaderText", typeof(string), typeof(ControlExample), null);
    public string HeaderText
    {
        get { return (string)GetValue(HeaderTextProperty); }
        set { SetValue(HeaderTextProperty, value); }
    }

    public static readonly BindableProperty ExampleProperty = BindableProperty.Create("Example", typeof(View), typeof(ControlExample), null);
    public View Example
    {
        get { return (View)GetValue(ExampleProperty); }
        set { SetValue(ExampleProperty, value); }
    }

    public static readonly BindableProperty OutputProperty = BindableProperty.Create("Output", typeof(View), typeof(ControlExample), null);
    public View Output
    {
        get { return (View)GetValue(OutputProperty); }
        set { SetValue(OutputProperty, value); }
    }

    public static readonly BindableProperty OptionsProperty = BindableProperty.Create("Options", typeof(View), typeof(ControlExample), null);
    public View Options
    {
        get { return (View)GetValue(OptionsProperty); }
        set { SetValue(OptionsProperty, value); }
    }

    public static readonly BindableProperty XamlProperty = BindableProperty.Create("Xaml", typeof(string), typeof(ControlExample), null);
    public string Xaml
    {
        get { return (string)GetValue(XamlProperty); }
        set { SetValue(XamlProperty, value); }
    }

    public static readonly BindableProperty XamlSourceProperty = BindableProperty.Create("XamlSource", typeof(object), typeof(ControlExample), null);
    public Uri XamlSource
    {
        get { return (Uri)GetValue(XamlSourceProperty); }
        set { SetValue(XamlSourceProperty, value); }
    }

    public static readonly BindableProperty CSharpProperty = BindableProperty.Create("CSharp", typeof(string), typeof(ControlExample), null);
    public string CSharp
    {
        get { return (string)GetValue(CSharpProperty); }
        set { SetValue(CSharpProperty, value); }
    }

    public static readonly BindableProperty CSharpSourceProperty = BindableProperty.Create("CSharpSource", typeof(object), typeof(ControlExample), null);
    public Uri CSharpSource
    {
        get { return (Uri)GetValue(CSharpSourceProperty); }
        set { SetValue(CSharpSourceProperty, value); }
    }

    public static readonly BindableProperty SubstitutionsProperty = BindableProperty.Create("Substitutions", typeof(IList<ControlExampleSubstitution>), typeof(ControlExample), null);
    public IList<ControlExampleSubstitution> Substitutions
    {
        get { return (IList<ControlExampleSubstitution>)GetValue(SubstitutionsProperty); }
        set { SetValue(SubstitutionsProperty, value); }
    }

    private static readonly GridLength defaultExampleHeight =
        new GridLength(1, GridUnitType.Star);

    public static readonly BindableProperty ExampleHeightProperty = BindableProperty.Create("ExampleHeight", typeof(GridLength), typeof(ControlExample), defaultExampleHeight);
    public GridLength ExampleHeight
    {
        get { return (GridLength)GetValue(ExampleHeightProperty); }
        set { SetValue(ExampleHeightProperty, value); }
    }

    public static readonly BindableProperty WebViewHeightProperty = BindableProperty.Create("WebViewHeight", typeof(int), typeof(ControlExample), 400);
    public int WebViewHeight
    {
        get { return (int)GetValue(WebViewHeightProperty); }
        set { SetValue(WebViewHeightProperty, value); }
    }

    public static readonly BindableProperty WebViewWidthProperty = BindableProperty.Create("WebViewWidth", typeof(int), typeof(ControlExample), 800);
    public int WebViewWidth
    {
        get { return (int)GetValue(WebViewWidthProperty); }
        set { SetValue(WebViewWidthProperty, value); }
    }

    //public new static readonly BindableProperty HorizontalContentAlignmentProperty = BindableProperty.Create("HorizontalContentAlignment", typeof(LayoutOptions), typeof(ControlExample), LayoutOptions.Start);
    //public new HorizontalAlignment HorizontalContentAlignment
    //{
    //    get { return (HorizontalAlignment)GetValue(HorizontalContentAlignmentProperty); }
    //    set { SetValue(HorizontalContentAlignmentProperty, value); }
    //}

    public ControlExample()
    {
        this.InitializeComponent();
        //Substitutions = new List<ControlExampleSubstitution>();

        //ControlPresenter.CreatePropertyChangedCallback(ContentPresenter.PaddingProperty, ControlPaddingChangedCallback);
        //this.Loaded += ControlExample_Loaded;
    }

    //private void ControlExample_Loaded(object sender, RoutedEventArgs e)
    //{
    //    if (!XamlPresenter.IsEmpty && !CSharpPresenter.IsEmpty)
    //    {
    //        VisualStateManager.GoToState(this, "SeparatorVisible", false);
    //    }
    //}

    private void rootGrid_Loaded(object sender, EventArgs e)
    {
        //if (MinimumUniversalAPIContract != 0 && !(ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", (ushort)MinimumUniversalAPIContract)))
        //{
        //    ErrorTextBlock.IsVisible = true;
        //}
    }

    private enum SyntaxHighlightLanguage { Xml, CSharp };

    //private void ScreenshotButton_Click(object sender, EventArgs e)
    //{
    //    TakeScreenshot();
    //}

    //private void ScreenshotDelayButton_Click(object sender, EventArgs e)
    //{
    //    TakeScreenshotWithDelay();
    //}

//    private async void TakeScreenshot()
//    {
//        // Using RTB doesn't capture popups; but in the non-delay case, that probably isn't necessary.
//        // This method seems more robust than using AppRecordingManager and also will work on non-desktop devices.

//        RenderTargetBitmap rtb = new RenderTargetBitmap();
//        await rtb.RenderAsync(ControlPresenter);

//        var pixelBuffer = await rtb.GetPixelsAsync();
//        var pixels = pixelBuffer.ToArray();

//        var file = await UIHelper.ScreenshotStorageFolder.CreateFileAsync(GetBestScreenshotName(), CreationCollisionOption.ReplaceExisting);
//        using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
//        {
//            var displayInformation = DisplayInformation.GetForCurrentView();
//            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
//            encoder.SetPixelData(BitmapPixelFormat.Bgra8,
//                BitmapAlphaMode.Premultiplied,
//                (uint)rtb.PixelWidth,
//                (uint)rtb.PixelHeight,
//                displayInformation.RawDpiX,
//                displayInformation.RawDpiY,
//                pixels);

//            await encoder.FlushAsync();
//        }
//    }

//    public async void TakeScreenshotWithDelay()
//    {
//        // 3 second countdown
//        for (int i = 3; i > 0; i--)
//        {
//            ScreenshotStatusTextBlock.Text = i.ToString();
//            await Task.Delay(1000);
//        }
//        ScreenshotStatusTextBlock.Text = "Image captured";

//        // AppRecordingManager is desktop-only, and its use here is quite hacky,
//        // but it is able to capture popups (though not theme shadows).
//        bool isAppRecordingPresent = ApiInformation.IsTypePresent("Windows.Media.AppRecording.AppRecordingManager");
//        if (!isAppRecordingPresent)
//        {
//            // Better than doing nothing
//            TakeScreenshot();
//        }
//        else
//        {
//            var manager = AppRecordingManager.GetDefault();
//            if (manager.GetStatus().CanRecord)
//            {
//#if UNPACKAGED
//                StorageFolder localFolder = await StorageFolder.GetFolderFromPathAsync(System.AppContext.BaseDirectory);
//#else
//                StorageFolder localFolder = ApplicationData.Current.LocalFolder;
//#endif
//                var result = await manager.SaveScreenshotToFilesAsync(
//                    localFolder,
//                    "appScreenshot",
//                    AppRecordingSaveScreenshotOption.HdrContentVisible,
//                    manager.SupportedScreenshotMediaEncodingSubtypes);

//                if (result.Succeeded)
//                {
//                    // Open the screenshot back up
//                    var screenshotFile = await localFolder.GetFileAsync("appScreenshot.png");
//                    using (var stream = await screenshotFile.OpenAsync(FileAccessMode.Read))
//                    {
//                        var decoder = await BitmapDecoder.CreateAsync(stream);

//                        // Find the control in the picture
//                        GeneralTransform t = ControlPresenter.TransformToVisual(Window.Current.Content);
//                        Point pos = t.TransformPoint(new Point(0, 0));

//                        if (!CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar)
//                        {
//                            // Add the height of the title bar, which I really wish was programmatically available anywhere.
//                            pos.Y += 32.0;
//                        }

//                        // Crop the screenshot to the control area
//                        var transform = new BitmapTransform()
//                        {
//                            Bounds = new BitmapBounds()
//                            {
//                                X = (uint)(Math.Ceiling(pos.X)) + 1, // Avoid the 1px window border
//                                Y = (uint)(Math.Ceiling(pos.Y)) + 1,
//                                Width = (uint)ControlPresenter.ActualWidth - 1, // Rounding issues -- this avoids capturing the control border
//                                Height = (uint)ControlPresenter.ActualHeight - 1
//                            }
//                        };

//                        var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
//                            decoder.BitmapPixelFormat,
//                            BitmapAlphaMode.Ignore,
//                            transform,
//                            ExifOrientationMode.IgnoreExifOrientation,
//                            ColorManagementMode.DoNotColorManage);

//                        // Save the cropped picture
//                        var file = await localFolder.CreateFileAsync(GetBestScreenshotName(), CreationCollisionOption.ReplaceExisting);
//                        using (var outStream = await file.OpenAsync(FileAccessMode.ReadWrite))
//                        {
//                            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outStream);
//                            encoder.SetSoftwareBitmap(softwareBitmap);
//                            await encoder.FlushAsync();
//                        }
//                    }

//                    // Delete intermediate file
//                    await screenshotFile.DeleteAsync();
//                }
//            }
//        }

//        await Task.Delay(1000);
//        ScreenshotStatusTextBlock.Text = "";
//    }

//    string GetBestScreenshotName()
//    {
//        string imageName = "Screenshot.png";
//        if (XamlSource != null)
//        {
//            // Most of them don't have this, but the xaml source name is a really good file name
//            string xamlSource = new Uri("ms-appx:///" + Path.Combine("ControlPagesSampleCode", XamlSource.LocalPath)).LocalPath;
//            string fileName = Path.GetFileNameWithoutExtension(xamlSource);
//            if (!String.IsNullOrWhiteSpace(fileName))
//            {
//                imageName = fileName + ".png";
//            }
//        }
//        else if (!String.IsNullOrWhiteSpace(Name))
//        {
//            // Put together the page name and the control example name
//            UIElement uie = this;
//            while (uie != null && !(uie is Page))
//            {
//                uie = VisualTreeHelper.GetParent(uie) as UIElement;
//            }
//            if (uie != null)
//            {
//                string name = Name;
//                if (name.Equals("RootPanel"))
//                {
//                    // This is the default name for the example; add an index on the end to disambiguate
//                    imageName = uie.GetType().Name + "_" + ((Panel)this.Parent).Children.IndexOf(this).ToString() + ".png";
//                }
//                else
//                {
//                    imageName = uie.GetType().Name + "_" + name + ".png";
//                }
//            }
//        }
//        return imageName;
//    }

    private void ControlPaddingChangedCallback(BindableObject sender, BindableProperty dp)
    {
        //ControlPaddingBox.Text = ControlPresenter.Padding.ToString();
    }

    private void ControlPaddingBox_KeyUp(object sender, EventArgs e)
    {
        //if (e.Key == Windows.System.VirtualKey.Enter && !String.IsNullOrWhiteSpace(ControlPaddingBox.Text))
        //{
        //    EvaluatePadding();
        //}
    }

    //private void ControlPaddingBox_LostFocus(object sender, EventArgs e)
    //{
    //    EvaluatePadding();
    //}

    //private void EvaluatePadding()
    //{
    //    // Evaluate the text in the ControlPaddingBox as padding
    //    string[] strs = ControlPaddingBox.Text.Split(new char[] { ' ', ',' });
    //    double[] nums = new double[4];
    //    for (int i = 0; i < strs.Length; i++)
    //    {
    //        if (!Double.TryParse(strs[i], out nums[i]))
    //        {
    //            //  Bad format
    //            return;
    //        }
    //    }

    //    switch (nums.Length)
    //    {
    //        case 1:
    //            ControlPresenter.Padding = new Thickness() { Left = nums[0], Top = nums[0], Right = nums[0], Bottom = nums[0] };
    //            break;

    //        case 2:
    //            ControlPresenter.Padding = new Thickness() { Left = nums[0], Top = nums[1], Right = nums[0], Bottom = nums[1] };
    //            break;

    //        case 4:
    //            ControlPresenter.Padding = new Thickness() { Left = nums[0], Top = nums[1], Right = nums[2], Bottom = nums[3] };
    //            break;
    //    }
    //}

    
}
