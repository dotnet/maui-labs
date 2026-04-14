using Microsoft.Maui.Controls;
using Microsoft.Maui.Layouts;

namespace CustomLayouts
{
    public class CascadeLayout : StackLayout
    {
        public CascadeLayout()
        {
        }

        protected override ILayoutManager CreateLayoutManager()
        {
            return new CascadeLayoutManager(this);
        }
    }
}
