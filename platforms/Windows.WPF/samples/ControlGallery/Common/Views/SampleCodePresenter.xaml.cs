using System.Text.RegularExpressions;

namespace ControlGallery.Common.Views;

// this could be a good option for presenting code with color
// https://wbaldoumas.github.io/markdown-colorcode/index.html

public partial class SampleCodePresenter : ContentView
{
    public SampleCodePresenter()
    {
        InitializeComponent();
    }

    public static readonly BindableProperty CodeProperty = BindableProperty.Create("Code", typeof(string), typeof(SampleCodePresenter), default(string), propertyChanged: OnBindablePropertyChanged);
    public string Code
    {
        get { return (string)GetValue(CodeProperty); }
        set { SetValue(CodeProperty, value); }
    }

    public static readonly BindableProperty CodeSourceFileProperty = BindableProperty.Create("CodeSourceFile", typeof(object), typeof(SampleCodePresenter), propertyChanged: OnBindablePropertyChanged);
    public Uri CodeSourceFile
    {
        get { return (Uri)GetValue(CodeSourceFileProperty); }
        set { SetValue(CodeSourceFileProperty, value); }
    }

    public static readonly BindableProperty IsCSharpSampleProperty = BindableProperty.Create("IsCSharpSample", typeof(bool), typeof(SampleCodePresenter), false);
    public bool IsCSharpSample
    {
        get { return (bool)GetValue(IsCSharpSampleProperty); }
        set { SetValue(IsCSharpSampleProperty, value); }
    }

    public static readonly BindableProperty SubstitutionsProperty = BindableProperty.Create("Substitutions", typeof(IList<ControlExampleSubstitution>), typeof(ControlExample), null);
    public IList<ControlExampleSubstitution> Substitutions
    {
        get { return (IList<ControlExampleSubstitution>)GetValue(SubstitutionsProperty); }
        set { SetValue(SubstitutionsProperty, value); }
    }

    public bool IsEmpty => Code.Length == 0 && CodeSourceFile == null;

    private static Regex SubstitutionPattern = new Regex(@"\$\(([^)]+)\)");

    private static void OnBindablePropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is SampleCodePresenter presenter)
        {
            presenter.ReevaluateVisibility();
        }
    }

    private void ReevaluateVisibility()
    {
        if (Code.Length == 0 && CodeSourceFile == null)
        {
            IsVisible = false;
        }
        else
        {
            IsVisible = true;
        }
    }

    private void SampleCodePresenter_Loaded(object sender, EventArgs e)
    {
        ReevaluateVisibility();
        //VisualStateManager.GoToState(this, IsCSharpSample ? "CSharpSample" : "XAMLSample");
        //foreach (var substitution in Substitutions)
        //{
        //    substitution.ValueChanged += OnValueChanged;
        //}
    }

    private void OnValueChanged(object sender, ControlExampleSubstitution e)
    {
        GenerateSyntaxHighlightedContent();
    }

    private void CodePresenter_Loaded(object sender, EventArgs e)
    {
        GenerateSyntaxHighlightedContent();
    }

    private void SampleCodePresenter_ActualThemeChanged(object sender, object args)
    {
        // If the theme has changed after the user has already opened the app (ie. via settings), then the new locally set theme will overwrite the colors that are set during Loaded.
        // Therefore we need to re-format the REB to use the correct colors.
        GenerateSyntaxHighlightedContent();
    }

    private Uri GetDerivedSource(Uri rawSource)
    {
        // Get the full path of the source string
        string concatString = "";
        for (int i = 2; i < rawSource.Segments.Length; i++)
        {
            concatString += rawSource.Segments[i];
        }
        Uri derivedSource = new Uri(new Uri("ms-appx:///ControlPagesSampleCode/"), concatString);

        return derivedSource;
    }

    private void GenerateSyntaxHighlightedContent()
    {
        //if (!string.IsNullOrEmpty(Code))
        //{
        //    FormatAndRenderSampleFromString(Code, CodePresenter, IsCSharpSample ? Languages.CSharp : Languages.Xml);
        //}
        //else
        //{
        //    FormatAndRenderSampleFromFile(CodeSourceFile, CodePresenter, IsCSharpSample ? Languages.CSharp : Languages.Xml);
        //}
    }

    private void CopyCodeButton_Click(object sender, EventArgs e)
    {
        //DataPackage package = new DataPackage();
        //package.SetText(actualCode);
        //Clipboard.SetContent(package);

        //VisualStateManager.GoToState(this, "ConfirmationDialogVisible", false);
        //Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        //// Automatically close teachingtip after 1 seconds
        //if (dispatcherQueue != null)
        //{
        //    dispatcherQueue.TryEnqueue(async () =>
        //    {
        //        await Task.Delay(1000);
        //        VisualStateManager.GoToState(this, "ConfirmationDialogHidden", false);
        //    });
        //}
    }
}
