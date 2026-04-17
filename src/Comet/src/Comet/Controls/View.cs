using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Reflection;
using Comet.Helpers;
using Comet.HotReload;
using Comet.Internal;
using Comet.Reactive;
using Comet.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Animations;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.HotReload;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Primitives;

namespace Comet
{

	public class HandlerChangingEventArgs : EventArgs
	{
		public HandlerChangingEventArgs(IElementHandler oldHandler, IElementHandler newHandler)
		{
			OldHandler = oldHandler;
			NewHandler = newHandler;
		}

		public IElementHandler OldHandler { get; }
		public IElementHandler NewHandler { get; }
	}

	public class View : ContextualObject, IDisposable, IView, IHotReloadableView, ISafeAreaView, IContentTypeHash, IAnimator, ITitledElement, IGestureView, IVisualTreeElement, IPadding
	{
		static internal readonly WeakList<IView> ActiveViews = new WeakList<IView>();
		static internal readonly object ActiveViewsLock = new object();
		HashSet<(string Field, string Key)> usedEnvironmentData = new HashSet<(string Field, string Key)>();
		HashSet<IReactiveSource>? _bodyDependencies;
		BodyDependencySubscriber? _bodySubscriber;
		List<IDisposable>? _propertySubscriptions;
		protected static Dictionary<string, string> HandlerPropertyMapper = new()
		{
			[nameof(MeasuredSize)] = nameof(IView.DesiredSize),
			[EnvironmentKeys.Fonts.Size] = nameof(IText.Font),
			[EnvironmentKeys.Fonts.Slant] = nameof(IText.Font),
			[EnvironmentKeys.Fonts.Family] = nameof(IText.Font),
			[EnvironmentKeys.Fonts.Weight] = nameof(IText.Font),
		};

		protected static HashSet<string> PropertiesThatTriggerLayout = new()
		{
			nameof(IText.Font),
			nameof(IText.Text),
			nameof(IView.MinimumHeight),
			nameof(IView.MaximumHeight),
			nameof(IView.MinimumWidth),
			nameof(IView.MaximumWidth),
			nameof(IImageSourcePart.Source),
		};

		IReloadHandler reloadHandler;
		public IReloadHandler ReloadHandler
		{
			get => reloadHandler;
			set
			{
				reloadHandler = value;
			}
		}
		WeakReference parent;

		public string Id { get; } = IDGenerator.Instance.Next;

		public string Tag
		{
			get => GetPropertyFromContext<string>();
			internal set => SetPropertyInContext(value);
		}

		public IReadOnlyList<Gesture> Gestures
		{
			get => GetPropertyFromContext<List<Gesture>>();
			internal set => SetPropertyInContext(value);
		}

		public IList<Behavior> Behaviors
		{
			get => GetPropertyFromContext<List<Behavior>>() ?? (IList<Behavior>)(SetPropertyInContext(new List<Behavior>()));
			internal set => SetPropertyInContext(value);
		}

		public IList<DataTrigger> Triggers
		{
			get => GetPropertyFromContext<List<DataTrigger>>() ?? (IList<DataTrigger>)(SetPropertyInContext(new List<DataTrigger>()));
			internal set => SetPropertyInContext(value);
		}

		internal T GetPropertyFromContext<T>([CallerMemberName] string property = null) => this.GetEnvironment<T>(property, false);
		internal T SetPropertyInContext<T>(T value, [CallerMemberName] string property = null)
		{
			if (this.IsDisposed)
				return value;
			this.SetEnvironment(property, value, false);
			return value;
		}

		internal void SetPropertyInContext(object value, [CallerMemberName] string property = null)
		{
			if (this.IsDisposed)
				return;
			this.SetEnvironment(property, value, false);
		}

		public View Parent
		{
			get => parent?.Target as View;
			set
			{
				var p = parent?.Target as View;
				if (p == value)
					return;
				parent = new WeakReference(value);
				OnParentChange(value);
			}
		}
		internal void UpdateNavigation()
		{
			OnParentChange(Navigation);
		}
		protected virtual void OnParentChange(View parent)
		{
			this.Navigation = parent?.Navigation ?? parent as NavigationView;
		}
		public NavigationView Navigation { get; set; }
		public View()
		{
			lock (ActiveViewsLock)
				ActiveViews.Add(this);
			MauiHotReloadHelper.Register(this);
			SetEnvironmentFields();

		}

