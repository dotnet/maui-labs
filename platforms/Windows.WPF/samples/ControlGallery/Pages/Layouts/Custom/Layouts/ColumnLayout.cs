using Microsoft.Maui.Layouts;
using System.Collections;

namespace CustomLayouts
{
    public class ColumnLayout : VerticalStackLayout
    {
        // We'll use an attached property so we don't have to worry about tracking which items are
        // in "Fill" mode locally.
        public static readonly BindableProperty FillProperty = BindableProperty.Create("Fill", typeof(bool),
            typeof(ColumnLayout), false);

        public ColumnLayout()
        {
        }

        protected override ILayoutManager CreateLayoutManager()
        {
            return new ColumnLayoutManager(this);
        }

        // Support methods for the attached property
        public bool GetFill(BindableObject bindableObject)
        {
            return (bool)bindableObject.GetValue(FillProperty);
        }
        
        public void SetFill(BindableObject bindableObject, bool fill)
        {
            bindableObject.SetValue(FillProperty, fill);
        }

        // Convenience method for use from the layout manager
        public bool GetFill(IView view)
        {
            if (view is BindableObject bindableObject)
            {
                return GetFill(bindableObject);
            }

            return false;
        }
    }
}
