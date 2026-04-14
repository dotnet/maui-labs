using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using ControlGallery.Converters;
using Microsoft.Maui.Graphics;
using ControlGallery.Common;

namespace ControlGallery.Pages.Layouts
{	public class SliderGridPage : ContentPage
	{
        BoxView boxView;
        Slider redSlider;
        Slider greenSlider;
        Slider blueSlider;
        Label hexLabel;

		public SliderGridPage()
		{
            //BackgroundColor = Colors.White;
            Title = "Slider";

			Style labelStyle = new Style(typeof(Label))
            {
                Setters =
                {
                    new Setter { Property = Label.HorizontalTextAlignmentProperty, Value = TextAlignment.Center }
                }
            };
            Resources.Add(labelStyle);

            // Root page layout
            Grid rootGrid = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(),
                    new RowDefinition()
                }
            };

            boxView = new BoxView { Color = Colors.Black };
            rootGrid.Children.Add(boxView);

            hexLabel = new Label()
            {
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalOptions = LayoutOptions.Center,
                TextColor = Colors.Black,
                FontSize = 64
            };
            rootGrid.Children.Add(hexLabel);

            // Child page layout
            Grid childGrid = new Grid
            {
                Margin = new Thickness(20),
                RowDefinitions =
                {
                    new RowDefinition(),
                    new RowDefinition(),
                    new RowDefinition(),
                    new RowDefinition(),
                    new RowDefinition(),
                    new RowDefinition()
                }
            };

            DoubleToIntConverter doubleToInt = new DoubleToIntConverter();

            redSlider = new Slider();
            redSlider.ValueChanged += OnSliderValueChanged;
            childGrid.Children.Add(redSlider);

            Label redLabel = new Label(){
                HorizontalTextAlignment = TextAlignment.Center
            };
            redLabel.SetBinding(Label.TextProperty, new Binding("Value", converter: doubleToInt, converterParameter: "255", stringFormat: "Red = {0}", source: redSlider));
            Grid.SetRow(redLabel, 1);
            childGrid.Children.Add(redLabel);

            greenSlider = new Slider();
            greenSlider.ValueChanged += OnSliderValueChanged;
            Grid.SetRow(greenSlider, 2);
            childGrid.Children.Add(greenSlider);

            Label greenLabel = new Label(){
                HorizontalTextAlignment = TextAlignment.Center
            };
            greenLabel.SetBinding(Label.TextProperty, new Binding("Value", converter: doubleToInt, converterParameter: "255", stringFormat: "Green = {0}", source: greenSlider));
            Grid.SetRow(greenLabel, 3);
            childGrid.Children.Add(greenLabel);

            blueSlider = new Slider();
            blueSlider.ValueChanged += OnSliderValueChanged;
            Grid.SetRow(blueSlider, 4);
            childGrid.Children.Add(blueSlider);

            Label blueLabel = new Label(){
                HorizontalTextAlignment = TextAlignment.Center
            };
            blueLabel.SetBinding(Label.TextProperty, new Binding("Value", converter: doubleToInt, converterParameter: "255", stringFormat: "Blue = {0}", source: blueSlider));
            Grid.SetRow(blueLabel, 5);
            childGrid.Children.Add(blueLabel);

            // Place the child Grid in the root Grid
            rootGrid.Add(childGrid, column: 0, row: 1);


			this.Content = rootGrid;
		}

        void OnSliderValueChanged(object sender, ValueChangedEventArgs e)
        {
            boxView.Color = new Color((float)redSlider.Value, (float)greenSlider.Value, (float)blueSlider.Value);
            hexLabel.Text = boxView.Color.ToHex();

            // if color is dark, then flip color of text?
        }
	}
}
