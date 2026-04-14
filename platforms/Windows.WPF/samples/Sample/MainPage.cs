using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Sample;

public class MainPage : ContentPage
{
    int _buttonClickCount;
    readonly Label _statusLabel;
    readonly Label _sliderValue;
    readonly Label _pickerStatus;
#if !TVOS
    readonly Label _dialogResult;
#endif

    public MainPage()
    {
        this.WithPageBackground();

        _statusLabel = new Label
        {
            Text = "Ready - interact with the controls!",
            FontSize = 18,
        }.WithStatusText();

        _sliderValue = new Label
        {
            Text = "Value: 50",
            FontSize = 18,
        }.WithPrimaryText();

        _pickerStatus = new Label
        {
            Text = "No selection",
            FontSize = 18,
        }.WithPrimaryText();

#if !TVOS
        _dialogResult = new Label
        {
            Text = "No dialog shown yet",
            FontSize = 18,
        }.WithStatusText();
#endif

        // --- Title ---
        var title = new Label
        {
#if TVOS
            Text = "MAUI tvOS Sample",
#elif MACOS
            Text = "MAUI macOS Sample",
#endif
            FontSize = 44,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
        }.WithPrimaryText();

        // --- Buttons ---
        var button1 = new Button
        {
            Text = "Primary Button",
            BackgroundColor = AppColors.AccentBlue,
            TextColor = Colors.White,
        };
        button1.Clicked += OnButtonClicked;

        var button2 = new Button
        {
            Text = "Secondary Button",
            BackgroundColor = AppColors.AccentPurple,
            TextColor = Colors.White,
        };
        button2.Clicked += OnButtonClicked;

        // --- Navigation ---
        var navButton = new Button
        {
            Text = "Go to CollectionView Page →",
            BackgroundColor = AppColors.AccentGreen,
            TextColor = Colors.White,
        };
        navButton.Clicked += async (s, e) => await Navigation.PushAsync(new CollectionViewPage());

#if WPFAPP
        var blazorButton = new Button
        {
            Text = "Go to Blazor Page →",
            BackgroundColor = AppColors.AccentPurple,
            TextColor = Colors.White,
        };
        blazorButton.Clicked += async (s, e) => await Navigation.PushAsync(new BlazorPage());

        var graphicsButton = new Button
        {
            Text = "Go to GraphicsView Page →",
            BackgroundColor = AppColors.AccentOrange,
            TextColor = Colors.White,
        };
        graphicsButton.Clicked += async (s, e) => await Navigation.PushAsync(new GraphicsViewPage());
#endif

#if !TVOS
        // --- Dialog Buttons ---
        var alertButton = new Button
        {
            Text = "Show Alert",
            BackgroundColor = AppColors.AccentPink,
            TextColor = Colors.White,
        };
        alertButton.Clicked += OnAlertClicked;

        var confirmButton = new Button
        {
            Text = "Show Confirm",
            BackgroundColor = AppColors.AccentOrange,
            TextColor = Colors.White,
        };
        confirmButton.Clicked += OnConfirmClicked;

        var promptButton = new Button
        {
            Text = "Show Prompt",
            BackgroundColor = AppColors.AccentGreen,
            TextColor = Colors.White,
        };
        promptButton.Clicked += OnPromptClicked;
#endif

        // --- Entry ---
        var entry = new Entry
        {
            Placeholder = "Type something...",
        }.WithEntryTheme();

        // --- SearchBar ---
        var searchResult = new Label
        {
            Text = "No search yet",
            FontSize = 18,
        }.WithStatusText();

        var searchBar = new SearchBar
        {
            Placeholder = "Search...",
        };
        searchBar.SearchButtonPressed += (s, e) =>
        {
            searchResult.Text = !string.IsNullOrEmpty(searchBar.Text)
                ? $"Searched: \"{searchBar.Text}\""
                : "Empty search";
        };
        searchBar.TextChanged += (s, e) =>
        {
            searchResult.Text = $"Typing: \"{e.NewTextValue}\"";
        };

        // --- Picker ---
        var picker = new Picker
        {
            Title = "Pick a color...",
        }.WithPickerTheme();
        picker.Items.Add("Red");
        picker.Items.Add("Green");
        picker.Items.Add("Blue");
        picker.Items.Add("Purple");
        picker.SelectedIndexChanged += OnPickerChanged;

        // --- Slider ---
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = 50,
        };
        slider.ValueChanged += OnSliderChanged;

