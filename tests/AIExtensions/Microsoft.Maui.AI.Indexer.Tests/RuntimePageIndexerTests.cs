using Microsoft.Maui.Controls;

namespace Microsoft.Maui.AI.Indexer.Tests;

public sealed class RuntimePageIndexerTests
{
    [Fact]
    public void Capture_VisibleDynamicControls_RendersLiveStateAndSkipsHiddenBranch()
    {
        var heading = new Label { Text = "Write Review" };
        SemanticProperties.SetHeadingLevel(heading, SemanticHeadingLevel.Level1);

        var cancel = new Button { Text = "\uf36a" };
        SemanticProperties.SetDescription(cancel, "Cancel");
        SemanticProperties.SetHint(cancel, "Returns to product details");

        var rating = new Slider { Minimum = 1, Maximum = 5, Value = 4 };
        SemanticProperties.SetDescription(rating, "Rating");
        SemanticProperties.SetHint(rating, "Slide to select 1 to 5 stars");

        var hiddenBranch = new VerticalStackLayout
        {
            IsVisible = false,
            Children =
            {
                new Entry { Placeholder = "Admin note" },
            },
        };

        var page = new ContentPage
        {
            Content = new VerticalStackLayout
            {
                Children =
                {
                    heading,
                    new Label { Text = "Heirloom Tomato Seeds" },
                    hiddenBranch,
                    rating,
                    new Editor
                    {
                        Text = "A draft that stays private",
                        Placeholder = "Share your experience...",
                    },
                    cancel,
                    new Button { Text = "Submit Review", IsEnabled = false },
                },
            },
        };

        var snapshot = RuntimePageIndexer.Capture(page);

        Assert.Equal("ContentPage", snapshot.PageName);
        Assert.Equal(
            """
            # Current UI: ContentPage

            Runtime snapshot: currently visible, materialized controls and live state.

            - Heading (level 1): "Write Review"
            - Label: "Heirloom Tomato Seeds"
            - Slider: "Rating" [hint: Slide to select 1 to 5 stars, value: 4, range: 1–5]
            - Editor: [placeholder: "Share your experience...", has text; value omitted]
            - Button: "Cancel" [hint: Returns to product details]
            - Button: "Submit Review" [disabled]

            """,
            snapshot.Markdown);
        Assert.DoesNotContain("Admin note", snapshot.Markdown);
        Assert.DoesNotContain("A draft that stays private", snapshot.Markdown);
    }

    [Fact]
    public void Capture_InputTextOptIn_IncludesOrdinaryTextButNeverPasswords()
    {
        var page = new ContentPage
        {
            Content = new VerticalStackLayout
            {
                Children =
                {
                    new Entry
                    {
                        Text = "person@example.com",
                        Placeholder = "Email address",
                    },
                    new Editor
                    {
                        Text = "Current review draft",
                        Placeholder = "Review",
                    },
                    new Entry
                    {
                        Text = "correct horse battery staple",
                        Placeholder = "Password",
                        IsPassword = true,
                    },
                },
            },
        };

        var defaultSnapshot = RuntimePageIndexer.Capture(page);
        var optInSnapshot = RuntimePageIndexer.Capture(
            page,
            new CurrentPageSnapshotOptions { IncludeInputText = true });

        Assert.DoesNotContain("person@example.com", defaultSnapshot.Markdown);
        Assert.DoesNotContain("Current review draft", defaultSnapshot.Markdown);
        Assert.DoesNotContain("correct horse battery staple", defaultSnapshot.Markdown);
        Assert.Contains("placeholder: \"Email address\", has text; value omitted", defaultSnapshot.Markdown);
        Assert.Contains("placeholder: \"Review\", has text; value omitted", defaultSnapshot.Markdown);

        Assert.Contains("value: \"person@example.com\"", optInSnapshot.Markdown);
        Assert.Contains("value: \"Current review draft\"", optInSnapshot.Markdown);
        Assert.Contains(
            "placeholder: \"Password\", secure input, has text; value omitted",
            optInSnapshot.Markdown);
        Assert.DoesNotContain("correct horse battery staple", optInSnapshot.Markdown);
    }

