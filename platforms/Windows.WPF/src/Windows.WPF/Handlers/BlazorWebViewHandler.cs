using System;
using System.Linq;
using System.Collections.Specialized;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platforms.Shared;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;

namespace Microsoft.AspNetCore.Components.WebView.Maui.WPF
{
#if ANDROID
	[SupportedOSPlatform("android23.0")]
#endif
	public partial class BlazorWebViewHandler
	{
		/// <summary>
		/// This field is part of MAUI infrastructure and is not intended for use by application code.
		/// </summary>
		public static PropertyMapper<BlazorWebView, BlazorWebViewHandler> BlazorWebViewMapper = new(ViewMapper)
		{
			[nameof(IBlazorWebView.HostPage)] = MapHostPage,
			[nameof(IBlazorWebView.RootComponents)] = MapRootComponents,
			[nameof(IBlazorWebView.StartPath)] = MapStartPath,
		};

		/// <summary>
		/// Initializes a new instance of <see cref="BlazorWebViewHandler"/> with default mappings.
		/// </summary>
		public BlazorWebViewHandler() : this(BlazorWebViewMapper)
		{
		}

		/// <summary>
		/// Initializes a new instance of <see cref="BlazorWebViewHandler"/> using the specified mappings.
		/// </summary>
		/// <param name="mapper">The property mappings.</param>
		public BlazorWebViewHandler(PropertyMapper? mapper) : base(mapper ?? BlazorWebViewMapper)
		{
		}

		//TODO
		//internal BlazorWebViewDeveloperTools DeveloperTools => MauiContext!.Services.GetRequiredService<BlazorWebViewDeveloperTools>();

		/// <summary>
		/// Maps the <see cref="IBlazorWebView.HostPage"/> property to the specified handler.
		/// </summary>
		/// <param name="handler">The <see cref="BlazorWebViewHandler"/>.</param>
		/// <param name="webView">The <see cref="IBlazorWebView"/>.</param>
		public static void MapHostPage(BlazorWebViewHandler handler, BlazorWebView webView)
		{
#if WINDOWS
			handler.PlatformView.HostPage = webView.HostPage!;
			//handler.StartWebViewCoreIfPossible();
#endif
		}

		/// <summary>
		/// Maps the <see cref="IBlazorWebView.RootComponents"/> property to the specified handler.
		/// </summary>
		/// <param name="handler">The <see cref="BlazorWebViewHandler"/>.</param>
		/// <param name="webView">The <see cref="IBlazorWebView"/>.</param>
		public static void MapRootComponents(BlazorWebViewHandler handler, BlazorWebView webView)
		{
#if WINDOWS
			handler.ObserveRootComponents(webView.RootComponents);
#endif
		}

		public static void MapStartPath(BlazorWebViewHandler handler, BlazorWebView webView)
			=> handler.PlatformView.StartPath = webView.StartPath;

		RootComponentsCollection? _observedRootComponents;
		readonly RootComponentReconciler<RootComponent> _rootComponentReconciler = new();
		readonly Dictionary<RootComponent, Wpf.RootComponent> _nativeRootComponents = [];

		void ObserveRootComponents(RootComponentsCollection? components)
		{
			if (_observedRootComponents != null)
				_observedRootComponents.CollectionChanged -= RootComponentsChanged;
			_observedRootComponents = components;
			if (components != null)
				components.CollectionChanged += RootComponentsChanged;
			SynchronizeRootComponents();
		}

		void RootComponentsChanged(object? sender, NotifyCollectionChangedEventArgs args)
			=> SynchronizeRootComponents();

		async void SynchronizeRootComponents()
		{
			try
			{
				await _rootComponentReconciler.SynchronizeAsync(
					() => _observedRootComponents?.ToArray() ?? [],
					component =>
					{
						var native = new Wpf.RootComponent
						{
							ComponentType = component.ComponentType!,
							Parameters = component.Parameters!,
							Selector = component.Selector!,
						};
						PlatformView.RootComponents.Add(native);
						_nativeRootComponents.Add(component, native);
						return Task.CompletedTask;
					},
					component =>
					{
						PlatformView.RootComponents.Remove(_nativeRootComponents[component]);
						_nativeRootComponents.Remove(component);
						return Task.CompletedTask;
					});
			}
			catch (Exception ex)
			{
				MauiContext?.Services.GetService<ILogger<BlazorWebViewHandler>>()?
					.LogError(ex, "WPF Blazor root component synchronization failed.");
				System.Diagnostics.Trace.TraceError($"WPF Blazor root component synchronization failed: {ex}");
			}
		}

#if WINDOWS
		private string? HostPage { get; set; }

		//internal void UrlLoading(UrlLoadingEventArgs args) =>
		//	VirtualView.UrlLoading(args);

		//private RootComponentsCollection? _rootComponents;

		//private RootComponentsCollection? RootComponents
		//{
		//	get => _rootComponents;
		//	set
		//	{
		//		if (_rootComponents != null)
		//		{
		//			// Remove any previously-known root components and unhook events
		//			_rootComponents.Clear();
		//			_rootComponents.CollectionChanged -= OnRootComponentsCollectionChanged;
		//		}

		//		_rootComponents = value;

		//		if (_rootComponents != null)
		//		{
		//			// Add new root components and hook events
		//			if (_rootComponents.Count > 0 && _webviewManager != null)
		//			{
		//				_webviewManager.Dispatcher.AssertAccess();
		//				foreach (var component in _rootComponents)
		//				{
		//					_ = component.AddToWebViewManagerAsync(_webviewManager);
		//				}
		//			}
		//			_rootComponents.CollectionChanged += OnRootComponentsCollectionChanged;
		//		}
		//	}
		//}

		//private void OnRootComponentsCollectionChanged(object? sender, global::System.Collections.Specialized.NotifyCollectionChangedEventArgs eventArgs)
		//{
		//	// If we haven't initialized yet, this is a no-op
		//	if (_webviewManager != null)
		//	{
		//		// Dispatch because this is going to be async, and we want to catch any errors
		//		_ = _webviewManager.Dispatcher.InvokeAsync(async () =>
		//		{
		//			var newItems = eventArgs.NewItems!.Cast<RootComponent>();
		//			var oldItems = eventArgs.OldItems!.Cast<RootComponent>();

		//			foreach (var item in newItems.Except(oldItems))
		//			{
		//				await item.AddToWebViewManagerAsync(_webviewManager);
		//			}

		//			foreach (var item in oldItems.Except(newItems))
		//			{
		//				await item.RemoveFromWebViewManagerAsync(_webviewManager);
		//			}
		//		});
		//	}
		//}
#endif
	}
}