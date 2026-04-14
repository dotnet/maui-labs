using System.Collections.ObjectModel;
using System.Windows.Input;
using ControlGallery.Models;
using Microsoft.Maui.Controls;

namespace ControlGallery.Pages.Features.Animation
{
    public class AnimationsPageViewModel
    {
        public ObservableCollection<GroupedNavItems> Groups { get; } = new();

        public ICommand NavigateCommand { get; }

        public AnimationsPageViewModel()
        {
            NavigateCommand = new Command<NavItem>(
                async (NavItem item) =>
                {
                    if (item == null)
                        return;

                    var route = !string.IsNullOrEmpty(item.Route) ? item.Route : item.Destination?.Name;
                    if (string.IsNullOrEmpty(route))
                        return;

                    if (!route.StartsWith("/"))
                        await Shell.Current.GoToAsync(route);
                    else
                        await Shell.Current.GoToAsync(route);
                });

            PopulateGroups();
        }

        void PopulateGroups()
        {
            var g = new GroupedNavItems("Animations")
            {
                new NavItem { Title = "Custom", Description = "ColorTo", Route = "CustomAnimationPage" },
                new NavItem { Title = "Easings", Description = "Rate of change over time", Route = "EasingsPage" },
                new NavItem { Title = "Fade", Description = "FadeTo", Route = "FadePage" },
                new NavItem { Title = "Rotate", Description = "RotateTo", Route = "RotatePage" },
                new NavItem { Title = "Scale", Description = "ScaleTo", Route = "ScalePage" },
                new NavItem { Title = "Translate", Description = "TranslateTo", Route = "TranslatePage" }
            };

            Groups.Add(g);
        }
    }
}
