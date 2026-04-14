using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace ControlGallery.Models
{
    public class ColorBrush
    {
        public string BrushName {get;set;}
        public SolidColorBrush Brush {get;set;}
        public string BrushValue {

            get {
                return Brush.Color.ToHex();
            }
        }
        public ColorBrush()
        {
            
        }
    }
}