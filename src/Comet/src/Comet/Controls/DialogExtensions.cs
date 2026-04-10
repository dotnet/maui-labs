using System;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace Comet
{
	public static class DialogExtensions
	{
		public static async Task<bool> DisplayAlert(this View view, string title, string message, string accept, string cancel)
		{
			var page = GetPage(view);
			if (page is not null)
				return await page.DisplayAlert(title, message, accept, cancel);
			return false;
		}

		public static async Task DisplayAlert(this View view, string title, string message, string cancel)
		{
			var page = GetPage(view);
			if (page is not null)
				await page.DisplayAlert(title, message, cancel);
		}

		public static async Task<string> DisplayActionSheet(this View view, string title, string cancel, string destruction, params string[] buttons)
		{
			var page = GetPage(view);
			if (page is not null)
				return await page.DisplayActionSheet(title, cancel, destruction, buttons);
			return null;
		}

		public static async Task<string> DisplayPromptAsync(this View view, string title, string message = null, string accept = "OK", string cancel = "Cancel", string placeholder = null, int maxLength = -1, Microsoft.Maui.Keyboard keyboard = null, string initialValue = "")
		{
			var page = GetPage(view);
			if (page is not null)
				return await page.DisplayPromptAsync(title, message, accept, cancel, placeholder, maxLength, keyboard, initialValue);
			return null;
		}

		private static Page GetPage(View view)
		{
			// Try to get the MAUI context and find the current page
			var context = view.GetMauiContext();
			if (context?.Services is not null)
			{
				// Try to get the platform window
				try
				{
					var window = context.Services.GetService(typeof(IWindow)) as IWindow;
					if (window is Microsoft.Maui.Controls.Window mauiWindow && mauiWindow.Page is Page page)
						return page;
				}
				catch
				{
					// Ignore if service not available
				}
			}

			// Fallback: try Application.Current.MainPage
			if (Microsoft.Maui.Controls.Application.Current?.MainPage is Page mainPage)
				return mainPage;

			return null;
		}
	}
}
