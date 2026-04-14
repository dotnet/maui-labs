using Microsoft.Maui.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ControlGallery.Models
{
    public class QuickLink
    {
        public string FeaturedImage { get; set; }
        public string Hyperlink { get; set; }
        public DateTime PublishedDate { get; set; }
        public string Title { get; set; }

        public Command NavigateTo { private set; get; }

        public QuickLink()
        {
            NavigateTo = new Command(
                async () =>
                {
                    try{
                        await Browser.OpenAsync(Hyperlink);
                    }catch(Exception ex){
                        Debug.WriteLine($"Crap: {ex.Message}");
                    }
                });
        }
    }
}
