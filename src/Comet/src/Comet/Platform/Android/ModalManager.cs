using System;
using System.Collections.Generic;
using System.Linq;
using Android.OS;
using Android.Views;
using AndroidX.Fragment.App;
using AView = Android.Views.View;
using Microsoft.Maui.Platform;
using AndroidX.AppCompat.App;

namespace Comet.Android.Controls
{
	public class ModalManager
	{
		static ViewModal currrentDialog;
		static List<View> currentDialogs = new List<View>();
		public ModalManager()
		{
		}
		static FragmentManager FragmentManager(View view) =>  (view.GetMauiContext().Context as AppCompatActivity)?.SupportFragmentManager;
		public static void ShowModal(View view)
		{
			var transaction = FragmentManager(view).BeginTransaction();
			if (currrentDialog is not null)
				transaction.Remove(currrentDialog);
			transaction.AddToBackStack(null);

			var dialog = new ViewModal(view);
			currentDialogs.Add(dialog.HView);
			currrentDialog = dialog;
			dialog.Show(transaction, "dialog");
		}
		public static void DismisModal() => PerformDismiss(true);
		static void PerformDismiss(bool removeCurrent = true)
		{
			if (currrentDialog is null)
				return;
			var transaction = FragmentManager(currrentDialog.HView).BeginTransaction();

			if (removeCurrent)
			{
				transaction.Remove(currrentDialog);
				transaction.AddToBackStack(null);
			}

			currentDialogs.Remove(currrentDialog.HView);
			currrentDialog = null;
			var currentView = currentDialogs.LastOrDefault();
			if (currentView is null)
			{
				transaction.CommitAllowingStateLoss();
				return;
			}

			currrentDialog = new ViewModal(currentView);
			currrentDialog.Show(transaction, "dialog");
		}


		class ViewModal : DialogFragment
		{
			public ViewModal(View view)
			{
				HView = view;
			}

			public View HView { get; }

			AView currentBuiltView;
			public override AView OnCreateView(LayoutInflater inflater,
				ViewGroup container,
				Bundle savedInstanceState) => currentBuiltView = HView.ToContainerView(HView.GetMauiContext());

			public override void OnDestroy()
			{
				PerformDismiss(false);
				if (HView is not null)
				{
					HView.ViewHandler = null;
				}
				if (currentBuiltView is not null)
				{
					currentBuiltView?.Dispose();
					currentBuiltView = null;
				}
				base.OnDestroy();
				this.Dispose();
			}
		}
	}
}