		WeakReference __viewThatWasReplaced;
		View viewThatWasReplaced
		{
			get => __viewThatWasReplaced?.Target as View;
			set => __viewThatWasReplaced = new WeakReference(value);
		}
		public string AccessibilityId { get; set; }
		public string AutomationId => this.GetAutomationId() ?? AccessibilityId;
		public bool IsEnabled => this.GetEnvironment<bool?>(nameof(IView.IsEnabled)) ?? true;
		public global::Microsoft.Maui.Visibility Visibility => this.GetEnvironment<global::Microsoft.Maui.Visibility?>(nameof(IView.Visibility)) ?? global::Microsoft.Maui.Visibility.Visible;
		public bool IsVisible => Visibility == global::Microsoft.Maui.Visibility.Visible;
		public bool Hidden => !IsVisible;
		public bool Disabled => !IsEnabled;
		public Semantics Semantics => this.GetEnvironment<Semantics>(nameof(IView.Semantics));
		public bool InputTransparent => this.GetPropertyValue<bool?>() ?? false;
		public Rect Bounds => Frame;
		public Rect WindowBounds => Frame;
		public IViewHandler Handler => ViewHandler as IViewHandler;
		public object PlatformView => Handler?.PlatformView;
		public object NativeView => PlatformView;
		public string NativeType => PlatformView?.GetType().FullName;

		// Lifecycle events
		public event EventHandler Loaded;
		public event EventHandler Unloaded;
		public event EventHandler<HandlerChangingEventArgs> HandlerChanging;
		public event EventHandler HandlerChanged;
		public event EventHandler Appearing;
		public event EventHandler Disappearing;

		IElementHandler viewHandler;
		public IElementHandler ViewHandler
		{
			get => viewHandler;
			set
			{
				SetViewHandler(value);
			}
		}

		bool SetViewHandler(IElementHandler handler)
		{
			if (viewHandler == handler)
				return false;
			InvalidateMeasurement();
			var oldViewHandler = viewHandler;
			OnHandlerChanging(oldViewHandler, handler);
			//viewHandler?.Remove(this);
			viewHandler = handler;
			if (viewHandler?.VirtualView != this)
				viewHandler?.SetVirtualView(this);
			if (replacedView is not null)
				replacedView.ViewHandler = handler;
			if (handler is not null)
				MauiHotReloadHelper.AddActiveView((IHotReloadableView)this);
			else
				MauiHotReloadHelper.UnRegister(this);
			AddAllAnimationsToManager();
			OnHandlerChange();

			if (oldViewHandler is null && viewHandler is not null)
				OnLoaded();
			else if (oldViewHandler is not null && viewHandler is null)
				OnUnloaded();

			return true;

		}

		protected virtual void OnHandlerChanging(IElementHandler oldHandler, IElementHandler newHandler)
		{
			HandlerChanging?.Invoke(this, new HandlerChangingEventArgs(oldHandler, newHandler));
		}

		protected virtual void OnHandlerChange()
		{
			HandlerChanged?.Invoke(this, EventArgs.Empty);
		}

		protected virtual void OnLoaded()
		{
			Loaded?.Invoke(this, EventArgs.Empty);
		}

		protected virtual void OnUnloaded()
		{
			Unloaded?.Invoke(this, EventArgs.Empty);
		}

		internal void UpdateFromOldView(View view)
		{
			// Suppress reactive notifications during internal state transfer.
			// Setting Gestures/ViewHandler goes through SetEnvironment → ReactiveEnvironment →
			// NotifyChanged → MarkViewDirty, which would re-dirty the view we're currently
			// rebuilding, causing infinite Flush recursion.
			Reactive.ReactiveScheduler.SuppressNotifications = true;
			try
			{
				if (view is NavigationView nav)
				{
					((NavigationView)this).SetPerformNavigate(nav);
					((NavigationView)this).SetPerformPop(nav);
					((NavigationView)this).SetPerformContentReset(nav);
				}
				var oldView = view.ViewHandler;
				this.ReloadHandler = view.ReloadHandler;
				this.Gestures = view.Gestures;
				view.ViewHandler = null;
				view.replacedView?.Dispose();
				this.ViewHandler = oldView;
			}
			finally
			{
				Reactive.ReactiveScheduler.SuppressNotifications = false;
			}
		}
		View builtView;
		public View BuiltView => builtView?.BuiltView ?? builtView;
		/// <summary>
		/// This will reload the view, forcing a build/diff.
		/// Bindings are more efficient. But this works with any data models.
		/// </summary>
		public void Reload() => Reload(false);
		internal virtual void Reload(bool isHotReload) => ResetView(isHotReload);
		void ResetView(bool isHotReload = false)
		{
			// We save the old replaced view so we can clean it up after the diff
			var oldReplacedView = replacedView;
			try
			{
				//Built view shows off the view that has the Handler, But we still need to dispose the parent!
				var oldView = BuiltView;
				var oldParentView = builtView;
				builtView = null;
				//Null it out, so it isnt replaced by this.GetRenderView();
				replacedView = null;

				//if (ViewHandler is null)
				//	return;
				//ViewHandler?.Remove(this);
				var view = this.GetRenderView();
				if (oldView is not null)
					view = view.Diff(oldView, isHotReload);
				if (view != oldView)
					oldView?.Dispose();
				if (view != oldParentView)
					oldParentView?.Dispose();
				animations?.ForEach(x => x.Dispose());
				ViewHandler?.SetVirtualView(this);
				ReloadHandler?.Reload();
			}
			finally
			{
				//We are done, clean it up.
				if (oldReplacedView is not null)
				{
					oldReplacedView.ViewHandler = null;
					oldReplacedView.Dispose();
				}
				InvalidateMeasurement();
			}
		}

