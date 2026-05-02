using System;

namespace Comet
{
	/// <summary>
	/// Defines a reusable visual template for custom controls.
	/// Matches MAUI's ControlTemplate concept adapted for MVU.
	/// 
	/// Usage:
	///   var template = new ControlTemplate(presenter => new VStack {
	///       new Text("Header"),
	///       presenter,  // ContentPresenter - where child content goes
	///       new Text("Footer"),
	///   });
	///   
	///   new TemplatedView(template) { new Text("My Content") }
	/// </summary>
	public class ControlTemplate
	{
		readonly Func<ContentPresenter, View> _templateFactory;

		public ControlTemplate(Func<ContentPresenter, View> templateFactory)
		{
			_templateFactory = templateFactory ?? throw new ArgumentNullException(nameof(templateFactory));
		}

		/// <summary>
		/// Creates the template view, injecting content through the ContentPresenter.
		/// </summary>
		public View CreateContent(View content)
		{
			var presenter = new ContentPresenter { Content = content };
			return _templateFactory(presenter);
		}
	}

	/// <summary>
	/// Placeholder within a ControlTemplate that is replaced by the templated view's content.
	/// Equivalent to MAUI's ContentPresenter.
	/// </summary>
	public class ContentPresenter : ContentView
	{
		public ContentPresenter() { }
	}

	/// <summary>
	/// A view that applies a ControlTemplate to its content.
	/// The template wraps the content, with ContentPresenter marking where content appears.
	/// </summary>
	public class TemplatedView : ContentView
	{
		ControlTemplate _controlTemplate;
		View _innerContent;

		public ControlTemplate ControlTemplate
		{
			get => _controlTemplate;
			set
			{
				_controlTemplate = value;
				ApplyTemplate();
			}
		}

		public TemplatedView() { }

		public TemplatedView(ControlTemplate template)
		{
			_controlTemplate = template;
		}

		public override void Add(View view)
		{
			_innerContent = view;
			ApplyTemplate();
		}

		void ApplyTemplate()
		{
			if (_controlTemplate is not null && _innerContent is not null)
			{
				base.Add(_controlTemplate.CreateContent(_innerContent));
			}
			else if (_innerContent is not null)
			{
				base.Add(_innerContent);
			}
		}
	}

	/// <summary>
	/// Extension methods for applying ControlTemplates to views.
	/// </summary>
	public static class ControlTemplateExtensions
	{
		/// <summary>
		/// Applies a ControlTemplate to a view, wrapping its body in the template.
		/// </summary>
		public static T WithTemplate<T>(this T view, ControlTemplate template) where T : View
		{
			if (view is TemplatedView tv)
			{
				tv.ControlTemplate = template;
			}
			return view;
		}
	}
}
