using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using ColorCode;
using ControlGallery.Common;
using Markdig;
using Markdown.ColorCode;
using Microsoft.Maui.Controls;
using Portable.Xaml;

namespace ControlGallery.Converters;

public class ColorSyntaxConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var codestring = (string)value;
        ILanguage language = (ILanguage)parameter;// == "csharp" ? Languages.CSharp : Languages.Xml;

        if (string.IsNullOrEmpty(codestring))
            return string.Empty;

        //var pipeline = new MarkdownPipelineBuilder()
        //    .UseAdvancedExtensions()
        //    .UseColorCode()
        //    //.LoadFromXaml(codestring)
        //    .Build();

        //var colorizedHtml = Markdig.Markdown.ToHtml(codestring, pipeline);

        //Color backgroundColor = Colors.Black;
        //Color textColor = Colors.White;
        //Color descriptionColor = Colors.White;
        ////if (App.Current.RequestedTheme == AppTheme.Dark)
        ////{
        ////    backgroundColor = (Color)App.Current.Resources["Black"];
        ////    textColor = (Color)App.Current.Resources["White"];
        ////    descriptionColor = (Color)App.Current.Resources["White"];
        ////}

        //string html = $"<!DOCTYPE html><head><meta name='viewport' content='initial-scale=1.0' /><style>body {{ color: {textColor.ToHex()}; background-color: {backgroundColor.ToHex()}; font-family: 'HelveticaNeue-Light', Helvetica, Arial, sans-serif; font-size: 1.4em; }} </style><title>code</title></head><body>";

        //Debug.WriteLine(html + colorizedHtml + "</body></html>");


        //return html + colorizedHtml + "</body></html>";

        // Microsoft.Maui.Controls.Internals.Profile.FrameBegin();
        // Microsoft.Maui.Controls.Internals.Profile.FramePartition("Converter Start");

        var formatter = new FormattedStringFormatter();
        var fs = new FormattedString();
        formatter.FormatString(codestring, language, fs);

        Debug.WriteLine(XamlServices.Save(fs));

        //foreach(var s in fs.Spans)
        //{
        //    Debug.WriteLine(s.ToString());
        //}        

        // Microsoft.Maui.Controls.Internals.Profile.FrameEnd();
        return fs;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return !(bool)value;
    }
}
