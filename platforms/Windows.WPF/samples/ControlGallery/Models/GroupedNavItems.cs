using System.Collections.ObjectModel;

namespace ControlGallery.Models
{
    public class GroupedNavItems : ObservableCollection<NavItem>
    {
        public string Title { get; }

        public GroupedNavItems(string title)
        {
            Title = title;
        }
    }
}
