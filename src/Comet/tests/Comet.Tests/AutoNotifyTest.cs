using System;
using System.Collections.Generic;
using System.Text;
using Comet;
using Comet.Reactive;
using Xunit;

namespace Comet.Tests
{
	public class AutoNotifyTest : TestBase
	{

		public class MainPage : View
		{
			public readonly Reactive<string> value1 = new Reactive<string>("");
			public readonly Reactive<string> value2 = new Reactive<string>("");
			public Text TotalText { get; set; }
			[Body]
			View body()
				=> new VStack
				{
					(TotalText = new Text($"{value1.Value}{value2.Value}"))
				};
		}


		[Fact]
		public void VerifyAutoGeneratesAndUpdates()
		{
			var view = new MainPage();
			view.SetViewHandlerToGeneric();
			view.value1.Value = "Foo";
			ReactiveScheduler.FlushSync();
			view.value2.Value = "Bar";
			ReactiveScheduler.FlushSync();

			Assert.Equal("FooBar", view.TotalText.Value);

		}

	}
}
