using System;
using System.Collections.ObjectModel;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class Issue125 : Component
	{
		private class TodoItem
		{
			public string Name { get; set; }
			public bool Done { get; set; }
		}

		readonly ObservableCollection<TodoItem> items = new ObservableCollection<TodoItem>{
			new TodoItem{
				Name = "Hi",
				Done = true,
			},
			new TodoItem
			{
				Name ="Finish Tasky",
			}
		};


		public override View Render() =>
			new ListView<TodoItem>(items)
			{
				ViewFor = (item) => HStack(
					Text(item.Name).Alignment(Alignment.Leading),
					Spacer(),
					Toggle(item.Done).Alignment(Alignment.Center)
				).Margin(6).FillHorizontal()
			}.Title("Tasky");
	}
}