		Func<View> body;
		public Func<View> Body
		{
			get => body;
			set
			{
				var wasSet = body is not null;
				body = value;
				if (wasSet)
					ResetView();
				//   this.SetBindingValue(State, ref body, value, ResetPropertyString);
			}
		}

		///
		public bool HasContent => Body is not null;
		public View GetView() => GetRenderView();
		View replacedView;
		internal void SetHotReloadReplacement(View replacement, bool transferState = true)
		{
			if (replacement is null || replacement == this)
				return;

			replacement.viewThatWasReplaced = this;
			replacement.ViewHandler = ViewHandler;
			replacement.Navigation = Navigation;
			replacement.Parent = this;
			replacement.ReloadHandler = ReloadHandler;
			replacement.PopulateFromEnvironment();
			if (transferState)
				TransferHotReloadStateTo(replacement);
			if (replacement.BuiltView is not null)
				replacement.Reload(true);

			replacedView = replacement;
		}
		protected virtual View GetRenderView()
		{
			if (replacedView is not null)
				return replacedView.GetRenderView();
			MauiHotReloadHelper.Register(this);
			var replaced = viewThatWasReplaced is null
				? CometHotReloadHelper.CreateReplacement(this) ?? MauiHotReloadHelper.GetReplacedView(this) as View
				: null;
			if (replaced is not null && replaced != this)
			{
				SetHotReloadReplacement(replaced);
				return builtView = replacedView.GetRenderView();
			}
			CheckForBody();
			if (Body is null)
				return this;


			if (BuiltView is null)
			{
				try
				{
					var view = GetRenderViewReactive();
					view.Parent = this;
					if (view is NavigationView navigationView)
						Navigation = navigationView;
					builtView = view.GetRenderView();
					UpdateBuiltViewContext(builtView);
				}
				catch (Exception ex)
				{
					if (Debugger.IsAttached)
					{
						builtView = new VStack { new Text(ex.Message.ToString()).LineBreakMode(LineBreakMode.WordWrap) };
					}
					else throw;
				}
			}

			// We need to make this check if there are global views. If so, return itself so it can be in a container view
			// If HotReload never collapse!
			// If not collapse down to the built view.
			//return HotReloadHelper.IsEnabled || hasGlobalState ? this : BuiltView;
			return BuiltView;
		}

		internal View GetRenderViewReactive()
		{
			using var scope = ReactiveScope.BeginTracking();
			View view;
			try
			{
				if (usedEnvironmentData.Any())
					PopulateFromEnvironment();
				view = Body.Invoke();
			}
			catch
			{
				scope.EndTracking();
				throw;
			}
			var newDependencies = scope.EndTracking();

			if (_bodySubscriber is null)
				_bodySubscriber = new BodyDependencySubscriber(this);

			if (_bodyDependencies is not null)
			{
				foreach (var dependency in _bodyDependencies)
				{
					if (!newDependencies.Contains(dependency))
						dependency.Unsubscribe(_bodySubscriber);
				}
			}

			foreach (var dependency in newDependencies)
			{
				if (_bodyDependencies is null || !_bodyDependencies.Contains(dependency))
					dependency.Subscribe(_bodySubscriber);
			}

			_bodyDependencies = newDependencies;
			return view;
		}

		sealed class BodyDependencySubscriber : IReactiveSubscriber
		{
			readonly View _view;

