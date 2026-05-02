using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Maui.LifecycleEvents.WPF;
using Microsoft.Maui.Platforms.Windows.WPF;
using Window = System.Windows.Window;

namespace Microsoft.Maui.Platforms.Windows.WPF
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MauiWPFWindow : Window
	{
		public MauiWPFWindow()
		{
		}

		protected override void OnActivated(EventArgs args)
		{
			base.OnActivated(args);
			MauiWPFApplication.Current.Services?.InvokeLifecycleEvents<WPFLifecycle.OnActivated>(del => del(this, args));
		}

		protected override void OnDeactivated(EventArgs e)
		{
			base.OnDeactivated(e);
		}
	}
}
