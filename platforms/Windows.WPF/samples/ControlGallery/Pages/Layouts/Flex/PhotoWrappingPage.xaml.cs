using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

using Microsoft.Maui.Controls;
using System.Diagnostics;

namespace ControlGallery.Pages.Layouts
{
    public partial class PhotoWrappingPage : ContentPage
    {
        // Class for deserializing JSON list of sample bitmaps
        [DataContract]
        class ImageList
        {
            [DataMember(Name = "photos")]
            public List<string> Photos = null;
        }

        public PhotoWrappingPage ()
        {
            InitializeComponent ();

            LoadBitmapCollection();
        }

        async void LoadBitmapCollection()
        {
            using (HttpClient webClient = new HttpClient())
            {
                try
                {
                    // Download the list of stock photos
                    Uri uri = new Uri("https://raw.githubusercontent.com/xamarin/docs-archive/master/Images/stock/small/stock.json");
                    byte[] data = await webClient.GetByteArrayAsync(uri);

                    // Convert to a Stream object
                    using (Stream stream = new MemoryStream(data))
                    {
                        // Deserialize the JSON into an ImageList object
                        var jsonSerializer = new DataContractJsonSerializer(typeof(ImageList));
                        ImageList imageList = (ImageList)jsonSerializer.ReadObject(stream);

                        // Create an Image object for each bitmap
                        foreach (string filepath in imageList.Photos)
                        {
                            Debug.WriteLine($"Image: {filepath}");
                            if (!string.IsNullOrEmpty(filepath))
                            {
                                Image image = new Image
                                {
                                    Source = ImageSource.FromUri(new Uri(filepath)),
                                    Aspect = Aspect.AspectFill,
                                    MaximumHeightRequest = 100,
                                    MinimumWidthRequest = 25,
                                    MinimumHeightRequest = 25,
                                    BackgroundColor = Colors.Red
                                };
                                FlexLayout.SetBasis(image, new Microsoft.Maui.Layouts.FlexBasis(0.2f));
                                flexLayout.Add(image);
                            }
                            
                        }
                    }
                }
                catch
                {
                    flexLayout.Add(new Label
                    {
                        Text = "Cannot access list of bitmap files"
                    });
                }
            }

            activityIndicator.IsRunning = false;
            activityIndicator.IsVisible = false;
        }
    }
}