			public BodyDependencySubscriber(View view)
			{
				_view = view;
			}

			public void OnDependencyChanged(IReactiveSource source)
			{
				if (_view.IsDisposed)
					return;
				ReactiveScheduler.MarkViewDirty(_view);
			}
		}

		bool didCheckForBody;
		void CheckForBody()
		{
			if (didCheckForBody)
				return;
			didCheckForBody = true;
			if (Body is not null)
				return;
			var bodyMethod = this.GetBody();
			if (bodyMethod is not null)
				Body = bodyMethod;
		}

		protected const string ResetPropertyString = "ResetPropertyString";

		internal void BindingPropertyChanged(INotifyPropertyRead bindingObject, string property, string fullProperty, object value)
		{
			BindingPropertyChanged<object>(bindingObject, property, fullProperty, value);
		}

		internal void BindingPropertyChanged<T>(INotifyPropertyRead bindingObject, string property, string fullProperty, T value)
		{
			try
			{
				Reactive.ReactiveScheduler.MarkViewDirty(this);
			}
			catch (Exception ex)
			{
				Logger.Error(ex);
			}
		}

		private bool _isBatching;
		private readonly List<(string property, object value)> _batchedChanges = new List<(string, object)>();
		private HashSet<string> _propertiesBeingUpdated;

		/// <summary>
		/// Begins a batch update. Property changes will be queued until BatchCommit() is called,
		/// preventing multiple re-renders during bulk updates.
		/// </summary>
		public void BatchBegin()
		{
			_isBatching = true;
		}

		/// <summary>
		/// Commits all batched property changes and triggers a single re-render.
		/// </summary>
		public void BatchCommit()
		{
			_isBatching = false;
			if (_batchedChanges.Count == 0)
				return;

			var changes = _batchedChanges.ToList();
			_batchedChanges.Clear();

			foreach (var (property, value) in changes)
			{
				try
				{
					this.SetPropertyValue(property, value);
				}
				catch (Exception ex)
				{
					Debug.WriteLine($"Error setting batched property:{property} : {value} on :{this}");
					Debug.WriteLine(ex);
				}
			}

			// Single handler update and layout invalidation
			foreach (var (property, _) in changes)
			{
				var newPropName = GetHandlerPropertyName(property);
				ViewHandler?.UpdateValue(newPropName);
			}

			InvalidateMeasurement();
		}

		public virtual void ViewPropertyChanged(string property, object value)
		{
			if (property == ResetPropertyString)
			{
				ResetView();
				return;
			}

			if (_isBatching)
			{
				_batchedChanges.Add((property, value));
				return;
			}

			// Re-entrancy guard: prevent infinite recursion when SetPropertyValue
			// triggers SetPropertyInContext → SetEnvironment → ContextPropertyChanged → ViewPropertyChanged
			_propertiesBeingUpdated ??= new HashSet<string>();
			if (!_propertiesBeingUpdated.Add(property))
				return;

			try
			{
				this.SetPropertyValue(property, value);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Error setting property:{property} : {value} on :{this}");
				Debug.WriteLine(ex);
			}
			finally
			{
				_propertiesBeingUpdated.Remove(property);
			}
			var newPropName = GetHandlerPropertyName(property);
			ViewHandler?.UpdateValue(newPropName);
			builtView?.ViewPropertyChanged(property, value);
			if (measurementValid && PropertyChangeShouldTriggerLayout(newPropName))
			{
				this.InvalidateMeasurement();
			}
		}

		protected virtual string GetHandlerPropertyName(string property) =>
			HandlerPropertyMapper.TryGetValue(property, out var value) ? value : property;

		protected virtual bool PropertyChangeShouldTriggerLayout(string property) =>
			PropertiesThatTriggerLayout.Contains(property);

		/// <summary>
		/// Attaches a <see cref="PropertySubscription{T}"/> to this view, binding it
		/// to the specified property name. The subscription is kept alive and disposed
		/// when the view is disposed. Used by Signal extension methods and generated controls.
		/// </summary>
		internal void AttachPropertySubscription<T>(PropertySubscription<T> subscription, string propertyName)
		{
			if (subscription is null)
				return;

			subscription.BindToView(this, propertyName);
			_propertySubscriptions ??= new List<IDisposable>();
			_propertySubscriptions.Add(subscription);
		}

		internal override void ContextPropertyChanged(string property, object value, bool cascades)
		{
			builtView?.ContextPropertyChanged(property, value, cascades);
			ViewPropertyChanged(property, value);
		}

