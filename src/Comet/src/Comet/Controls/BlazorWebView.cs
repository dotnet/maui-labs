using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Comet
{
	/// <summary>
	/// Root component descriptor for BlazorWebView.
	/// Maps a Blazor component to a DOM selector with optional parameters.
	/// </summary>
	public class RootComponent
	{
		/// <summary>
		/// CSS selector for the DOM element to render the component in.
		/// Example: "#app" for &lt;div id="app"&gt;&lt;/div&gt;
		/// </summary>
		public string Selector { get; set; }

		/// <summary>
		/// The Blazor component type to render.
		/// Example: typeof(App)
		/// </summary>
		public Type ComponentType { get; set; }

		/// <summary>
		/// Optional parameters to pass to the component.
		/// </summary>
		public IDictionary<string, object> Parameters { get; set; }
	}

	/// <summary>
	/// Comet MVU wrapper for MAUI's BlazorWebView.
	/// Enables rendering Blazor components within Comet's MVU application.
	/// 
	/// Usage:
	///   new BlazorWebView
	///   {
	///       HostPage = "wwwroot/index.html",
	///       StartPath = "/",
	///   }
	///   .RootComponent(typeof(App))
	/// </summary>
	public class BlazorWebView : WebView
	{
		private List<RootComponent> _rootComponents = new();

		/// <summary>
		/// Gets or sets the path to the HTML file to render.
		/// This is an app relative path to the file such as <c>wwwroot\index.html</c>
		/// </summary>
		public string HostPage { get; set; }

		/// <summary>
		/// Gets or sets the path for initial navigation within the Blazor navigation context.
		/// Default is "/".
		/// </summary>
		public string StartPath { get; set; } = "/";

		/// <summary>
		/// Gets the collection of root Blazor components to render.
		/// </summary>
		public IReadOnlyList<RootComponent> RootComponents => _rootComponents;

		/// <summary>
		/// Adds a root component to the BlazorWebView.
		/// </summary>
		/// <param name="selector">CSS selector for the DOM element (e.g., "#app")</param>
		/// <param name="componentType">The Blazor component type to render</param>
		/// <param name="parameters">Optional parameters to pass to the component</param>
		public BlazorWebView AddRootComponent(
			string selector,
			Type componentType,
			IDictionary<string, object> parameters = null)
		{
			if (string.IsNullOrWhiteSpace(selector))
				throw new ArgumentException("Selector cannot be empty", nameof(selector));
			if (componentType is null)
				throw new ArgumentNullException(nameof(componentType));

			_rootComponents.Add(new RootComponent
			{
				Selector = selector,
				ComponentType = componentType,
				Parameters = parameters ?? new Dictionary<string, object>()
			});

			return this;
		}

		/// <summary>
		/// Fluent method to add a root component with a generic type parameter.
		/// </summary>
		public BlazorWebView AddRootComponent<TComponent>(
			string selector,
			IDictionary<string, object> parameters = null)
		{
			return AddRootComponent(selector, typeof(TComponent), parameters);
		}

		/// <summary>
		/// Adds the default root component to "#app" selector.
		/// Convenience method for single-component apps.
		/// </summary>
		public BlazorWebView RootComponent(Type componentType, IDictionary<string, object> parameters = null)
		{
			return AddRootComponent("#app", componentType, parameters);
		}

		/// <summary>
		/// Adds the default root component to "#app" selector using a generic parameter.
		/// Convenience method for single-component apps.
		/// </summary>
		public BlazorWebView RootComponent<TComponent>(IDictionary<string, object> parameters = null)
		{
			return AddRootComponent<TComponent>("#app", parameters);
		}

		/// <summary>
		/// Fires when a URL is being loaded (for interception/validation).
		/// </summary>
		public event Action<string> OnUrlLoading;

		/// <summary>
		/// Fires when the BlazorWebView has finished initializing.
		/// </summary>
		public event Action OnBlazorWebViewInitialized;

		/// <summary>
		/// Raises the UrlLoading event.
		/// </summary>
		internal void RaiseUrlLoading(string url) => OnUrlLoading?.Invoke(url);

		/// <summary>
		/// Raises the BlazorWebViewInitialized event.
		/// </summary>
		internal void RaiseBlazorWebViewInitialized() => OnBlazorWebViewInitialized?.Invoke();
	}

	/// <summary>
	/// Hybrid WebView for advanced JavaScript interop scenarios.
	/// Extends WebView with explicit JS method invocation support.
	/// </summary>
	public class HybridWebView : WebView
	{
		/// <summary>
		/// Gets or sets the default HTML file to load.
		/// Default is "index.html".
		/// </summary>
		public string DefaultFile { get; set; } = "index.html";

		/// <summary>
		/// Invokes a JavaScript method with the given name and arguments.
		/// </summary>
		/// <param name="methodName">JavaScript function name (e.g., "window.alert")</param>
		/// <param name="args">Arguments to pass to the function</param>
		/// <returns>The JavaScript function's return value as a string</returns>
		public async Task<string> InvokeJavaScriptAsync(string methodName, params object[] args)
		{
			if (string.IsNullOrWhiteSpace(methodName))
				throw new ArgumentException("Method name cannot be empty", nameof(methodName));

			var script = $"{methodName}({string.Join(",", args.Select(a => System.Text.Json.JsonSerializer.Serialize(a)))})";
			return await ((Microsoft.Maui.IWebView)this).EvaluateJavaScriptAsync(script);
		}

		/// <summary>
		/// Fires when raw message data is received from JavaScript.
		/// </summary>
		public event EventHandler<string> RawMessageReceived;

		/// <summary>
		/// Sends a raw message string to JavaScript listeners.
		/// </summary>
		public void SendRawMessage(string message)
		{
			if (string.IsNullOrWhiteSpace(message))
				throw new ArgumentException("Message cannot be empty", nameof(message));

			RawMessageReceived?.Invoke(this, message);
		}
	}
}