    [Fact]
    public void Capture_DefaultInputPrivacy_RedactsCurrentTextFromMetadata()
    {
        const string privateText = "person@example.com";
        var entry = new Entry
        {
            Text = privateText,
            Placeholder = $"Email: {privateText}",
        };
        SemanticProperties.SetDescription(entry, $"Current account is {privateText}");
        SemanticProperties.SetHint(entry, $"Edit {privateText}");

        var page = new ContentPage { Content = entry };

        var defaultSnapshot = RuntimePageIndexer.Capture(page);
        var optInSnapshot = RuntimePageIndexer.Capture(
            page,
            new CurrentPageSnapshotOptions { IncludeInputText = true });

        Assert.DoesNotContain(privateText, defaultSnapshot.Markdown);
        Assert.Contains("\"Current account is ••••\"", defaultSnapshot.Markdown);
        Assert.Contains("placeholder: \"Email: ••••\"", defaultSnapshot.Markdown);
        Assert.Contains("hint: Edit ••••", defaultSnapshot.Markdown);

        Assert.Contains(privateText, optInSnapshot.Markdown);
        Assert.Contains($"value: \"{privateText}\"", optInSnapshot.Markdown);
    }

    [Fact]
    public void Capture_PasswordValueInSemanticMetadata_IsAlwaysRedacted()
    {
        const string secret = "correct horse battery staple";
        var password = new Entry
        {
            Text = secret,
            IsPassword = true,
            Placeholder = $"Password: {secret}",
        };
        SemanticProperties.SetDescription(password, $"Current value is {secret}");
        SemanticProperties.SetHint(password, $"Do not reveal {secret}");

        var page = new ContentPage { Content = password };

        var snapshot = RuntimePageIndexer.Capture(
            page,
            new CurrentPageSnapshotOptions { IncludeInputText = true });

        Assert.DoesNotContain(secret, snapshot.Markdown);
        Assert.Contains("\"Current value is ••••\"", snapshot.Markdown);
        Assert.Contains("placeholder: \"Password: ••••\"", snapshot.Markdown);
        Assert.Contains("hint: Do not reveal ••••", snapshot.Markdown);
        Assert.Contains("secure input, has text; value omitted", snapshot.Markdown);
    }

    [Fact]
    public void Capture_ResolvedBinding_UsesCurrentValueInsteadOfBindingPath()
    {
        var productName = new Label();
        productName.SetBinding(Label.TextProperty, nameof(RuntimeViewModel.ProductName));
        productName.BindingContext = new RuntimeViewModel
        {
            ProductName = "Heirloom Tomato Seeds",
        };

        var page = new ContentPage { Content = productName };

        var snapshot = RuntimePageIndexer.Capture(page);

        Assert.Contains("- Label: \"Heirloom Tomato Seeds\"", snapshot.Markdown);
        Assert.DoesNotContain("{ProductName}", snapshot.Markdown);
    }

    [Fact]
    public void Capture_EmptySemanticDescription_OmitsDecorativeSubtree()
    {
        var icon = new Label { Text = "\uf788" };
        SemanticProperties.SetDescription(icon, "");

        var page = new ContentPage
        {
            Content = new VerticalStackLayout
            {
                Children =
                {
                    icon,
                    new Label { Text = "Products" },
                },
            },
        };

        var snapshot = RuntimePageIndexer.Capture(page);

        Assert.DoesNotContain("\uf788", snapshot.Markdown);
        Assert.Contains("- Label: \"Products\"", snapshot.Markdown);
    }

    [Fact]
    public void Capture_CustomContainer_PreservesRuntimeHierarchy()
    {
        var page = new ContentPage
        {
            Content = new ReviewSection
            {
                Content = new VerticalStackLayout
                {
                    Children =
                    {
                        new Label { Text = "Comment (optional)" },
                        new Editor { Placeholder = "Share your experience..." },
                    },
                },
            },
        };

        var snapshot = RuntimePageIndexer.Capture(page);

        Assert.Contains(
            """
            - [ReviewSection]:
              - Label: "Comment (optional)"
              - Editor: [placeholder: "Share your experience...", empty]
            """,
            snapshot.Markdown);
    }

    [Fact]
    public void Capture_DescribedFrameworkContainer_PreservesAccessibleGroup()
    {
        var card = new Border
        {
            Content = new Label { Text = "Inside" },
        };
        SemanticProperties.SetDescription(card, "Product card");

        var page = new ContentPage { Content = card };

        var snapshot = RuntimePageIndexer.Capture(page);

        Assert.Contains(
            """
            - Border: "Product card"
              - Label: "Inside"
            """,
            snapshot.Markdown);
    }

