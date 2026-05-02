using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;

namespace Microsoft.Maui.Platforms.Windows.WPF
{
	public class WPFBlazorWebView : Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebView
	{
		public override IFileProvider CreateFileProvider(string contentRootDir)
		{
			// Call into the platform-specific code to get that platform's asset file provider
			return ((Microsoft.AspNetCore.Components.WebView.Maui.WPF.BlazorWebViewHandler)(Handler!)).CreateFileProvider(contentRootDir);
		}
	}
}
