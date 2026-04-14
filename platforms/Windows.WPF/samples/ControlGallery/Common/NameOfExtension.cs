using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ControlGallery.Common
{
    public class NameOfExtension : IMarkupExtension<string>
    {
        public Type Type { get; set; }
        public string ProvideValue(IServiceProvider serviceProvider)
        {
            return nameof(Type);
        }

        object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider)
        {
            return (this as IMarkupExtension<string>).ProvideValue(serviceProvider);
        }
    }
}
