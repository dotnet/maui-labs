// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform;

namespace Microsoft.Maui.Go.CompanionApp;

/// <summary>
/// Present a MAUI ContentPage modally from CometApp
/// (CometApp doesn't use standard MAUI Controls.Application navigation).
/// </summary>
static class ModalPresenter
{
	public static bool Present(ContentPage page)
	{
#if IOS || MACCATALYST
		var mauiContext = Comet.CometApp.MauiContext;
		if (mauiContext is null) return false;

		var keyWindow = UIKit.UIApplication.SharedApplication.ConnectedScenes
			.OfType<UIKit.UIWindowScene>()
			.SelectMany(s => s.Windows)
			.FirstOrDefault(w => w.IsKeyWindow);

		var vc = keyWindow?.RootViewController;
		while (vc?.PresentedViewController is not null)
			vc = vc.PresentedViewController;

		if (vc is null) return false;

		var handler = page.ToHandler(mauiContext);
		var pageVc = handler.ViewController;
		if (pageVc is null) return false;

		pageVc.ModalPresentationStyle = UIKit.UIModalPresentationStyle.FullScreen;
		vc.PresentViewController(pageVc, true, null);
		return true;
#elif ANDROID
		// TODO: Android modal presentation
		return false;
#else
		return false;
#endif
	}
}