    [Fact]
    public void Capture_ExcludedSubtree_OmitsGroupAndDescendants()
    {
        var sidebar = new ReviewSection
        {
            Content = new Entry
            {
                Text = "Assistant input",
                Placeholder = "Ask Sage",
            },
        };
        IndexingProperties.SetExcludeWithChildren(sidebar, true);

        var page = new ContentPage
        {
            Content = new VerticalStackLayout
            {
                Children =
                {
                    new Label { Text = "Page content" },
                    sidebar,
                },
            },
        };

        var snapshot = RuntimePageIndexer.Capture(
            page,
            new CurrentPageSnapshotOptions { IncludeInputText = true });

        Assert.Contains("- Label: \"Page content\"", snapshot.Markdown);
        Assert.DoesNotContain("ReviewSection", snapshot.Markdown);
        Assert.DoesNotContain("Assistant input", snapshot.Markdown);
        Assert.DoesNotContain("Ask Sage", snapshot.Markdown);
    }

    [Fact]
    public void Capture_ExcludedRoot_OmitsAllRuntimeContent()
    {
        var page = new ContentPage
        {
            Content = new Label { Text = "Excluded secret" },
        };
        IndexingProperties.SetExcludeWithChildren(page, true);

        var snapshot = RuntimePageIndexer.Capture(page);

        Assert.DoesNotContain("Excluded secret", snapshot.Markdown);
    }

    [Fact]
    public void Capture_ExcludedNavigationContainer_DoesNotLeakCurrentPage()
    {
        var currentPage = new ContentPage
        {
            Content = new Label { Text = "Nested secret" },
        };
        var navigation = new NavigationPage(currentPage);
        IndexingProperties.SetExcludeWithChildren(navigation, true);
        var window = new Window(navigation);

        var snapshot = RuntimePageIndexer.Capture(window);

        Assert.Null(snapshot);
    }

    [Fact]
    public void Capture_MaximumTextLength_TruncatesLongRuntimeValues()
    {
        var page = new ContentPage
        {
            Content = new Label { Text = "1234567890" },
        };

        var snapshot = RuntimePageIndexer.Capture(
            page,
            new CurrentPageSnapshotOptions { MaximumTextLength = 6 });

        Assert.Contains("- Label: \"12345…\"", snapshot.Markdown);
    }

    [Fact]
    public void Capture_PickerDisplayBinding_UsesVisibleItemText()
    {
        var picker = new Picker
        {
            ItemsSource = new[]
            {
                new RuntimeOption("Normal mode"),
                new RuntimeOption("Compact mode"),
            },
            ItemDisplayBinding = new Binding(nameof(RuntimeOption.Name)),
            SelectedIndex = 1,
        };
        var page = new ContentPage { Content = picker };

        var snapshot = RuntimePageIndexer.Capture(page);

        Assert.Contains("selected: \"Compact mode\"", snapshot.Markdown);
        Assert.DoesNotContain(nameof(RuntimeOption), snapshot.Markdown);
    }

    [Fact]
    public void Capture_MediaSources_EmitsOnlyPrivacySafeSourceKinds()
    {
        var page = new ContentPage
        {
            Content = new VerticalStackLayout
            {
                Children =
                {
                    new Image
                    {
                        Source = new UriImageSource
                        {
                            Uri = new Uri(
                                "https://user:password@example.com/image.png?sig=secret#fragment"),
                        },
                    },
                    new WebView
                    {
                        Source = new UrlWebViewSource
                        {
                            Url = "https://example.com/help?token=secret#section",
                        },
                    },
                    new Image
                    {
                        Source = new FileImageSource
                        {
                            File = "/Users/person/private-image-name.png",
                        },
                    },
                },
            },
        };

        var snapshot = RuntimePageIndexer.Capture(page);

        Assert.Contains("- Image: \"remote image\"", snapshot.Markdown);
        Assert.Contains("- WebView: \"web content\"", snapshot.Markdown);
        Assert.Contains("- Image: \"local image\"", snapshot.Markdown);
        Assert.DoesNotContain("example.com", snapshot.Markdown);
        Assert.DoesNotContain("/Users/person", snapshot.Markdown);
        Assert.DoesNotContain("user", snapshot.Markdown);
        Assert.DoesNotContain("password", snapshot.Markdown);
        Assert.DoesNotContain("secret", snapshot.Markdown);
        Assert.DoesNotContain("fragment", snapshot.Markdown);
    }

    [Fact]
    public void Capture_HiddenWindowPage_ReturnsNoSnapshot()
    {
        var page = new ContentPage
        {
            IsVisible = false,
            Content = new Label { Text = "Hidden content" },
        };
        var window = new Window(page);

        var snapshot = RuntimePageIndexer.Capture(window);

        Assert.Null(snapshot);
    }

