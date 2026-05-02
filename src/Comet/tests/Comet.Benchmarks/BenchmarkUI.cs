using System;
using System.Collections.Generic;
using Comet;
using Comet.Benchmarks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.HotReload;

namespace Comet.Benchmarks
{
	/// <summary>
	/// Initializes Comet handler infrastructure for benchmarks (mirrors Comet.Tests.UI).
	/// </summary>
	public static class BenchmarkUI
	{
		static bool _initialized;
		public static IServiceProvider? Services { get; private set; }

		public static void Init()
		{
			if (_initialized) return;
			_initialized = true;

			ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

			var builder = MauiApp.CreateBuilder();
			builder.ConfigureMauiHandlers(handlers =>
			{
				handlers.AddHandler<Button, GenericViewHandler>();
				handlers.AddHandler<ContentView, GenericViewHandler>();
				handlers.AddHandler<Image, GenericViewHandler>();
				handlers.AddHandler<HStack, GenericViewHandler>();
				handlers.AddHandler<ListView, GenericViewHandler>();
				handlers.AddHandler<Text, GenericViewHandler>();
				handlers.AddHandler<TextField, GenericViewHandler>();
				handlers.AddHandler<ProgressBar, GenericViewHandler>();
				handlers.AddHandler<SecureField, GenericViewHandler>();
				handlers.AddHandler<ScrollView, GenericViewHandler>();
				handlers.AddHandler<Slider, GenericViewHandler>();
				handlers.AddHandler<Toggle, GenericViewHandler>();
				handlers.AddHandler<View, GenericViewHandler>();
				handlers.AddHandler<VStack, GenericViewHandler>();
				handlers.AddHandler<ZStack, GenericViewHandler>();
			});

			var app = builder.Build();
			Services = app.Services;

			MauiHotReloadHelper.IsEnabled = false; // Disable for benchmarks

			var style = new Styles.Style();
			style.Apply();
		}

		public static void InitializeHandlers(View view)
		{
			if (view == null) return;

			var handler = view.ViewHandler;
			if (handler == null)
			{
				var factory = Services!.GetRequiredService<IMauiHandlersFactory>();
				handler = factory.GetHandler(view.GetType());
				view.ViewHandler = handler;
				handler.SetVirtualView(view);
			}

			if (view is AbstractLayout layout)
			{
				foreach (var subView in layout)
					InitializeHandlers(subView);
			}
			else if (view is ContentView contentView)
			{
				InitializeHandlers(contentView.Content);
			}
			else if (view.BuiltView != null)
			{
				InitializeHandlers(view.BuiltView);
			}
		}
	}
}
