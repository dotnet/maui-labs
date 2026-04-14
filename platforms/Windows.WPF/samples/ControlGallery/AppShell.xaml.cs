using System.Diagnostics;
using Animations;
using CarouselGallery.Views;
using CollectionViewDemos.Views;
using CommunityToolkit.Mvvm.Messaging;
using ControlGallery.Common.Messages;
using ControlGallery.Pages;
using ControlGallery.Pages.Controls.CarouselView;
using ControlGallery.Pages.Features.Animation;
using ControlGallery.Pages.Layouts;
using ControlGallery.Pages.Layouts.AbsoluteLayouts;
using Flexibility.Shared;
namespace ControlGallery;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        RegisterRoutes();

        BindingContext = new AppShellViewModel();

        this.SizeChanged += UpdateFlyoutBehaviorIfNeeded;
    }

    private const double minPageWidth = 800;
    private void UpdateFlyoutBehaviorIfNeeded(object sender, EventArgs e)
    {
        var window = (VisualElement)sender;
        double currentWidth = window.Width + Shell.Current.FlyoutWidth; // don't get this, insteaed get the window size

        Debug.WriteLine($"currentWidth: {currentWidth}, " +
        $"PageWidth: {window.Width}, " +
            $"Shell.Current.FlyoutWidth: {Shell.Current.FlyoutWidth}");

        if (currentWidth < minPageWidth && Shell.Current.FlyoutWidth > 75)
        {
            // Shell.Current.FlyoutBehavior = FlyoutBehavior.Flyout;
            Shell.Current.FlyoutWidth = 75;
            WeakReferenceMessenger.Default.Send(new ToggleFlyoutHeaderMsg(false));
        }
        else if (currentWidth > minPageWidth && Shell.Current.FlyoutWidth < 320)
        {
            // Shell.Current.FlyoutBehavior = FlyoutBehavior.Locked;
            Shell.Current.FlyoutWidth = 320;
            WeakReferenceMessenger.Default.Send(new ToggleFlyoutHeaderMsg(true));
        }
    }

    private void RegisterRoutes()
    {
        Routing.RegisterRoute(nameof(ProfileReportPage), typeof(ProfileReportPage));

        Routing.RegisterRoute(nameof(ActivityIndicatorPage), typeof(ActivityIndicatorPage));
        Routing.RegisterRoute(nameof(AlertPage), typeof(AlertPage));
        Routing.RegisterRoute(nameof(BorderPage), typeof(BorderPage));
        Routing.RegisterRoute(nameof(ButtonPage), typeof(ButtonPage));
        Routing.RegisterRoute(nameof(CheckboxPage), typeof(CheckboxPage));
        Routing.RegisterRoute(nameof(DatePickerPage), typeof(DatePickerPage));
        Routing.RegisterRoute(nameof(EditorPage), typeof(EditorPage));
        Routing.RegisterRoute(nameof(EntryPage), typeof(EntryPage));
        Routing.RegisterRoute(nameof(ImagePage), typeof(ImagePage));
        Routing.RegisterRoute(nameof(LabelPage), typeof(LabelPage));
        Routing.RegisterRoute(nameof(MapPage), typeof(MapPage));
        Routing.RegisterRoute(nameof(ProgressBarPage), typeof(ProgressBarPage));
        Routing.RegisterRoute(nameof(RadioButtonPage), typeof(RadioButtonPage));
        Routing.RegisterRoute(nameof(ShapesPage), typeof(ShapesPage));
        Routing.RegisterRoute(nameof(SliderPage), typeof(SliderPage));
        Routing.RegisterRoute(nameof(StepperPage), typeof(StepperPage));
        Routing.RegisterRoute(nameof(SwitchPage), typeof(SwitchPage));
        Routing.RegisterRoute(nameof(RefreshViewPage), typeof(RefreshViewPage));
        Routing.RegisterRoute(nameof(WebViewPage), typeof(WebViewPage));
        Routing.RegisterRoute(nameof(TimePickerPage), typeof(TimePickerPage));
        Routing.RegisterRoute(nameof(PickerPage), typeof(PickerPage));
        Routing.RegisterRoute(nameof(SearchBarPage), typeof(SearchBarPage));
        Routing.RegisterRoute(nameof(BoxViewPage), typeof(BoxViewPage));

        // Layouts
        Routing.RegisterRoute(nameof(BasisExperimentPage), typeof(BasisExperimentPage));
        Routing.RegisterRoute(nameof(CatalogItemsPage), typeof(CatalogItemsPage));
        Routing.RegisterRoute(nameof(CssCatalogItemsPage), typeof(CssCatalogItemsPage));
        Routing.RegisterRoute(nameof(GrowExperimentPage), typeof(GrowExperimentPage));
        Routing.RegisterRoute(nameof(PhotoWrappingPage), typeof(PhotoWrappingPage));
        Routing.RegisterRoute(nameof(ShrinkExperimentPage), typeof(ShrinkExperimentPage));
        Routing.RegisterRoute(nameof(SimpleStackPage), typeof(SimpleStackPage));

        Routing.RegisterRoute(nameof(AlignmentGridPage), typeof(AlignmentGridPage));
        Routing.RegisterRoute(nameof(BasicGridPage), typeof(BasicGridPage));
        Routing.RegisterRoute(nameof(CalculatorPage), typeof(CalculatorPage));
        Routing.RegisterRoute(nameof(KeypadPage), typeof(KeypadPage));
        Routing.RegisterRoute(nameof(SliderGridPage), typeof(SliderGridPage));
        Routing.RegisterRoute(nameof(SpacingGridPage), typeof(SpacingGridPage));

        Routing.RegisterRoute(nameof(AlignmentStackPage), typeof(AlignmentStackPage));
        Routing.RegisterRoute(nameof(CombinedStackPage), typeof(CombinedStackPage));
        Routing.RegisterRoute(nameof(ExpansionStackPage), typeof(ExpansionStackPage));
        Routing.RegisterRoute(nameof(HorizontalStackPage), typeof(HorizontalStackPage));
        Routing.RegisterRoute(nameof(SpacingStackPage), typeof(SpacingStackPage));
        Routing.RegisterRoute(nameof(VerticalStackPage), typeof(VerticalStackPage));
        Routing.RegisterRoute(nameof(AndExpandPage), typeof(AndExpandPage));

        Routing.RegisterRoute(nameof(HorizontalWrapLayoutPage), typeof(HorizontalWrapLayoutPage));
        Routing.RegisterRoute(nameof(CascadeLayoutPage), typeof(CascadeLayoutPage));
        Routing.RegisterRoute(nameof(ColumnLayoutPage), typeof(ColumnLayoutPage));


        Routing.RegisterRoute(nameof(MarginPaddingPage), typeof(MarginPaddingPage));



        Routing.RegisterRoute(nameof(BouncingTextDemoPage), typeof(BouncingTextDemoPage));
        Routing.RegisterRoute(nameof(ChessboardDemoPage), typeof(ChessboardDemoPage));
        Routing.RegisterRoute(nameof(ProportionalCoordinateCalcDemoPage), typeof(ProportionalCoordinateCalcDemoPage));
        Routing.RegisterRoute(nameof(ProportionalDemoPage), typeof(ProportionalDemoPage));
        Routing.RegisterRoute(nameof(SimpleOverlayDemoPage), typeof(SimpleOverlayDemoPage));
        Routing.RegisterRoute(nameof(StylishHeaderDemoPage), typeof(StylishHeaderDemoPage));

        Routing.RegisterRoute(nameof(CollectionViewPage), typeof(CollectionViewPage));
        Routing.RegisterRoute(nameof(EmptyViewDataTemplateSelectorPage), typeof(EmptyViewDataTemplateSelectorPage));
        Routing.RegisterRoute(nameof(EmptyViewFilteredPage), typeof(EmptyViewFilteredPage));
        Routing.RegisterRoute(nameof(EmptyViewLoadSimulationPage), typeof(EmptyViewLoadSimulationPage));
        Routing.RegisterRoute(nameof(EmptyViewNullPage), typeof(EmptyViewNullPage));
        Routing.RegisterRoute(nameof(EmptyViewSwapPage), typeof(EmptyViewSwapPage));
        Routing.RegisterRoute(nameof(EmptyViewTemplatePage), typeof(EmptyViewTemplatePage));
        Routing.RegisterRoute(nameof(EmptyViewWithViewsFilteredPage), typeof(EmptyViewWithViewsFilteredPage));

        Routing.RegisterRoute(nameof(VerticalListEmptyGroupsPage), typeof(VerticalListEmptyGroupsPage));
        Routing.RegisterRoute(nameof(VerticalListGroupingPage), typeof(VerticalListGroupingPage));
        Routing.RegisterRoute(nameof(VerticalListGroupingVariableSizeItemsPage), typeof(VerticalListGroupingVariableSizeItemsPage));
        Routing.RegisterRoute(nameof(VerticalListTextGroupingPage), typeof(VerticalListTextGroupingPage));
        Routing.RegisterRoute(nameof(VerticalGridTextPage), typeof(VerticalGridTextPage));
        Routing.RegisterRoute(nameof(VerticalListSpacingPage), typeof(VerticalListSpacingPage));
        Routing.RegisterRoute(nameof(HorizontalGridSpacingPage), typeof(HorizontalGridSpacingPage));
        Routing.RegisterRoute(nameof(HorizontalListSpacingPage), typeof(HorizontalListSpacingPage));
        Routing.RegisterRoute(nameof(VerticalGridSpacingPage), typeof(VerticalGridSpacingPage));

        Routing.RegisterRoute(nameof(VerticalListSwipeContextItemsPage), typeof(VerticalListSwipeContextItemsPage));
        Routing.RegisterRoute(nameof(VerticalListSnapPointsPage), typeof(VerticalListSnapPointsPage));

        Routing.RegisterRoute(nameof(VerticalListDynamicSizeItemsPage), typeof(VerticalListDynamicSizeItemsPage));
        Routing.RegisterRoute(nameof(VerticalListVariableSizeItemsPage), typeof(VerticalListVariableSizeItemsPage));

        Routing.RegisterRoute(nameof(VerticalListSnapPointsPage), typeof(VerticalListSnapPointsPage));
        Routing.RegisterRoute(nameof(VerticalListSnapPointsPage), typeof(VerticalListSnapPointsPage));


        Routing.RegisterRoute(nameof(HorizontalGridHeaderFooterViewPage), typeof(HorizontalGridHeaderFooterViewPage));
        Routing.RegisterRoute(nameof(VerticalListHeaderFooterDataTemplatePage), typeof(VerticalListHeaderFooterDataTemplatePage));
        Routing.RegisterRoute(nameof(VerticalListHeaderFooterStringPage), typeof(VerticalListHeaderFooterStringPage));
        Routing.RegisterRoute(nameof(VerticalListHeaderFooterViewPage), typeof(VerticalListHeaderFooterViewPage));

        Routing.RegisterRoute(nameof(HorizontalGridPage), typeof(HorizontalGridPage));
        Routing.RegisterRoute(nameof(HorizontalGridTextPage), typeof(HorizontalGridTextPage));
        Routing.RegisterRoute(nameof(HorizontalListPage), typeof(HorizontalListPage));
        Routing.RegisterRoute(nameof(HorizontalListTextPage), typeof(HorizontalListTextPage));
        Routing.RegisterRoute(nameof(VerticalGridPage), typeof(VerticalGridPage));
        Routing.RegisterRoute(nameof(VerticalListDataTemplateSelectorPage), typeof(VerticalListDataTemplateSelectorPage));
        Routing.RegisterRoute(nameof(VerticalListPage), typeof(VerticalListPage));
        Routing.RegisterRoute(nameof(VerticalListRTLPage), typeof(VerticalListRTLPage));
        Routing.RegisterRoute(nameof(VerticalListTextPage), typeof(VerticalListTextPage));

        Routing.RegisterRoute(nameof(HorizontalGridPullToRefreshPage), typeof(HorizontalGridPullToRefreshPage));
        Routing.RegisterRoute(nameof(VerticalListPullToRefreshPage), typeof(VerticalListPullToRefreshPage));

        Routing.RegisterRoute(nameof(VerticalListMultiplePreSelectionPage), typeof(VerticalListMultiplePreSelectionPage));
        Routing.RegisterRoute(nameof(VerticalListMultipleSelectionPage), typeof(VerticalListMultipleSelectionPage));
        Routing.RegisterRoute(nameof(VerticalListSelectionColorPage), typeof(VerticalListSelectionColorPage));
        Routing.RegisterRoute(nameof(VerticalListSinglePreSelectionPage), typeof(VerticalListSinglePreSelectionPage));
        Routing.RegisterRoute(nameof(VerticalListSingleSelectionPage), typeof(VerticalListSingleSelectionPage));

        Routing.RegisterRoute(nameof(IncrementalLoadingPage), typeof(IncrementalLoadingPage));
        Routing.RegisterRoute(nameof(ItemsUpdatingScrollModePage), typeof(ItemsUpdatingScrollModePage));
        Routing.RegisterRoute(nameof(ScrollToByIndexPage), typeof(ScrollToByIndexPage));
        Routing.RegisterRoute(nameof(ScrollToByIndexWithGroupingPage), typeof(ScrollToByIndexWithGroupingPage));
        Routing.RegisterRoute(nameof(ScrollToByObjectPage), typeof(ScrollToByObjectPage));
        Routing.RegisterRoute(nameof(ScrollToByObjectWithGroupingPage), typeof(ScrollToByObjectWithGroupingPage));

        // Features pages
        Routing.RegisterRoute(nameof(AppThemePage), typeof(AppThemePage));
        Routing.RegisterRoute(nameof(BehaviorsPage), typeof(BehaviorsPage));
        Routing.RegisterRoute(nameof(ColorsPage), typeof(ColorsPage));
        Routing.RegisterRoute(nameof(FontImagePage), typeof(FontImagePage));
        Routing.RegisterRoute(nameof(MenuBarPage), typeof(MenuBarPage));
        Routing.RegisterRoute(nameof(TriggersPage), typeof(TriggersPage));
        Routing.RegisterRoute(nameof(NativeViewsPage), typeof(NativeViewsPage));
        Routing.RegisterRoute(nameof(ClippingPage), typeof(ClippingPage));
        Routing.RegisterRoute(nameof(ShadowPage), typeof(ShadowPage));
        Routing.RegisterRoute(nameof(ContextMenuPage), typeof(ContextMenuPage));
        Routing.RegisterRoute(nameof(TooltipPage), typeof(TooltipPage));
        Routing.RegisterRoute(nameof(GesturesPage), typeof(GesturesPage));
        Routing.RegisterRoute(nameof(TapGesturePage), typeof(TapGesturePage));
        Routing.RegisterRoute(nameof(PointerGesturePage), typeof(PointerGesturePage));

        Routing.RegisterRoute(nameof(CarouselViewPage), typeof(CarouselViewPage));
        Routing.RegisterRoute(nameof(BasicCarouselPage), typeof(BasicCarouselPage));
        Routing.RegisterRoute(nameof(InstagramPage), typeof(InstagramPage));
        Routing.RegisterRoute(nameof(LufthansaPage), typeof(LufthansaPage));
        Routing.RegisterRoute(nameof(MarriottPage), typeof(MarriottPage));
        Routing.RegisterRoute(nameof(MintPage), typeof(MintPage));
        Routing.RegisterRoute(nameof(REIPage), typeof(REIPage));

        Routing.RegisterRoute(nameof(HybridWebViewPage), typeof(HybridWebViewPage));

        Routing.RegisterRoute(nameof(AchievementsPage), typeof(AchievementsPage));
        Routing.RegisterRoute(nameof(AlignmentPage), typeof(AlignmentPage));
        Routing.RegisterRoute(nameof(GridPage), typeof(GridPage));
        Routing.RegisterRoute(nameof(LoginPage), typeof(LoginPage));
        Routing.RegisterRoute(nameof(PhotosPage), typeof(PhotosPage));
        Routing.RegisterRoute(nameof(ReadingColumnsPage), typeof(ReadingColumnsPage));
        Routing.RegisterRoute(nameof(ShowPage), typeof(ShowPage));

        // Animations
        Routing.RegisterRoute(nameof(AnimationsPage), typeof(AnimationsPage));
        Routing.RegisterRoute(nameof(CustomAnimationPage), typeof(CustomAnimationPage));
        Routing.RegisterRoute(nameof(EasingsPage), typeof(EasingsPage));
        Routing.RegisterRoute(nameof(EasingEditorPage), typeof(EasingEditorPage));
        Routing.RegisterRoute(nameof(FadePage), typeof(FadePage));
        Routing.RegisterRoute(nameof(LayoutToPage), typeof(LayoutToPage));
        Routing.RegisterRoute(nameof(RotatePage), typeof(RotatePage));
        Routing.RegisterRoute(nameof(ScalePage), typeof(ScalePage));
        Routing.RegisterRoute(nameof(TranslatePage), typeof(TranslatePage));


    }
}

public partial class AppShellViewModel : ObservableObject
{
    private string appearance = App.Current.UserAppTheme.ToString();

    public string Appearance
    {
        get => appearance;
        set
        {
            if (appearance == value)
                return;

            appearance = value;

            switch (appearance)
            {
                case "Light":
                    App.Current.UserAppTheme = AppTheme.Light;
                    break;
                case "Dark":
                    App.Current.UserAppTheme = AppTheme.Dark;
                    break;
                default:
                    App.Current.UserAppTheme = AppTheme.Unspecified;
                    break;
            }
        }
    }



    public AppShellViewModel()
    {
        App.Current.RequestedThemeChanged += Current_RequestedThemeChanged;

    }





    private void Current_RequestedThemeChanged(object sender, AppThemeChangedEventArgs e)
    {
        appearance = App.Current.UserAppTheme.ToString();
        Console.WriteLine($"{App.Current.PlatformAppTheme} | {App.Current.UserAppTheme}");
        OnPropertyChanged(nameof(Appearance));
    }


}
