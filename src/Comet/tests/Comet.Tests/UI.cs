using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Comet.Tests.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.HotReload;

namespace Comet.Tests
{
	public static class UI
	{
		static bool hasInit;
		public static IServiceProvider Services { get; set; }
		public static void Init(bool force = false)
		{
			if (hasInit && !force)
				return;
			hasInit = true;
			ThreadHelper.SetFireOnMainThread((a) => a?.Invoke()); 

			var builder = MauiApp.CreateBuilder();
			builder.ConfigureMauiHandlers(handlers =>
			{
				handlers.AddHandler<Button, GenericViewHandler>();
				handlers.AddHandler<ContentView, GenericViewHandler>();
				handlers.AddHandler<Image, GenericViewHandler>();
				handlers.AddHandler<HStack, GenericViewHandler>();
				handlers.AddHandler<ListView, GenericViewHandler>();
				handlers.AddHandler<NativeHost, GenericViewHandler>();
				handlers.AddHandler<Text, TextHandler>();
				handlers.AddHandler<TextField, TextFieldHandler>();
				handlers.AddHandler<ProgressBar, ProgressBarHandler>();
				handlers.AddHandler<SecureField, SecureFieldHandler>();
				handlers.AddHandler<ScrollView, GenericViewHandler>();
				handlers.AddHandler<Slider, SliderHandler>();
				handlers.AddHandler<Toggle, GenericViewHandler>();
				handlers.AddHandler<View, GenericViewHandler>();
				handlers.AddHandler<VStack, GenericViewHandler>();
				handlers.AddHandler<ZStack, GenericViewHandler>();
			});

			var app = builder.Build();
			Services = app.Services;

			MauiHotReloadHelper.IsEnabled = true;
		}
	}
}
