using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Comet.Reactive;

namespace Comet
{
	public class FlyoutNavigationView<T> : FlyoutView, IFlyoutView
	{
		ListView<T> listView;
		PropertySubscription<IReadOnlyList<T>> _items;
		View detailView;
		PropertySubscription<IReadOnlyList<T>> Items
		{
			get => _items;
			set => this.SetPropertySubscription(ref _items, value);
		}

		PropertySubscription<int> _currentIndex;
		public PropertySubscription<int> CurrentIndex
		{
			get => _currentIndex;
			private set => this.SetPropertySubscription(ref _currentIndex, value);
		}
		public FlyoutNavigationView(IReadOnlyList<T> items, int currentIndex = 0)
		{
			CurrentIndex = currentIndex;
			Items = new PropertySubscription<IReadOnlyList<T>>(items);
			Setup();
		}

		public FlyoutNavigationView(Func<IReadOnlyList<T>> items, Func<int> currentIndex = null, Func<double> flyoutWidth = null)
		{
			CurrentIndex = currentIndex is not null ? PropertySubscription<int>.FromFunc(currentIndex) : null;
			Items = PropertySubscription<IReadOnlyList<T>>.FromFunc(items);
			Setup();
		}

		void Setup()
		{
			listView = new ListView<T>(Items)
			{
				ViewFor = (t) => MenuViewFor(t),
				ItemSelected = (t) => {
					var v = DetailViewFor?.Invoke((T)t.item);
					SetDetail(v);
					CurrentIndex?.Set(t.row);
				}
			};


		}

		IView IFlyoutView.Flyout => listView;
		IView IFlyoutView.Detail => detailView ??= getDetailView();
		public Func<T, View> MenuViewFor { get; set; } = (t) => {
			var title = t.ToString();
			if (t is View v)
				title = v.Title;
			return new Text(title);
		};
		View getDetailView()
		{
			var t = Items.CurrentValue[CurrentIndex.CurrentValue];
			var v = DetailViewFor?.Invoke(t);
			return v;
		}
		public Func<T, View> DetailViewFor { get; set; }
		protected void SetDetail(View view)
		{
			detailView = view;
			ViewHandler?.UpdateValue(nameof(IFlyoutView.Detail));
		}

	}
}