		public static void SetGlobalEnvironment(string key, object value)
		{
			Environment.SetValue(key, value, true);
			ThreadHelper.RunOnMainThread(() => {
				List<View> views;
				lock (ActiveViewsLock)
					views = ActiveViews.OfType<View>().ToList();
				views.ForEach(x => x.ViewPropertyChanged(key, value));
			});

		}
		public static void SetGlobalEnvironment(string styleId, string key, object value)
		{
			//If there is no style, set the default key
			var typedKey = string.IsNullOrWhiteSpace(styleId) ? key : $"{styleId}.{key}";
			Environment.SetValue(typedKey, value, true);
			ThreadHelper.RunOnMainThread(() => {
				List<View> views;
				lock (ActiveViewsLock)
					views = ActiveViews.OfType<View>().ToList();
				views.ForEach(x => x.ViewPropertyChanged(typedKey, value));
			});
		}

		public static void SetGlobalEnvironment(Type type, string key, object value)
		{
			var typedKey = ContextualObject.GetTypedKey(type, key);
			Environment.SetValue(typedKey, value, true);
			ThreadHelper.RunOnMainThread(() => {
				List<View> views;
				lock (ActiveViewsLock)
					views = ActiveViews.OfType<View>().ToList();
				views.ForEach(x => x.ViewPropertyChanged(typedKey, value));
			});
		}

		public static void SetGlobalEnvironment(IDictionary<string, object> data)
		{
			foreach (var pair in data)
				Environment.SetValue(pair.Key, pair.Value, true);
		}
		public static T GetGlobalEnvironment<T>(string key) => Environment.GetValue<T>(key);

		void SetEnvironmentFields()
		{
			var fields = this.GetFieldsWithAttribute(typeof(EnvironmentAttribute));
			if (!fields.Any())
				return;
			foreach (var f in fields)
			{
				var attribute = f.GetCustomAttributes(true).OfType<EnvironmentAttribute>().FirstOrDefault();
				var key = attribute.Key ?? f.Name;
				usedEnvironmentData.Add((f.Name, key));
			}
		}
		void PopulateFromEnvironment()
		{
			var keys = usedEnvironmentData.ToList();
			foreach (var item in keys)
			{
				var key = item.Key;
				var value = this.GetEnvironment(key);
				if (value is null)
				{
					//Get the current MauiContext
					//I might be able to do something better, like searching up though the parent
					//Maybe I can do something where I get the current Context whenever I build
					//In test project, we don't assign the CurrentWindows to have the MauiContext
					var mauiContext = GetMauiContext();
					if (mauiContext is not null)
					{
						var type = this.GetType();
						var prop = type.GetDeepField(item.Field);
						var service = mauiContext.Services.GetService(prop.FieldType);
						if (service is not null)
							value = service;
					}
				}
				if (value is null)
				{
					//Check the replaced view
					if (viewThatWasReplaced is not null)
					{
						value = viewThatWasReplaced.GetEnvironment(key);
					}
					if (value is null)
					{
						//Lets try again with first letter uppercased;
						var newKey = key.FirstCharToUpper();
						value = this.GetEnvironment(newKey);
						if (value is not null)
						{
							key = newKey;
							usedEnvironmentData.Remove(item);
							usedEnvironmentData.Add((item.Field, newKey));
						}
					}
				}
				if (value is null && viewThatWasReplaced is not null)
				{
					value = viewThatWasReplaced.GetEnvironment(item.Key);
				}
				if (value is not null)
					this.SetDeepPropertyValue(item.Field, value);
			}
		}
		public bool IsDisposed => disposedValue;
		bool disposedValue = false;
		protected virtual void Dispose(bool disposing)
		{
			if (!disposing)
				return;

			lock (ActiveViewsLock)
				ActiveViews.Remove(this);



			if (_bodyDependencies is not null && _bodySubscriber is not null)
			{
				foreach (var dependency in _bodyDependencies)
					dependency.Unsubscribe(_bodySubscriber);
				_bodyDependencies = null;
				_bodySubscriber = null;
			}

			if (_propertySubscriptions is not null)
			{
				foreach (var sub in _propertySubscriptions)
					sub.Dispose();
				_propertySubscriptions = null;
			}

			var gestures = Gestures;
			if (gestures?.Any() ?? false)
			{
				foreach (var g in gestures)
					ViewHandler?.Invoke(Gesture.RemoveGestureProperty, g);
			}

			MauiHotReloadHelper.UnRegister(this);

			try
			{
				var vh = ViewHandler;
				ViewHandler = null;
				(vh as IDisposable)?.Dispose();
				replacedView?.Dispose();
				replacedView = null;
				builtView?.Dispose();
				builtView = null;
				body = null;
				Context(false)?.Clear();
			}
			finally
			{
			}
		}
		void OnDispose(bool disposing)
		{
			if (disposedValue)
				return;
			disposedValue = true;
			Dispose(disposing);
		}

