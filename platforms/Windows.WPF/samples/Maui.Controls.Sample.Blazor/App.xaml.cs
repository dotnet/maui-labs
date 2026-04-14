using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Windows.WPF;

namespace Maui.Controls.Sample.Blazor
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : MauiWPFApplication
	{
		protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
	}
}