        // --- Switch ---
        var switchControl = new Switch
        {
            IsToggled = false,
        };

#if !TVOS
        // --- CheckBox ---
        var checkBox = new CheckBox
        {
            IsChecked = false,
            Color = AppColors.AccentBlue,
        };
#endif

        // --- ActivityIndicator ---
        var activityIndicator = new ActivityIndicator
        {
            IsRunning = true,
            Color = AppColors.AccentBlue,
        };

        // --- Image (from URI) ---
        var image = new Image
        {
            Source = new UriImageSource
            {
                Uri = new Uri("https://raw.githubusercontent.com/dotnet/brand/main/logo/dotnet-logo.png"),
            },
            HeightRequest = 200,
            WidthRequest = 200,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center,
        };

        // --- Image (from string URL) ---
        var image2 = new Image
        {
            Source = "https://avatars.githubusercontent.com/u/9141961",
            HeightRequest = 120,
            WidthRequest = 120,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center,
        };

        // --- Border (accent bar) ---
        var boxView = new Border
        {
            BackgroundColor = AppColors.AccentPink,
            HeightRequest = 6,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 3 },
        };

        // --- Layout items in horizontal rows ---
        var layoutRow = new HorizontalStackLayout
        {
            Spacing = 15,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = "Item A", TextColor = Colors.White, BackgroundColor = AppColors.AccentPink, Padding = 12 },
                new Label { Text = "Item B", TextColor = Colors.White, BackgroundColor = AppColors.AccentBlue, Padding = 12 },
                new Label { Text = "Item C", TextColor = Colors.White, BackgroundColor = AppColors.AccentPurple, Padding = 12 },
            },
        };

        // --- Assemble page in a ScrollView ---
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(60, 40),
                Spacing = 20,
                Children =
                {
                    title,
                    boxView,

                    SectionHeader("Grid Layout"),
                    CreateGridDemo(),

                    SectionHeader("Buttons"),
                    button1,
                    button2,

                    SectionHeader("Navigation"),
                    navButton,

#if WPFAPP
                    blazorButton,
                    graphicsButton,
#endif
#if !TVOS
                    SectionHeader("Dialogs"),
                    alertButton,
                    confirmButton,
                    promptButton,
                    _dialogResult,
#endif

                    SectionHeader("Entry"),
                    entry,

                    SectionHeader("SearchBar"),
                    searchBar,
                    searchResult,

#if WPFAPP
                    SectionHeader("Editor (Multiline)"),
                    new Editor
                    {
                        Placeholder = "Type multiline text here...",
                        HeightRequest = 120,
                    }.WithSurfaceBackground(),

                    SectionHeader("Date & Time Pickers"),
                    CreateDateTimePickerDemo(),
#endif

                    SectionHeader("Picker"),
                    picker,
                    _pickerStatus,

                    SectionHeader("Slider"),
                    slider,
                    _sliderValue,

                    SectionHeader("Switch"),
                    switchControl,

#if !TVOS
                    SectionHeader("CheckBox"),
                    checkBox,

                    SectionHeader("WebView"),
                    new WebView
                    {
                        Source = new UrlWebViewSource { Url = "https://dotnet.microsoft.com" },
                        HeightRequest = 400,
                    },
#endif

                    SectionHeader("Images"),
                    image,
                    image2,

                    SectionHeader("Activity Indicator"),
                    activityIndicator,

                    SectionHeader("Progress Bar"),
                    new ProgressBar { Progress = 0.65, ProgressColor = AppColors.AccentBlue, HeightRequest = 8 },
                    new ProgressBar { Progress = 0.3, ProgressColor = AppColors.AccentPink, HeightRequest = 8 },

                    SectionHeader("Horizontal Layout"),
                    layoutRow,

                    SectionHeader("Border"),
                    CreateBorderDemo(),

                    SectionHeader("Shadow"),
                    new Label
                    {
                        Text = "Shadow on Label",
                        TextColor = Colors.White,
                        FontSize = 22,
                        BackgroundColor = AppColors.AccentBlue,
                        Padding = new Thickness(20, 12),
                        Shadow = new Shadow
                        {
                            Brush = Colors.Black,
                            Offset = new Point(4, 4),
                            Radius = 8,
                            Opacity = 0.6f,
                        },
                    },
                    new Button
                    {
                        Text = "Shadow on Button",
                        BackgroundColor = AppColors.AccentGreen,
                        TextColor = Colors.White,
                        Shadow = new Shadow
                        {
                            Brush = AppColors.AccentGreen,
                            Offset = new Point(0, 6),
                            Radius = 12,
                            Opacity = 0.5f,
                        },
                    },

#if TVAPP
                    SectionHeader("Carousel View"),
                    CreateCarouselDemo(),
#endif

                    SectionHeader("Status"),
                    _statusLabel,

                    new Border { HeightRequest = 40, StrokeThickness = 0 },
                },
            },
        };
    }

    static Grid CreateGridDemo()
    {
        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
            RowSpacing = 10,
            ColumnSpacing = 10,
        };

        var cell1 = new Label { Text = "Row 0, Col 0", TextColor = Colors.White, BackgroundColor = AppColors.AccentRed, Padding = 16, HorizontalTextAlignment = TextAlignment.Center };
        var cell2 = new Label { Text = "Row 0, Col 1", TextColor = Colors.White, BackgroundColor = AppColors.AccentBlue, Padding = 16, HorizontalTextAlignment = TextAlignment.Center };
        var cell3 = new Label { Text = "Row 1, Col 0", TextColor = Colors.White, BackgroundColor = AppColors.AccentGreen, Padding = 16, HorizontalTextAlignment = TextAlignment.Center };
        var cell4 = new Label { Text = "Row 1, Col 1", TextColor = Colors.White, BackgroundColor = AppColors.AccentPurple, Padding = 16, HorizontalTextAlignment = TextAlignment.Center };

        grid.Add(cell1, 0, 0);
        grid.Add(cell2, 1, 0);
        grid.Add(cell3, 0, 1);
        grid.Add(cell4, 1, 1);

        return grid;
    }

    static Label SectionHeader(string text) => new Label
    {
        Text = text,
        FontSize = 28,
        FontAttributes = FontAttributes.Bold,
    }.WithSectionStyle();

    static View CreateBorderDemo()
    {
        var stack = new VerticalStackLayout { Spacing = 15 };

        // Simple rounded border
        var border1 = new Border
        {
            Stroke = AppColors.AccentBlue,
            StrokeThickness = 2,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(16),
            Content = new Label
            {
                Text = "Rounded Border",
                FontSize = 18,
                HorizontalTextAlignment = TextAlignment.Center,
            }.WithPrimaryText(),
        };

        // Thick dashed border
        var border2 = new Border
        {
            Stroke = AppColors.AccentPink,
            StrokeThickness = 3,
            StrokeDashArray = new Microsoft.Maui.Controls.DoubleCollection { 6, 3 },
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(16),
            Content = new Label
            {
                Text = "Dashed Border",
                FontSize = 18,
                HorizontalTextAlignment = TextAlignment.Center,
            }.WithPrimaryText(),
        };

        // Border with background
        var border3 = new Border
        {
            Stroke = AppColors.AccentGreen,
            StrokeThickness = 2,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 },
            Padding = new Thickness(20),
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new Label { Text = "Card Style", FontSize = 20, FontAttributes = FontAttributes.Bold }.WithPrimaryText(),
                    new Label { Text = "Border with background and rounded corners", FontSize = 14 }.WithSecondaryText(),
                },
            },
        }.WithSurfaceBackground();

        stack.Children.Add(border1);
        stack.Children.Add(border2);
        stack.Children.Add(border3);
        return stack;
    }