    [Fact]
    public void Capture_PresentedFlyout_IncludesFlyoutAndDetail()
    {
        var page = new FlyoutPage
        {
            Flyout = new ContentPage
            {
                Title = "Menu",
                Content = new Label { Text = "Menu item" },
            },
            Detail = new ContentPage { Content = new Label { Text = "Detail content" } },
            IsPresented = true,
        };
        var window = new Window(page);

        var snapshot = RuntimePageIndexer.Capture(window);

        Assert.NotNull(snapshot);
        Assert.Equal("FlyoutPage", snapshot.PageName);
        Assert.Contains("- Label: \"Menu item\"", snapshot.Markdown);
        Assert.Contains("- Label: \"Detail content\"", snapshot.Markdown);
    }

    [Fact]
    public void Capture_PresentedShellFlyout_IncludesMenuAndCurrentPageOnly()
    {
        var currentPage = new ContentPage { Content = new Label { Text = "Current page content" } };
        var otherPage = new ContentPage { Content = new Label { Text = "Hidden page content" } };
        var currentItem = new FlyoutItem
        {
            Title = "Current",
            Items =
            {
                new ShellContent { Title = "Current", Content = currentPage },
            },
        };
        var otherItem = new FlyoutItem
        {
            Title = "Other",
            Items =
            {
                new ShellContent { Title = "Other", Content = otherPage },
            },
        };
        var shell = new Shell
        {
            FlyoutBehavior = FlyoutBehavior.Flyout,
            Items =
            {
                currentItem,
                otherItem,
            },
            CurrentItem = currentItem,
            FlyoutIsPresented = true,
        };
        var window = new Window(shell);

        var snapshot = RuntimePageIndexer.Capture(window);

        Assert.NotNull(snapshot);
        Assert.Equal("Shell", snapshot.PageName);
        Assert.Contains("- Item: \"Current\" [selected]", snapshot.Markdown);
        Assert.Contains("- Item: \"Other\"", snapshot.Markdown);
        Assert.Contains("- Current page: ContentPage", snapshot.Markdown);
        Assert.Contains("- Label: \"Current page content\"", snapshot.Markdown);
        Assert.DoesNotContain("Hidden page content", snapshot.Markdown);
    }

    [Fact]
    public void Capture_PresentedShellFlyout_UsesEffectiveMultipleItemsAndMenus()
    {
        var currentPage = new ContentPage { Content = new Label { Text = "Current page content" } };
        var otherPage = new ContentPage { Content = new Label { Text = "Hidden page content" } };
        var currentContent = new ShellContent
        {
            Title = "Current child",
            Content = currentPage,
            MenuItems =
            {
                new MenuItem { Text = "Help" },
            },
        };
        var currentSection = new Tab
        {
            Title = "Current child",
            Items =
            {
                currentContent,
            },
        };
        var otherSection = new Tab
        {
            Title = "Other child",
            Items =
            {
                new ShellContent { Title = "Other child", Content = otherPage },
            },
        };
        var group = new FlyoutItem
        {
            Title = "Hidden group title",
            FlyoutDisplayOptions = FlyoutDisplayOptions.AsMultipleItems,
            Items =
            {
                currentSection,
                otherSection,
            },
            CurrentItem = currentSection,
        };
        currentSection.CurrentItem = currentContent;

        var shell = new Shell
        {
            Items =
            {
                group,
            },
            CurrentItem = group,
            FlyoutIsPresented = true,
        };
        var window = new Window(shell);

        var snapshot = RuntimePageIndexer.Capture(window);

        Assert.NotNull(snapshot);
        Assert.Contains("- Item: \"Current child\" [selected]", snapshot.Markdown);
        Assert.Contains("- Item: \"Help\"", snapshot.Markdown);
        Assert.Contains("- Item: \"Other child\"", snapshot.Markdown);
        Assert.DoesNotContain("Hidden group title", snapshot.Markdown);
        Assert.DoesNotContain("Hidden page content", snapshot.Markdown);
    }

    [Fact]
    public void Capture_PresentedShellFlyout_RendersCustomContentHeaderAndFooter()
    {
        var shell = new Shell
        {
            FlyoutHeader = new Label { Text = "Account header" },
            FlyoutContent = new VerticalStackLayout
            {
                Children =
                {
                    new Button { Text = "Custom destination" },
                },
            },
            FlyoutFooter = new Label { Text = "Version 1" },
            Items =
            {
                new FlyoutItem
                {
                    Title = "Default item",
                    Items =
                    {
                        new ShellContent
                        {
                            Title = "Default item",
                            Content = new ContentPage
                            {
                                Content = new Label { Text = "Current content" },
                            },
                        },
                    },
                },
            },
            FlyoutIsPresented = true,
        };
        var window = new Window(shell);

        var snapshot = RuntimePageIndexer.Capture(window);

        Assert.NotNull(snapshot);
        Assert.Contains("- Flyout header:", snapshot.Markdown);
        Assert.Contains("- Label: \"Account header\"", snapshot.Markdown);
        Assert.Contains("- Flyout content:", snapshot.Markdown);
        Assert.Contains("- Button: \"Custom destination\"", snapshot.Markdown);
        Assert.Contains("- Flyout footer:", snapshot.Markdown);
        Assert.Contains("- Label: \"Version 1\"", snapshot.Markdown);
        Assert.DoesNotContain("Default item", snapshot.Markdown);
    }

