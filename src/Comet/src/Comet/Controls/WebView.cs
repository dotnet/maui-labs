using System;
using System.Net;
using Comet.Reactive;
using Microsoft.Maui;

namespace Comet
{
	internal class CometHtmlWebViewSource : IWebViewSource
	{
		public string Html { get; set; }
		public void Load(IWebViewDelegate webViewDelegate) => webViewDelegate.LoadHtml(Html, null);
	}

	internal class CometUrlWebViewSource : IWebViewSource
	{
		public string Url { get; set; }
		public void Load(IWebViewDelegate webViewDelegate) => webViewDelegate.LoadUrl(Url);
	}

	public class WebView : View, IWebView
	{
		PropertySubscription<string> html;
		public PropertySubscription<string> Html
		{
			get => html;
			set => this.SetPropertySubscription(ref html, value);
		}

		PropertySubscription<string> source;
		public PropertySubscription<string> Source
		{
			get => source;
			set => this.SetPropertySubscription(ref source, value);
		}

		public Action<string> OnNavigated { get; set; }
		public Action<string> OnNavigating { get; set; }

		IWebViewSource IWebView.Source
		{
			get
			{
				var src = Source?.CurrentValue;
				var htm = Html?.CurrentValue;
				if (!string.IsNullOrEmpty(htm))
					return new CometHtmlWebViewSource { Html = htm };
				if (!string.IsNullOrEmpty(src))
					return new CometUrlWebViewSource { Url = src };
				return null;
			}
		}

		bool IWebView.CanGoBack { get; set; }
		bool IWebView.CanGoForward { get; set; }
		string IWebView.UserAgent { get; set; }
		CookieContainer IWebView.Cookies => _cookies ??= new CookieContainer();
		CookieContainer _cookies;

		void IWebView.GoBack() => ViewHandler?.Invoke(nameof(IWebView.GoBack));
		void IWebView.GoForward() => ViewHandler?.Invoke(nameof(IWebView.GoForward));
		void IWebView.Reload() => ViewHandler?.Invoke(nameof(IWebView.Reload));
		void IWebView.Eval(string script) => ViewHandler?.Invoke(nameof(IWebView.Eval), script);
		Task<string> IWebView.EvaluateJavaScriptAsync(string script) => Task.FromResult<string>(null);

		bool IWebView.Navigating(WebNavigationEvent evnt, string url)
		{
			OnNavigating?.Invoke(url);
			return false; // false = allow navigation (true would cancel it)
		}

		void IWebView.Navigated(WebNavigationEvent evnt, string url, WebNavigationResult result)
		{
			OnNavigated?.Invoke(url);
		}
	}
}