		public void Dispose()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			OnDispose(true);
		}

		public virtual Rect Frame
		{
			get => this.GetEnvironment<Rect?>(nameof(Frame), false) ?? Rect.Zero;
			set
			{
				var f = Frame;
				if (f == value)
					return;
				this.SetEnvironment(nameof(Frame), value, false);
				(ViewHandler as IViewHandler)?.PlatformArrange(value);
			}
		}

	
		private bool measurementValid;
		public bool MeasurementValid
		{
			get => measurementValid;
			set
			{
				measurementValid = value;
				if (BuiltView is not null)
					BuiltView.MeasurementValid = value;
			}
		}

		public void InvalidateMeasurement()
		{
			lastAvailableSize = Size.Zero;
			MeasurementValid = false;
			(Parent as IView)?.InvalidateMeasure();
		}

		private Size _measuredSize;
		public Size MeasuredSize
		{
			get => _measuredSize;
			set
			{
				_measuredSize = value;
				if (BuiltView is not null)
					BuiltView.MeasuredSize = value;
			}
		}
		public virtual Size GetDesiredSize(Size availableSize)
		{
			if (BuiltView is not null)
				return BuiltView.GetDesiredSize(availableSize);
			if (!MeasurementValid || lastAvailableSize != availableSize)
			{
				var frameConstraints = this.GetFrameConstraints();
				var margins = this.GetMargin();

				if (frameConstraints?.Height > 0 && frameConstraints?.Width > 0)
					return new Size(frameConstraints.Width.Value, frameConstraints.Height.Value);
				var ms = this.ComputeDesiredSize(availableSize.Width, availableSize.Height);
				if (frameConstraints?.Width > 0)
					ms.Width = frameConstraints.Width.Value;
				if (frameConstraints?.Height > 0)
					ms.Height = frameConstraints.Height.Value;

				ms.Width += margins.HorizontalThickness;
				ms.Height += margins.VerticalThickness;
				MeasuredSize = ms;
			}
			MeasurementValid = this.ViewHandler is not null;
			return MeasuredSize;
		}


		Size lastAvailableSize;
		public Size Measure(double widthConstraint, double heightConstraint)
		{

			if (BuiltView is not null)
				return MeasuredSize = BuiltView.Measure(widthConstraint, heightConstraint);

			var availableSize = new Size(widthConstraint, heightConstraint);
			if (!MeasurementValid || availableSize != lastAvailableSize)
			{
				MeasuredSize = GetDesiredSize(new Size(widthConstraint, heightConstraint));
				if (ViewHandler is not null)
					lastAvailableSize = availableSize;
			}

			MeasurementValid = ViewHandler is not null;
			return MeasuredSize;
		}



		public virtual void LayoutSubviews(Rect frame)
		{
			this.SetFrameFromPlatformView(frame);
			if (BuiltView is not null)
				BuiltView.LayoutSubviews(frame);
			else if (this is ContainerView container)
			{
				foreach (var view in container)
				{
					view.LayoutSubviews(this.Frame);
				}
			}
		}
		public override string ToString() => $"{this.GetType()} - {this.Id}";

		View notificationView => replacedView ?? BuiltView;

		public virtual void ViewDidAppear()
		{
			notificationView?.ViewDidAppear();
			Appearing?.Invoke(this, EventArgs.Empty);
			ResumeAnimations();
		}
		public virtual void ViewDidDisappear()
		{
			Disappearing?.Invoke(this, EventArgs.Empty);
			notificationView?.ViewDidDisappear();
			PauseAnimations();
		}

		List<Animation> animations;
		List<Animation> GetAnimations(bool create) => !create ? animations : animations ?? (animations = new List<Animation>());
		public List<Animation> Animations => animations;

		public void AddAnimation(Animation animation)
		{
			animation.Parent = new WeakReference<IAnimator>(this);
			GetAnimations(true).Add(animation);
			AddAnimationsToManager(animation);
		}
		public void RemoveAnimation(Animation animation)
		{
			animation.Parent = null;
			GetAnimations(false)?.Remove(animation);
		}