#if TVAPP
    static View CreateCarouselDemo()
    {
        var colors = new[]
        {
            ("#E74C3C", "Slide 1 — Red"),
            ("#3498DB", "Slide 2 — Blue"),
            ("#2ECC71", "Slide 3 — Green"),
            ("#9B59B6", "Slide 4 — Purple"),
            ("#F39C12", "Slide 5 — Orange"),
        };

        var positionLabel = new Label { Text = "Position: 0", FontSize = 18 }.WithPrimaryText();

        var carousel = new CarouselView
        {
            HeightRequest = 200,
            ItemsSource = colors,
            ItemTemplate = new DataTemplate(() =>
            {
                var label = new Label
                {
                    FontSize = 28,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                };
                label.SetBinding(Label.TextProperty, "[Item2]");
                label.SetBinding(Label.BackgroundColorProperty, "[Item1]",
                    converter: new FuncConverter<string, Color>(hex => Color.FromArgb(hex)));

                return label;
            }),
        };

        carousel.PositionChanged += (s, e) => positionLabel.Text = $"Position: {e.CurrentPosition}";

        return new VerticalStackLayout
        {
            Spacing = 10,
            Children = { carousel, positionLabel },
        };
    }

    class FuncConverter<TIn, TOut> : IValueConverter
    {
        readonly Func<TIn, TOut> _convert;
        public FuncConverter(Func<TIn, TOut> convert) => _convert = convert;
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => value is TIn input ? _convert(input) : default;
        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }
