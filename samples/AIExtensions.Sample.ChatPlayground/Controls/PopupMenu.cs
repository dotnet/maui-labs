namespace AIExtensions.Sample.ChatPlayground.Controls;

/// <summary>Attaches a tap-open menu to a button in a grid-backed page.</summary>
public static class PopupMenu
{
    public static readonly BindableProperty ContentProperty = BindableProperty.CreateAttached(
        "Content", typeof(View), typeof(PopupMenu), null, propertyChanged: OnContentChanged);

    private static readonly BindableProperty ActiveMenuProperty = BindableProperty.CreateAttached(
        "ActiveMenu", typeof(MenuSession), typeof(PopupMenu), null);

    public static View? GetContent(BindableObject button) => (View?)button.GetValue(ContentProperty);

    public static void SetContent(BindableObject button, View? content) => button.SetValue(ContentProperty, content);

    public static void Dismiss(View anchor)
    {
        var root = FindRoot(anchor);
        if (root.GetValue(ActiveMenuProperty) is MenuSession session &&
            (session.Anchor == anchor || session.Content == anchor))
            session.Close();
    }

    private static void OnContentChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not Button and not ImageButton)
            throw new InvalidOperationException("PopupMenu.Content must be attached to a Button or ImageButton.");

        if (oldValue is not null)
            SetClickHandler(bindable, attach: false);
        if (newValue is not null)
            SetClickHandler(bindable, attach: true);
    }

    private static void SetClickHandler(BindableObject button, bool attach)
    {
        switch (button)
        {
            case Button textButton:
                if (attach) textButton.Clicked += OnButtonClicked;
                else textButton.Clicked -= OnButtonClicked;
                break;
            case ImageButton imageButton:
                if (attach) imageButton.Clicked += OnButtonClicked;
                else imageButton.Clicked -= OnButtonClicked;
                break;
        }
    }

    private static void OnButtonClicked(object? sender, EventArgs e)
    {
        if (sender is not View anchor || GetContent(anchor) is not View content)
            return;

        var root = FindRoot(anchor);
        if (root.GetValue(ActiveMenuProperty) is MenuSession previous)
        {
            previous.Close();
            if (previous.Anchor == anchor)
                return;
        }

        var session = new MenuSession(root, anchor, content);
        root.SetValue(ActiveMenuProperty, session);
        session.Open();
    }

    private static Grid FindRoot(View anchor)
    {
        var page = anchor.Parent;
        while (page is not null && page is not ContentPage)
            page = page.Parent;
        if (page is not ContentPage { Content: Grid root })
            throw new InvalidOperationException("PopupMenu requires a ContentPage with a Grid root.");
        return root;
    }

    private sealed class MenuSession
    {
        private readonly Grid _root;
        private readonly PopupMenuView _overlay;
        private readonly View _menuContent;
        private readonly double _requestedWidth;
        private readonly List<Button> _actionButtons = [];
        private readonly List<ImageButton> _imageActionButtons = [];

        public MenuSession(Grid root, View anchor, View content)
        {
            _root = root;
            Anchor = anchor;
            _menuContent = content;
            _requestedWidth = content.WidthRequest;
            _overlay = new PopupMenuView
            {
                BindingContext = anchor.BindingContext,
                MenuContent = content,
            };
            _overlay.DismissRequested += DismissRequested;
            SubscribeActions(content);
        }

        public View Anchor { get; }
        public View Content => _menuContent;

        public void Open()
        {
            Grid.SetColumnSpan(_overlay, Math.Max(1, _root.ColumnDefinitions.Count));
            Grid.SetRowSpan(_overlay, Math.Max(1, _root.RowDefinitions.Count));
            _overlay.ZIndex = 100;
            _root.Children.Add(_overlay);
            _overlay.SizeChanged += OverlaySizeChanged;
            _menuContent.SizeChanged += ContentSizeChanged;
            Anchor.SizeChanged += AnchorSizeChanged;
            Anchor.PropertyChanged += AnchorPropertyChanged;
            Anchor.Unloaded += AnchorUnloaded;
            PositionMenu();
        }

        public void Close()
        {
            _overlay.SizeChanged -= OverlaySizeChanged;
            _menuContent.SizeChanged -= ContentSizeChanged;
            _overlay.DismissRequested -= DismissRequested;
            Anchor.SizeChanged -= AnchorSizeChanged;
            Anchor.PropertyChanged -= AnchorPropertyChanged;
            Anchor.Unloaded -= AnchorUnloaded;
            foreach (var button in _actionButtons)
                button.Clicked -= ActionClicked;
            foreach (var button in _imageActionButtons)
                button.Clicked -= ActionClicked;
            if (ReferenceEquals(_root.GetValue(ActiveMenuProperty), this))
                _root.ClearValue(ActiveMenuProperty);
            _root.Children.Remove(_overlay);
            _overlay.MenuContent = null;
            if (_requestedWidth >= 0)
                _menuContent.WidthRequest = _requestedWidth;
        }

        private void SubscribeActions(View view)
        {
            switch (view)
            {
                case Button button:
                    button.Clicked += ActionClicked;
                    _actionButtons.Add(button);
                    break;
                case ImageButton button:
                    button.Clicked += ActionClicked;
                    _imageActionButtons.Add(button);
                    break;
                case Layout layout:
                    foreach (var child in layout.Children.OfType<View>())
                        SubscribeActions(child);
                    break;
                case ContentView { Content: View child }:
                    SubscribeActions(child);
                    break;
                case Border { Content: View child }:
                    SubscribeActions(child);
                    break;
            }
        }

        private void ActionClicked(object? sender, EventArgs e) => Close();
        private void DismissRequested(object? sender, EventArgs e) => Close();
        private void AnchorUnloaded(object? sender, EventArgs e) => Close();
        private void OverlaySizeChanged(object? sender, EventArgs e) => PositionMenu();
        private void ContentSizeChanged(object? sender, EventArgs e) => PositionMenu();
        private void AnchorSizeChanged(object? sender, EventArgs e) => PositionMenu();

        private void AnchorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(View.IsEnabled) && !Anchor.IsEnabled)
                Close();
        }

        private void PositionMenu()
        {
            if (_overlay.Width <= 0 || _overlay.Height <= 0)
                return;

            const double gap = 8;
            const double inset = 12;
            var availableWidth = Math.Max(0, _overlay.Width - inset * 2);
            var availableHeight = Math.Max(0, _overlay.Height - inset * 2);
            if (_requestedWidth >= 0)
            {
                var fittedWidth = Math.Min(
                    _requestedWidth, Math.Max(0, availableWidth - _overlay.HorizontalPadding));
                if (_menuContent.WidthRequest != fittedWidth)
                    _menuContent.WidthRequest = fittedWidth;
            }
            var desired = _overlay.MeasureMenu(availableWidth, availableHeight);
            // CollectionView templates can under-measure on iOS; honor an explicit menu width.
            var requestedPanelWidth = _requestedWidth >= 0
                ? _menuContent.WidthRequest + _overlay.HorizontalPadding : 0;
            var width = Math.Min(availableWidth, Math.Max(200, Math.Max(desired.Width, requestedPanelWidth)));
            var height = Math.Min(availableHeight, desired.Height);

            // Convert the anchor through scrolled ancestors into overlay coordinates before clamping.
            double anchorX = 0, anchorY = 0;
            for (Element? current = Anchor;
                current is VisualElement visual && current != _root;
                current = current.Parent)
            {
                anchorX += visual.X;
                anchorY += visual.Y;
                if (visual.Parent is ScrollView scroll)
                {
                    anchorX -= scroll.ScrollX;
                    anchorY -= scroll.ScrollY;
                }
            }
            anchorX -= _overlay.X;
            anchorY -= _overlay.Y;

            var x = anchorX + Anchor.Width / 2 > _overlay.Width / 2
                ? anchorX + Anchor.Width - width
                : anchorX;
            x = Math.Clamp(x, inset, Math.Max(inset, _overlay.Width - width - inset));
            var below = anchorY + Anchor.Height + gap;
            var y = below + height + inset <= _overlay.Height
                ? below
                : anchorY - height - gap;
            y = Math.Clamp(y, inset, Math.Max(inset, _overlay.Height - height - inset));
            _overlay.SetMenuPosition(new Rect(x, y, width, height));
        }
    }
}