		public void RemoveAnimation(string id)
		{
			var animation = GetAnimations(false)?.FirstOrDefault(a => a is ContextualAnimation ca && ca.Id == id);
			if (animation is not null)
				RemoveAnimation(animation);
		}

		public void RemoveAnimations() => GetAnimations(false)?.ToList().ForEach(animation => {
			animations.Remove(animation);
			RemoveAnimationsFromManager(animation);
		});
		void AddAnimationsToManager(Animation animation)
		{
			var animationManager = GetAnimationManager();
			if (animationManager is null)
				return;
			ThreadHelper.RunOnMainThread(() => animationManager.Add(animation));
		}

		protected virtual IMauiContext GetMauiContext() => ViewHandler?.MauiContext ?? BuiltView?.GetMauiContext();
		IAnimationManager GetAnimationManager() => GetMauiContext()?.Services.GetRequiredService<IAnimationManager>();

		void AddAllAnimationsToManager()
		{
			var animationManager = GetAnimationManager();
			if (animationManager is null)
				return;
			ThreadHelper.RunOnMainThread(() => GetAnimations(false)?.ToList().ForEach(animationManager.Add));
		}
		void RemoveAnimationsFromManager(Animation animation)
		{
			var animationManager = GetAnimationManager();
			if (animationManager is null)
				return;
			animationManager.Remove(animation);
		}

		public virtual void PauseAnimations()
		{
			GetAnimations(false)?.ForEach(x => x.Pause());
			notificationView?.PauseAnimations();
		}
		public virtual void ResumeAnimations()
		{
			GetAnimations(false)?.ForEach(x => x.Resume());
			notificationView?.ResumeAnimations();
		}

		bool IView.IsEnabled => IsEnabled;

		Rect IView.Frame
		{
			get => Frame;
			set => Frame = value;
		}

		IViewHandler IView.Handler
		{
			get => (ViewHandler as IViewHandler);
			set => SetViewHandler(value);
		}

		IElementHandler IElement.Handler
		{
			get => this.ViewHandler;
			set => SetViewHandler(value);
		}

		IElement IElement.Parent => this.Parent;

		Size IView.DesiredSize => MeasuredSize;


		double IView.Width => this.GetFrameConstraints()?.Width ?? Dimension.Unset;
		double IView.Height => this.GetFrameConstraints()?.Height ?? Dimension.Unset;

		double IView.MinimumHeight => this.GetEnvironment<double?>(nameof(IView.MinimumHeight)) ?? Dimension.Minimum;
		double IView.MaximumWidth => this.GetEnvironment<double?>(nameof(IView.MaximumWidth)) ?? Dimension.Maximum;
		double IView.MinimumWidth => this.GetEnvironment<double?>(nameof(IView.MinimumWidth)) ?? Dimension.Minimum;
		double IView.MaximumHeight => this.GetEnvironment<double?>(nameof(IView.MaximumHeight)) ?? Dimension.Maximum;

		public IView ReplacedView => this.GetView();// HasContent ? this : BuiltView ?? this;


		public bool RequiresContainer => HasContent;

		IShape IView.Clip => this.GetClipShape();

		IView IReplaceableView.ReplacedView => this.ReplacedView;

		Thickness IView.Margin => this.GetMargin();

		string IView.AutomationId => AutomationId;

		//TODO: lets update these to be actual property
		FlowDirection IView.FlowDirection => this.GetEnvironment<FlowDirection>(nameof(IView.FlowDirection));

		LayoutAlignment IView.HorizontalLayoutAlignment => this.GetHorizontalLayoutAlignment(this.Parent as ContainerView);

		LayoutAlignment IView.VerticalLayoutAlignment => this.GetVerticalLayoutAlignment(this.Parent as ContainerView);

		Semantics IView.Semantics => Semantics;

		bool ISafeAreaView.IgnoreSafeArea => this.GetIgnoreSafeArea(false);

		Visibility IView.Visibility => Visibility;

		double IView.Opacity => this.GetOpacity();

		Paint IView.Background => this.GetBackground();

		double ITransform.TranslationX => this.GetEnvironment<double>(nameof(ITransform.TranslationX));

		double ITransform.TranslationY => this.GetEnvironment<double>(nameof(ITransform.TranslationY));

		double ITransform.Scale => this.GetEnvironment<double?>(nameof(ITransform.Scale)) ?? 1;

		double ITransform.ScaleX => this.GetEnvironment<double?>(nameof(ITransform.ScaleX)) ?? 1;