#endif

#if WPFAPP
    static View CreateDateTimePickerDemo()
    {
        var dateLabel = new Label { Text = "Selected date: (none)", FontSize = 16 }.WithPrimaryText();
        var datePicker = new DatePicker
        {
            Date = DateTime.Today,
            MinimumDate = new DateTime(2020, 1, 1),
            MaximumDate = new DateTime(2030, 12, 31),
        };
        datePicker.DateSelected += (s, e) => dateLabel.Text = $"Selected date: {e.NewDate:d}";

        var timeLabel = new Label { Text = "Selected time: (none)", FontSize = 16 }.WithPrimaryText();
        var timePicker = new TimePicker
        {
            Time = DateTime.Now.TimeOfDay,
        };
        timePicker.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(TimePicker.Time))
                timeLabel.Text = $"Selected time: {timePicker.Time:hh\\:mm\\:ss}";
        };

        return new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                new Label { Text = "Date Picker", TextColor = AppColors.AccentBlue, FontSize = 18 },
                datePicker,
                dateLabel,
                new Border { HeightRequest = 4, StrokeThickness = 0 },
                new Label { Text = "Time Picker", TextColor = AppColors.AccentBlue, FontSize = 18 },
                timePicker,
                timeLabel,
            },
        };
    }
#endif

    void OnButtonClicked(object? sender, EventArgs e)
    {
        _buttonClickCount++;
        _statusLabel.Text = $"Button clicked {_buttonClickCount} time{(_buttonClickCount != 1 ? "s" : "")}";
    }

    void OnPickerChanged(object? sender, EventArgs e)
    {
        if (sender is Picker p && p.SelectedIndex >= 0)
        {
            _pickerStatus.Text = $"Selected: {p.Items[p.SelectedIndex]}";
            _statusLabel.Text = $"Picker: {p.Items[p.SelectedIndex]}";
        }
    }

    void OnSliderChanged(object? sender, ValueChangedEventArgs e)
    {
        _sliderValue.Text = $"Value: {(int)e.NewValue}";
        _statusLabel.Text = $"Slider: {(int)e.NewValue}";
    }

#if !TVOS
    async void OnAlertClicked(object? sender, EventArgs e)
    {
        await DisplayAlertAsync("Alert", "This is a simple alert message.", "OK");
        _dialogResult.Text = "Alert dismissed";
        _statusLabel.Text = "Alert dismissed";
    }

    async void OnConfirmClicked(object? sender, EventArgs e)
    {
        var result = await DisplayAlertAsync("Confirm", "Do you want to proceed?", "Yes", "No");
        _dialogResult.Text = $"Confirm result: {(result ? "Yes" : "No")}";
        _statusLabel.Text = $"Confirm: {(result ? "Yes" : "No")}";
    }

    async void OnPromptClicked(object? sender, EventArgs e)
    {
        var result = await DisplayPromptAsync("Prompt", "Enter your name:", placeholder: "Type here...");
        _dialogResult.Text = result != null ? $"Prompt result: {result}" : "Prompt cancelled";
        _statusLabel.Text = result != null ? $"Prompt: {result}" : "Prompt cancelled";
    }
#endif
}

