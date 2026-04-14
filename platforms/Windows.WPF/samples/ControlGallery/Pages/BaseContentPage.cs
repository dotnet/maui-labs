using ControlGallery.Services;

namespace ControlGallery.Pages;

public abstract class ContentPageBase : ContentPage
{
    public ContentPageBase()
    {
        Build();
    }
    
    protected virtual void Build()
    {

    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);

        Build();

#if DEBUG
        HotReloadService.UpdateApplicationEvent += ReloadUI;
#endif
    }
    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);

#if DEBUG
        HotReloadService.UpdateApplicationEvent -= ReloadUI;
#endif
    }

    private void ReloadUI(Type[] obj)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Build();
        });
    }
}

public abstract class BaseContentPage<T> : ContentPageBase where T : BaseViewModel
	{
		protected BaseContentPage(in T viewModel)//, in IAnalyticsService analyticsService, in IMainThread mainThread, in bool shouldUseSafeArea = false
			: base() //analyticsService, mainThread, shouldUseSafeArea
		{
			base.BindingContext = viewModel;
		}

		protected new T BindingContext => (T)base.BindingContext;
	}