		double ITransform.ScaleY => this.GetEnvironment<double?>(nameof(ITransform.ScaleY)) ?? 1;

		double ITransform.Rotation => this.GetEnvironment<double>(nameof(ITransform.Rotation));

		double ITransform.RotationX => this.GetEnvironment<double>(nameof(ITransform.RotationX));

		double ITransform.RotationY => this.GetEnvironment<double>(nameof(ITransform.RotationY));

		double ITransform.AnchorX => this.GetEnvironment<double?>(nameof(ITransform.AnchorX)) ?? .5;

		double ITransform.AnchorY => this.GetEnvironment<double?>(nameof(ITransform.AnchorY)) ?? .5;


		public string Title => this.GetTitle();

		IShadow IView.Shadow => this.GetEnvironment<Graphics.Shadow>(EnvironmentKeys.View.Shadow);

		int IView.ZIndex => this.GetEnvironment<int?>(nameof(IView.ZIndex)) ?? 0;

		Size IView.Arrange(Rect bounds)
		{
			LayoutSubviews(bounds);
			return Frame.Size;
		}
		Size IView.Measure(double widthConstraint, double heightConstraint)
			=>
			//Measure(new Size(widthConstraint, heightConstraint));
			Measure(widthConstraint, heightConstraint);
		void IView.InvalidateMeasure() => InvalidateMeasurement();
		void IView.InvalidateArrange() { }
		internal void TransferHotReloadStateTo(View newView)
		{
			if (newView is null)
				return;
			TransferHotReloadStateToCore(newView);
		}
		[UnconditionalSuppressMessage("Trimming", "IL2070",
			Justification = "Hot reload is debug-only; trimming/AOT are disabled in debug builds")]
		protected virtual void TransferHotReloadStateToCore(View newView)
		{
			if (_context is not null)
			{
				var target = newView.Context(true);
				foreach (var pair in _context.dictionary)
					target.dictionary[pair.Key] = pair.Value;
			}

			if (_localContext is not null)
			{
				var target = newView.LocalContext(true);
				foreach (var pair in _localContext.dictionary)
					target.dictionary[pair.Key] = pair.Value;
			}

			var oldType = this.GetType();
			var newType = newView.GetType();

			static bool IsSignalType(Type type)
			{
				while (type is not null && type != typeof(object))
				{
					if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Signal<>))
						return true;
					type = type.BaseType;
				}

				return false;
			}

			var fields = oldType.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
				.Where(field => IsSignalType(field.FieldType));

			foreach (var field in fields)
			{
				var newField = newType.GetField(field.Name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
				if (newField is not null && newField.FieldType == field.FieldType)
				{
					var signalRef = field.GetValue(this);
					if (signalRef is not null)
						newField.SetValue(newView, signalRef);
				}
			}
		}
		void IHotReloadableView.TransferState(IView newView) => TransferHotReloadStateTo(newView as View);
		void IHotReloadableView.Reload() => ThreadHelper.RunOnMainThread(() => Reload(true));
		protected int? TypeHashCode;
		public virtual int GetContentTypeHashCode() => this.replacedView?.GetContentTypeHashCode() ?? (TypeHashCode ??= this.GetType().GetHashCode());

		protected T GetPropertyValue<T>(bool cascades = true, [CallerMemberName] string key = "") => this.GetEnvironment<T>(key, cascades);
		bool IView.Focus() => true;
		void IView.Unfocus() { }

		IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren()
		{
			if (BuiltView is IVisualTreeElement builtView && builtView != this)
				return new[] { builtView };

			if (this is not IContainerView container)
				return Array.Empty<IVisualTreeElement>();

			var children = container.GetChildren();
			if (children is null || children.Count == 0)
				return Array.Empty<IVisualTreeElement>();

			var visualChildren = new List<IVisualTreeElement>(children.Count);
			foreach (var child in children)
			{
				if (child is IVisualTreeElement visualChild)
					visualChildren.Add(visualChild);
			}

			return visualChildren.Count == 0 ? Array.Empty<IVisualTreeElement>() : visualChildren;
		}
		IVisualTreeElement IVisualTreeElement.GetVisualParent() => this.Parent;

		internal IBorderStroke Border
		{
			get
			{
				var border = this.GetBorder();
				if (border is not null)
					border.view = this;
				return border;
			}
		}

		bool IView.IsFocused { get; set; }

		bool IView.InputTransparent => InputTransparent;

		Thickness IPadding.Padding => this.GetPadding();
	}
}
