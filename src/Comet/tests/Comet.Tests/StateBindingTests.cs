using System;
using Comet.Internal;
using Xunit;

namespace Comet.Tests
{
	public class StateBindingTests : TestBase
	{
		public class MyDataModel
		{
			readonly Reactive<int> _childCount = new Reactive<int>(10);
			readonly Reactive<int> _parentCount = new Reactive<int>(10);

			public int ChildCount
			{
				get => _childCount.Value;
				set => _childCount.Value = value;
			}
			public int ParentCount
			{
				get => _parentCount.Value;
				set => _parentCount.Value = value;
			}
		}

		public class ParentView : View
		{
			[State]
			public readonly MyDataModel model = new MyDataModel();
		}

		public class ChildView : View
		{
			[Environment]
			public readonly MyDataModel model;
		}


		[Fact]
		public void BuildingChildren()
		{
			ParentView parent = null;
			parent = new ParentView
			{
				Body = () => new View
				{
					Body = () => new Text($"Child Click Count: {parent.model.ChildCount}")
				}
			};
		}
	}
}
