using System;
using Comet.HotReload;
using Comet.Internal;
using Comet.Reactive;
using Microsoft.Maui.HotReload;
using Xunit;
namespace Comet.Tests
{
	public class ReloadTransfersStateTest : TestBase
	{
		[Fact]
		public void StateIsTransferedToReloadedView()
		{
			ResetComet();
			const string textValue = "Hello";
			var orgView = new MyOrgView();

			// Trigger initial build
			var orgText = orgView.GetView() as Text;
			orgView.title.Value = textValue;

			var originalTitle = orgView.title;

			CometHotReloadHelper.RegisterReplacedView(typeof(MyOrgView).FullName, typeof(MyNewView));
			// GetView() synchronously triggers GetRenderView which detects the replacement
			var newText = orgView.GetView() as Text;

			var v = orgView.GetReplacedView();
			var newView = v as MyNewView;
			Assert.NotNull(newView);
			Assert.Same(originalTitle, newView.title);
			Assert.Equal(textValue, newView.title.Value);
		}


		[Fact]
		public void StateTransfersSignalInstances()
		{
			ResetComet();
			const string textValue = "Hello";
			var orgView = new MyOrgView();

			// Trigger initial build
			var orgText = orgView.GetView() as Text;
			orgView.title.Value = textValue;
			Assert.True(orgView.isEnabled.Value);

			var originalTitle = orgView.title;
			var originalIsEnabled = orgView.isEnabled;

			CometHotReloadHelper.RegisterReplacedView(typeof(MyOrgView).FullName, typeof(MyNewView));
			// GetView() synchronously triggers GetRenderView which detects the replacement
			orgView.GetView();

			var v = orgView.GetReplacedView();
			var newView = v as MyNewView;
			Assert.IsType<MyNewView>(v);
			Assert.NotNull(newView);
			Assert.Same(originalTitle, newView.title);
			Assert.Same(originalIsEnabled, newView.isEnabled);
			Assert.Equal(textValue, newView.title.Value);
			Assert.True(newView.isEnabled.Value);
		}


		public class MyOrgView : View
		{
			public readonly Signal<string> title = new("");
			public readonly Signal<bool> isEnabled = new(true);

			readonly Reactive<bool> MyBoolean = false;

			[Body]
			View body() => new Text(title.Value);

		}
		public class MyNewView : View
		{
			public readonly Signal<string> title = new("");
			public readonly Signal<bool> isEnabled = new(false);

			readonly Reactive<bool> MyBoolean = false;

			[Body]
			View body() => new Text(title.Value);
		}
	}
}