    [Fact]
    public void Capture_PresentedShellFlyout_OmitsExcludedMenuCommand()
    {
        var content = new ShellContent
        {
            Title = "Current",
            Content = new ContentPage(),
        };
        var visible = new MenuItem { Text = "Visible command" };
        var excluded = new MenuItem { Text = "Private command" };
        IndexingProperties.SetExcludeWithChildren(excluded, true);
        content.MenuItems.Add(visible);
        content.MenuItems.Add(excluded);

        var item = new FlyoutItem
        {
            Title = "Current",
            Items =
            {
                content,
            },
        };
        var shell = new Shell
        {
            Items =
            {
                item,
            },
            CurrentItem = item,
            FlyoutIsPresented = true,
        };
        var window = new Window(shell);

        var snapshot = RuntimePageIndexer.Capture(window);

        Assert.NotNull(snapshot);
        Assert.Contains("- Item: \"Visible command\"", snapshot.Markdown);
        Assert.DoesNotContain("Private command", snapshot.Markdown);
    }

    [Fact]
    public void Capture_PresentedShellFlyout_OmitsExcludedTopLevelMenuCommand()
    {
        var privateCommand = new MenuItem { Text = "Private top-level command" };
        IndexingProperties.SetExcludeWithChildren(privateCommand, true);

        var currentItem = new FlyoutItem
        {
            Title = "Current",
            Items =
            {
                new ShellContent
                {
                    Title = "Current",
                    Content = new ContentPage(),
                },
            },
        };
        var shell = new Shell
        {
            Items =
            {
                currentItem,
                privateCommand,
            },
            CurrentItem = currentItem,
            FlyoutIsPresented = true,
        };
        var window = new Window(shell);

        var snapshot = RuntimePageIndexer.Capture(window);

        Assert.NotNull(snapshot);
        Assert.DoesNotContain("Private top-level command", snapshot.Markdown);
    }

    [Fact]
    public void Capture_ExcludedActiveShellContent_DoesNotLeakPageOrFlyoutEntry()
    {
        var privatePage = new ContentPage
        {
            Content = new Label { Text = "Private page content" },
        };
        var privateContent = new ShellContent
        {
            Title = "Private page",
            Content = privatePage,
        };
        IndexingProperties.SetExcludeWithChildren(privateContent, true);

        var privateSection = new Tab
        {
            Title = "Private page",
            Items =
            {
                privateContent,
            },
            CurrentItem = privateContent,
        };
        var privateItem = new FlyoutItem
        {
            Title = "Private group",
            FlyoutDisplayOptions = FlyoutDisplayOptions.AsMultipleItems,
            Items =
            {
                privateSection,
            },
            CurrentItem = privateSection,
        };
        var shell = new Shell
        {
            Items =
            {
                privateItem,
            },
            CurrentItem = privateItem,
        };
        var window = new Window(shell);

        var closedSnapshot = RuntimePageIndexer.Capture(window);
        shell.FlyoutIsPresented = true;
        var openSnapshot = RuntimePageIndexer.Capture(window);

        Assert.Null(closedSnapshot);
        Assert.NotNull(openSnapshot);
        Assert.DoesNotContain("Private page content", openSnapshot.Markdown);
        Assert.DoesNotContain("- Item: \"Private page\"", openSnapshot.Markdown);
        Assert.DoesNotContain("- Current page:", openSnapshot.Markdown);
    }

    [Fact]
    public void Capture_NonPositiveMaximumTextLength_Throws()
    {
        var page = new ContentPage();
        var options = new CurrentPageSnapshotOptions { MaximumTextLength = 0 };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => RuntimePageIndexer.Capture(page, options));

        Assert.Equal("MaximumTextLength", exception.ParamName);
    }

    private sealed class ReviewSection : ContentView;

    private sealed class RuntimeViewModel
    {
        public string ProductName { get; init; } = "";
    }

    private sealed record RuntimeOption(string Name);
}
