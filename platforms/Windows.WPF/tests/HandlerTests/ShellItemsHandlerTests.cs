using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Handlers.WPF;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Windows.WPF;
using Microsoft.Maui.WPF;
using WGrid = System.Windows.Controls.Grid;
using Dispatcher = System.Windows.Threading.Dispatcher;
using Label = Microsoft.Maui.Controls.Label;

namespace HandlerTests;

[Collection("Shell handlers")]
public class ShellItemsHandlerTests
{
	[Theory]
	[InlineData("Header", false)]
	[InlineData("Footer", false)]
	[InlineData("Item", false)]
	[InlineData("Header", true)]
	[InlineData("Footer", true)]
	[InlineData("Item", true)]
	public void FlyoutTemplates_ContentOnlyMutation_PreservesNativeState(string surface, bool active)
	{
		RunWithFlyoutTemplates((shell, handler, calls, created) =>
		{
			var section = shell.Items[active ? 0 : 1].Items[0];
			var selectedPage = shell.CurrentPage;
			var tabs = shell.CurrentItem.Items.ToArray();
			var before = FlyoutTemplateNative(handler, surface);
			Assert.Contains(created[surface], entry => ReferenceEquals(entry.Handler?.PlatformView, before));
			before.Text = "Locally edited flyout text";
			before.Select(3, 7);
			DrainDispatcher();
			Assert.Equal("Locally edited flyout text", before.Text);
			Assert.Equal(3, before.SelectionStart);
			Assert.Equal(7, before.SelectionLength);
			var factoryCounts = created.ToDictionary(pair => pair.Key, pair => pair.Value.Count);
			var headerTemplate = shell.FlyoutHeaderTemplate;
			var footerTemplate = shell.FlyoutFooterTemplate;
			var itemTemplate = shell.ItemTemplate;
			calls.Clear();

			var replacement = Content("Replacement content");
			section.Items[0] = replacement;
			DrainDispatcher();

			Assert.Same(headerTemplate, shell.FlyoutHeaderTemplate);
			Assert.Same(footerTemplate, shell.FlyoutFooterTemplate);
			Assert.Same(itemTemplate, shell.ItemTemplate);
			Assert.Equal(new[] { nameof(Shell.BackgroundColor), nameof(Shell.FlyoutBackgroundColor),
				nameof(Shell.FlyoutBackground), nameof(Shell.Items) }, calls);
			AssertTabs(handler, tabs);
			AssertCurrentPage(shell, handler);
			if (active)
			{
				Assert.Same(replacement, section.CurrentItem);
				Assert.NotSame(selectedPage, shell.CurrentPage);
				Assert.Equal("Replacement content", shell.CurrentPage.Title);
			}
			else
				Assert.Same(selectedPage, shell.CurrentPage);

			var after = FlyoutTemplateNative(handler, surface);
			Assert.Same(before, after);
			Assert.Equal("Locally edited flyout text", after.Text);
			Assert.Equal(3, after.SelectionStart);
			Assert.Equal(7, after.SelectionLength);
			foreach (var pair in factoryCounts)
				Assert.Equal(pair.Value, created[pair.Key].Count);
		});
	}

	[Theory]
	[InlineData("Header", false)]
	[InlineData("Footer", false)]
	[InlineData("Item", false)]
	[InlineData("Header", true)]
	[InlineData("Footer", true)]
	public void FlyoutTemplates_ChangedTemplateOrData_UpdatesNativeContent(string surface, bool changeData)
	{
		RunWithFlyoutTemplates((shell, handler, calls, created) =>
		{
			var before = FlyoutTemplateNative(handler, surface);
			Assert.Contains(created[surface], entry => ReferenceEquals(entry.Handler?.PlatformView, before));
			Assert.Equal(surface == "Item" ? "Initial item" : $"Initial {surface}", before.Text);
			var originalTemplate = surface == "Header" ? shell.FlyoutHeaderTemplate
				: surface == "Footer" ? shell.FlyoutFooterTemplate : shell.ItemTemplate;
			var replacementEntries = new List<Microsoft.Maui.Controls.Entry>();
			if (changeData)
			{
				if (surface == "Header")
					shell.FlyoutHeader = "Updated data";
				else
					shell.FlyoutFooter = "Updated data";
			}
			else
			{
				var template = new Microsoft.Maui.Controls.DataTemplate(() =>
				{
					var entry = new Microsoft.Maui.Controls.Entry { Text = "Updated template" };
					replacementEntries.Add(entry);
					return entry;
				});
				if (surface == "Header")
					shell.FlyoutHeaderTemplate = template;
				else if (surface == "Footer")
					shell.FlyoutFooterTemplate = template;
				else
					shell.ItemTemplate = template;
			}
			DrainDispatcher();
			calls.Clear();

			// Exercise invalidation through the same deferred Items replay as the preservation cases.
			shell.Items[1].Items[0].Items.Add(Content("Inactive content"));
			DrainDispatcher();

			var after = FlyoutTemplateNative(handler, surface);
			Assert.Equal(changeData ? "Updated data" : "Updated template", after.Text);
			if (changeData)
			{
				Assert.Same(originalTemplate, surface == "Header" ? shell.FlyoutHeaderTemplate : shell.FlyoutFooterTemplate);
				var entry = Assert.Single(created[surface], entry => ReferenceEquals(entry.Handler?.PlatformView, after));
				Assert.Equal("Updated data", entry.BindingContext);
			}
			else
			{
				Assert.NotSame(before, after);
				Assert.Contains(replacementEntries, entry => ReferenceEquals(entry.Handler?.PlatformView, after));
				if (surface == "Item")
				{
					var buttons = Flyout(handler).Children.Cast<System.Windows.Controls.Button>().ToArray();
					Assert.Equal(shell.Items.Count, buttons.Length);
					foreach (var button in buttons)
					{
						var textBox = Assert.IsType<TextBox>(button.Content);
						Assert.Equal("Updated template", textBox.Text);
						Assert.Contains(replacementEntries, entry => ReferenceEquals(entry.Handler?.PlatformView, textBox));
					}
				}
			}
			Assert.Equal(new[] { nameof(Shell.BackgroundColor), nameof(Shell.FlyoutBackgroundColor),
				nameof(Shell.FlyoutBackground), nameof(Shell.Items) }, calls);
			AssertTabs(handler, shell.CurrentItem.Items.ToArray());
			AssertCurrentPage(shell, handler);
		});
	}

	[Fact]
	public void FlyoutTemplates_SelectorResultChanges_ReplacesOnlyAffectedItem()
	{
		RunWithFlyoutTemplates((shell, handler, _, _) =>
		{
			var entries = new List<Microsoft.Maui.Controls.Entry>();
			var first = Template("First selection");
			var second = Template("Second selection");
			var changed = false;
			shell.ItemTemplate = new FlyoutTestSelector(item =>
				changed && ReferenceEquals(item, shell.Items[0]) ? second : first);
			shell.Items[1].Items[0].Items.Add(Content("Initial refresh"));
			DrainDispatcher();
			var original = FlyoutTemplateNative(handler, "Item");
			var originalEntry = Assert.Single(entries, entry => ReferenceEquals(entry.Handler?.PlatformView, original));
			var other = ((System.Windows.Controls.Button)Flyout(handler).Children[1]).Content;
			Assert.Equal("First selection", original.Text);
			Assert.Equal(2, entries.Count);

			changed = true;
			shell.Items[1].Items[0].Items.Add(Content("Resolve changed selector"));
			DrainDispatcher();

			Assert.NotSame(original, FlyoutTemplateNative(handler, "Item"));
			Assert.Equal("Second selection", FlyoutTemplateNative(handler, "Item").Text);
			Assert.Null(originalEntry.Handler);
			Assert.Same(other, ((System.Windows.Controls.Button)Flyout(handler).Children[1]).Content);
			Assert.Equal(3, entries.Count);
			AssertCurrentPage(shell, handler);

			Microsoft.Maui.Controls.DataTemplate Template(string text) => new(() =>
			{
				var entry = new Microsoft.Maui.Controls.Entry { Text = text };
				entries.Add(entry);
				return entry;
			});
		});
	}

	[Fact]
	public void FlyoutTemplates_RemovedItem_DisconnectsAndDoesNotReuseDetachedNativeView()
	{
		RunWithFlyoutTemplates((shell, handler, _, created) =>
		{
			var removed = shell.Items[1];
			var button = (System.Windows.Controls.Button)Flyout(handler).Children[1];
			var native = Assert.IsType<TextBox>(button.Content);
			var entry = Assert.Single(created["Item"], candidate => ReferenceEquals(candidate.Handler?.PlatformView, native));
			shell.Items.Remove(removed);
			DrainDispatcher();

			Assert.Null(entry.Handler);
			Assert.Null(entry.BindingContext);
			Assert.Single(Flyout(handler).Children.Cast<System.Windows.Controls.Button>());
			var selected = shell.CurrentItem;
			button.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
			Assert.Same(selected, shell.CurrentItem);

			shell.Items.Add(removed);
			DrainDispatcher();
			var replacement = Assert.IsType<TextBox>(((System.Windows.Controls.Button)Flyout(handler).Children[1]).Content);
			Assert.NotSame(native, replacement);
			Assert.Contains(created["Item"], candidate => ReferenceEquals(candidate.Handler?.PlatformView, replacement));
			AssertCurrentPage(shell, handler);
		});
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void FlyoutTemplates_RebindOrReconnect_ReleasesPreviousTemplateViews(bool reconnect)
	{
		RunWithFlyoutTemplates((shell, handler, _, created) =>
		{
			var nativeViews = new[] { "Header", "Footer", "Item" }
				.Select(surface => FlyoutTemplateNative(handler, surface)).ToArray();
			var entries = created.Values.SelectMany(value => value).Where(entry => entry.Handler != null).ToArray();
			Assert.Equal(4, entries.Length);
			if (reconnect)
				((IElementHandler)handler).DisconnectHandler();
			var replacement = reconnect ? shell : new Shell
			{
				FlyoutHeader = shell.FlyoutHeader,
				FlyoutFooter = shell.FlyoutFooter,
				FlyoutHeaderTemplate = shell.FlyoutHeaderTemplate,
				FlyoutFooterTemplate = shell.FlyoutFooterTemplate,
				ItemTemplate = shell.ItemTemplate,
				Items = { Item("Replacement shell") },
			};
			handler.SetVirtualView(replacement);
			DrainDispatcher();

			Assert.All(entries, entry =>
			{
				Assert.Null(entry.Handler);
				Assert.Null(entry.BindingContext);
			});
			foreach (var surface in new[] { "Header", "Footer", "Item" })
				Assert.DoesNotContain(FlyoutTemplateNative(handler, surface), nativeViews);
			AssertCurrentPage(replacement, handler);
			if (!reconnect)
			{
				var newHeader = FlyoutTemplateNative(handler, "Header");
				shell.Items.Clear();
				DrainDispatcher();
				Assert.Same(newHeader, FlyoutTemplateNative(handler, "Header"));
				AssertCurrentPage(replacement, handler);
			}
		});
	}

	[Theory]
	[InlineData("Header")]
	[InlineData("Footer")]
	public void FlyoutTemplates_EffectiveBindingContextChanges_UpdatesNativeContent(string surface)
	{
		RunWithFlyoutTemplates((shell, handler, _, created) =>
		{
			if (surface == "Header")
				shell.FlyoutHeader = null;
			else
				shell.FlyoutFooter = null;
			shell.BindingContext = "First context";
			shell.Items[1].Items[0].Items.Add(Content("Initial context"));
			DrainDispatcher();
			Assert.Equal("First context", FlyoutTemplateNative(handler, surface).Text);

			shell.BindingContext = "Second context";
			shell.Items[1].Items[0].Items.Add(Content("New context"));
			DrainDispatcher();
			var native = FlyoutTemplateNative(handler, surface);
			Assert.Equal("Second context", native.Text);
			var entry = Assert.Single(created[surface], candidate => ReferenceEquals(candidate.Handler?.PlatformView, native));
			Assert.Equal("Second context", entry.BindingContext);
			AssertCurrentPage(shell, handler);
		});
	}

	[Fact]
	public void FlyoutTemplates_UnchangedDirectHeader_DoesNotDetachNativeContent()
	{
		RunWithFlyoutTemplates((shell, handler, _, _) =>
		{
			var header = new Microsoft.Maui.Controls.Entry { Text = "Direct header" };
			shell.FlyoutHeaderTemplate = null;
			shell.FlyoutHeader = header;
			shell.Items[1].Items[0].Items.Add(Content("Render direct header"));
			DrainDispatcher();
			var native = FlyoutTemplateNative(handler, "Header");
			Assert.Same(header.Handler?.PlatformView, native);
			native.Text = "Edited direct header";
			native.Select(2, 5);
			var host = (ContentControl)((WGrid)handler.PlatformView.Children[0]).Children[0];
			var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(ContentControl.ContentProperty, typeof(ContentControl));
			var changes = 0;
			EventHandler onChanged = (_, _) => changes++;
			descriptor.AddValueChanged(host, onChanged);
			try
			{
				shell.Items[1].Items[0].Items.Add(Content("Preserve direct header"));
				DrainDispatcher();
				Assert.Same(native, host.Content);
				Assert.Equal("Edited direct header", native.Text);
				Assert.Equal(2, native.SelectionStart);
				Assert.Equal(5, native.SelectionLength);
				Assert.Equal(0, changes);
				AssertCurrentPage(shell, handler);
			}
			finally
			{
				descriptor.RemoveValueChanged(host, onChanged);
				header.Handler?.DisconnectHandler();
				header.Handler = null;
			}
		});
	}

	[Fact]
	public void FlyoutTemplates_SameFooterContext_RefreshesProjectionWithoutReplacingState()
	{
		RunWithFlyoutTemplates((shell, handler, _, _) =>
		{
			var model = new Label { Text = "Original label", FontSize = 16 };
			var labels = new List<Label>();
			var pickers = new List<Microsoft.Maui.Controls.Picker>();
			shell.FlyoutFooter = model;
			shell.FlyoutFooterTemplate = new Microsoft.Maui.Controls.DataTemplate(() =>
			{
				var label = new Label();
				label.SetBinding(Label.TextProperty, nameof(Label.Text));
				label.SetBinding(Label.FontSizeProperty, nameof(Label.FontSize));
				var picker = new Microsoft.Maui.Controls.Picker { Items = { "First", "Second" }, SelectedIndex = 0 };
				labels.Add(label);
				pickers.Add(picker);
				return new VerticalStackLayout { Children = { label, picker } };
			});
			shell.Items[1].Items[0].Items.Add(Content("Render footer projection"));
			DrainDispatcher();
			var host = (ContentControl)((WGrid)handler.PlatformView.Children[0]).Children[2];
			var native = Assert.IsType<System.Windows.Controls.StackPanel>(host.Content);
			var text = Assert.IsType<TextBlock>(native.Children[0]);
			var combo = Assert.IsType<ComboBox>(native.Children[1]);
			Assert.Equal("Original label", text.Text);
			Assert.Equal(16, text.FontSize);
			var shellOwnedRoot = Assert.IsType<VerticalStackLayout>(((IShellController)shell).FlyoutFooter);
			var shellOwnedLabel = Assert.Single(shellOwnedRoot.Children.OfType<Label>());
			var shellOwnedPicker = Assert.Single(shellOwnedRoot.Children.OfType<Microsoft.Maui.Controls.Picker>());
			Assert.Equal(2, labels.Count);
			Assert.Equal(2, pickers.Count);
			Assert.Contains(shellOwnedLabel, labels);
			Assert.Contains(shellOwnedPicker, pickers);
			combo.SelectedIndex = 1;
			DrainDispatcher();
			var projectedPicker = Assert.Single(pickers, picker => picker.SelectedIndex == 1);
			Assert.NotSame(shellOwnedPicker, projectedPicker);
			Assert.Equal(0, shellOwnedPicker.SelectedIndex);
			var projectedRoot = Assert.IsType<VerticalStackLayout>(projectedPicker.Parent);
			Assert.NotSame(shellOwnedRoot, projectedRoot);
			var projectedLabel = Assert.Single(labels, label => ReferenceEquals(label.Parent, projectedRoot));
			Assert.NotSame(shellOwnedLabel, projectedLabel);
			Assert.Same(model, projectedLabel.BindingContext);
			var labelsAfterInitial = labels.ToArray();
			var pickersAfterInitial = pickers.ToArray();

			model.Text = "Changed bound label";
			model.FontSize = 24;
			DrainDispatcher();
			Assert.Equal("Changed bound label", projectedLabel.Text);
			shell.Items[1].Items[0].Items.Add(Content("Refresh same footer context"));
			DrainDispatcher();

			Assert.Same(model, shell.FlyoutFooter);
			Assert.Same(native, host.Content);
			Assert.Same(text, native.Children[0]);
			Assert.Same(combo, native.Children[1]);
			Assert.Equal("Changed bound label", text.Text);
			Assert.Equal(24, text.FontSize);
			Assert.Equal(1, combo.SelectedIndex);
			Assert.Equal(labelsAfterInitial, labels);
			Assert.Equal(pickersAfterInitial, pickers);
			AssertCurrentPage(shell, handler);

			shell.FlyoutFooterTemplate = null;
			shell.FlyoutFooter = null;
			shell.Items[1].Items[0].Items.Add(Content("Release projected footer"));
			DrainDispatcher();
			Assert.Null(host.Content);
			Assert.Null(((IShellController)shell).FlyoutFooter);
			projectedPicker.SelectedIndex = 0;
			DrainDispatcher();
			Assert.Equal(1, combo.SelectedIndex);
			combo.SelectedIndex = -1;
			combo.SelectedIndex = 1;
			Assert.Equal(0, projectedPicker.SelectedIndex);
		});
	}

	[Fact]
	public void FlyoutTemplates_SameHeaderContext_RefreshesImageProjectionInPlace()
	{
		var firstPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wpf-shell-{Guid.NewGuid():N}.png");
		var secondPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wpf-shell-{Guid.NewGuid():N}.png");
		try
		{
			RunWithFlyoutTemplates((shell, handler, _, _) =>
			{
				WriteImage(firstPath, 1);
				WriteImage(secondPath, 2);
				var model = new Microsoft.Maui.Controls.Image
				{
					Source = Microsoft.Maui.Controls.ImageSource.FromFile(firstPath),
					WidthRequest = 32,
					HeightRequest = 24,
				};
				var images = new List<Microsoft.Maui.Controls.Image>();
				shell.FlyoutHeader = model;
				shell.FlyoutHeaderTemplate = new Microsoft.Maui.Controls.DataTemplate(() =>
				{
					var image = new Microsoft.Maui.Controls.Image();
					image.SetBinding(Microsoft.Maui.Controls.Image.SourceProperty, nameof(Microsoft.Maui.Controls.Image.Source));
					image.SetBinding(VisualElement.WidthRequestProperty, nameof(VisualElement.WidthRequest));
					image.SetBinding(VisualElement.HeightRequestProperty, nameof(VisualElement.HeightRequest));
					images.Add(image);
					return image;
				});
				shell.Items[1].Items[0].Items.Add(Content("Render header projection"));
				DrainDispatcher();
				var host = (ContentControl)((WGrid)handler.PlatformView.Children[0]).Children[0];
				var native = Assert.IsType<WGrid>(host.Content);
				var image = Assert.IsType<System.Windows.Controls.Image>(Assert.Single(native.Children.Cast<System.Windows.UIElement>()));
				Assert.Equal(1, Assert.IsAssignableFrom<System.Windows.Media.Imaging.BitmapSource>(image.Source).PixelWidth);
				Assert.Equal(32, image.Width);
				Assert.Equal(24, image.Height);
				var shellOwnedImage = Assert.IsType<Microsoft.Maui.Controls.Image>(((IShellController)shell).FlyoutHeader);
				Assert.Equal(2, images.Count);
				Assert.Contains(shellOwnedImage, images);
				var projectedImage = Assert.Single(images, candidate => !ReferenceEquals(candidate, shellOwnedImage));
				Assert.Same(model, projectedImage.BindingContext);
				Assert.Same(model.Source, projectedImage.Source);
				Assert.Equal(32, projectedImage.WidthRequest);
				Assert.Equal(24, projectedImage.HeightRequest);
				var imagesAfterInitial = images.ToArray();

				model.Source = Microsoft.Maui.Controls.ImageSource.FromFile(secondPath);
				model.WidthRequest = 48;
				model.HeightRequest = 36;
				DrainDispatcher();
				Assert.Same(model.Source, projectedImage.Source);
				shell.Items[1].Items[0].Items.Add(Content("Refresh same header context"));
				DrainDispatcher();

				Assert.Same(model, shell.FlyoutHeader);
				Assert.Same(native, host.Content);
				Assert.Same(image, native.Children[0]);
				Assert.Equal(2, Assert.IsAssignableFrom<System.Windows.Media.Imaging.BitmapSource>(image.Source).PixelWidth);
				Assert.Equal(48, image.Width);
				Assert.Equal(36, image.Height);
				Assert.Equal(36, native.Height);
				Assert.Equal(imagesAfterInitial, images);
				AssertCurrentPage(shell, handler);

				shell.FlyoutHeaderTemplate = null;
				shell.FlyoutHeader = null;
				shell.Items[1].Items[0].Items.Add(Content("Release projected header"));
				DrainDispatcher();
				Assert.Null(host.Content);
				Assert.Null(((IShellController)shell).FlyoutHeader);
			});
		}
		finally
		{
			System.IO.File.Delete(firstPath);
			System.IO.File.Delete(secondPath);
		}

		static void WriteImage(string path, int width)
		{
			var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(width, 1, 96, 96,
				System.Windows.Media.PixelFormats.Bgra32, null, new byte[width * 4], width * 4);
			var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
			encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
			using var stream = System.IO.File.Open(path, System.IO.FileMode.CreateNew);
			encoder.Save(stream);
		}
	}

	sealed class FlyoutTestSelector(Func<object, Microsoft.Maui.Controls.DataTemplate> select)
		: Microsoft.Maui.Controls.DataTemplateSelector
	{
		protected override Microsoft.Maui.Controls.DataTemplate OnSelectTemplate(object item, BindableObject container)
			=> select(item);
	}

	[Theory]
	[InlineData(nameof(Shell.BackgroundColor), false)]
	[InlineData(nameof(Shell.FlyoutBackgroundColor), false)]
	[InlineData(nameof(Shell.FlyoutBackground), false)]
	[InlineData(nameof(Shell.BackgroundColor), true)]
	public void BackgroundAppendToMapping_ItemsOnlyRefresh_PreservesNativeCustomization(string key, bool mutateOnceInCallback)
	{
		var originalMapper = ShellHandler.Mapper;
		var mapper = new PropertyMapper<Shell, ShellHandler>(originalMapper);
		string[] backgroundKeys = [nameof(Shell.BackgroundColor), nameof(Shell.FlyoutBackgroundColor), nameof(Shell.FlyoutBackground)];
		var replayedKeys = new List<string>();
		var unrelatedCalls = 0;
		System.Windows.Media.Brush? customBrush = null;
		Action? mutateItems = null;
		foreach (var backgroundKey in backgroundKeys)
		{
			mapper.AppendToMapping(backgroundKey, (handler, _) =>
			{
				replayedKeys.Add(backgroundKey);
				if (backgroundKey != key)
					return;
				customBrush ??= new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Magenta);
				if (key == nameof(Shell.BackgroundColor))
					handler.PlatformView.SetBarBackground(customBrush);
				else
					handler.PlatformView.SetFlyoutBackground(customBrush);
				var mutation = mutateItems;
				mutateItems = null;
				mutation?.Invoke();
			});
		}
		mapper.AppendToMapping(nameof(Shell.FlyoutWidth), (_, _) => unrelatedCalls++);
		try
		{
			ShellHandler.Mapper = mapper;
			Run((shell, handler) =>
			{
				var item = Item("Initial");
				shell.Items.Add(item);
				shell.BackgroundColor = Microsoft.Maui.Graphics.Colors.Red;
				shell.FlyoutBackgroundColor = Microsoft.Maui.Graphics.Colors.Blue;
				if (key == nameof(Shell.FlyoutBackground))
					shell.FlyoutBackground = new Microsoft.Maui.Controls.SolidColorBrush(Microsoft.Maui.Graphics.Colors.Green);
				DrainDispatcher();

				// Establish a valid customization without relying on initial mapper enumeration order.
				handler.UpdateValue(key);
				DrainDispatcher();
				Assert.NotNull(customBrush);
				Assert.Same(customBrush, NativeBackground(handler, key));
				Assert.Contains(key, replayedKeys);
				var background = shell.BackgroundColor;
				var flyoutColor = shell.FlyoutBackgroundColor;
				var flyoutBrush = shell.FlyoutBackground;
				var changedBackgrounds = new List<string>();
				System.ComponentModel.PropertyChangedEventHandler onChanged = (_, args) =>
				{
					if (args.PropertyName is string name && backgroundKeys.Contains(name))
						changedBackgrounds.Add(name);
				};
				shell.PropertyChanged += onChanged;
				try
				{
					replayedKeys.Clear();
					var previousUnrelatedCalls = unrelatedCalls;
					if (mutateOnceInCallback)
						mutateItems = () => item.Items.Add(Section("Added by callback"));
					item.Items.Add(Section("Added by test"));
					DrainDispatcher();

					Assert.Same(background, shell.BackgroundColor);
					Assert.Same(flyoutColor, shell.FlyoutBackgroundColor);
					Assert.Same(flyoutBrush, shell.FlyoutBackground);
					Assert.Empty(changedBackgrounds);
					Assert.Equal(previousUnrelatedCalls, unrelatedCalls);
					Assert.Same(customBrush, NativeBackground(handler, key));
					Assert.Equal(mutateOnceInCallback ? backgroundKeys.Concat(backgroundKeys) : backgroundKeys, replayedKeys);
					Assert.Null(mutateItems);
					Assert.Equal(mutateOnceInCallback ? 3 : 2, item.Items.Count);
					AssertTabs(handler, item.Items.ToArray());
					AssertCurrentPage(shell, handler);
				}
				finally
				{
					shell.PropertyChanged -= onChanged;
				}
			});
		}
		finally
		{
			ShellHandler.Mapper = originalMapper;
		}
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	public void BackgroundMappings_ItemsOnlyRefresh_PreservesUncustomizedColorsAndPrecedence(bool themeDefaults, bool useFlyoutBrush)
	{
		Run((shell, handler) =>
		{
			var item = Item("Initial");
			shell.Items.Add(item);
			if (!themeDefaults)
			{
				shell.BackgroundColor = Microsoft.Maui.Graphics.Colors.Red;
				shell.FlyoutBackgroundColor = Microsoft.Maui.Graphics.Colors.Blue;
			}
			if (useFlyoutBrush)
				shell.FlyoutBackground = new Microsoft.Maui.Controls.SolidColorBrush(Microsoft.Maui.Graphics.Colors.Green);
			if (themeDefaults)
				shell.FlyoutBackground = null;
			DrainDispatcher();
			handler.UpdateValue(nameof(Shell.BackgroundColor));
			handler.UpdateValue(nameof(Shell.FlyoutBackgroundColor));
			handler.UpdateValue(nameof(Shell.FlyoutBackground));
			var toolbarColor = Assert.IsType<System.Windows.Media.SolidColorBrush>(NativeBackground(handler, nameof(Shell.BackgroundColor))).Color;
			var flyoutColor = Assert.IsType<System.Windows.Media.SolidColorBrush>(NativeBackground(handler, nameof(Shell.FlyoutBackground))).Color;
			if (themeDefaults)
			{
				Assert.Null(shell.BackgroundColor);
				Assert.Null(shell.FlyoutBackgroundColor);
				Assert.Null(shell.FlyoutBackground);
			}
			else
			{
				Assert.Equal(System.Windows.Media.Colors.Red, toolbarColor);
				Assert.Equal(useFlyoutBrush ? System.Windows.Media.Colors.Green : System.Windows.Media.Colors.Blue, flyoutColor);
			}

			item.Items.Add(Section("Added"));
			DrainDispatcher();
			Assert.Equal(toolbarColor, Assert.IsType<System.Windows.Media.SolidColorBrush>(NativeBackground(handler, nameof(Shell.BackgroundColor))).Color);
			Assert.Equal(flyoutColor, Assert.IsType<System.Windows.Media.SolidColorBrush>(NativeBackground(handler, nameof(Shell.FlyoutBackground))).Color);
			AssertTabs(handler, item.Items.ToArray());
			AssertCurrentPage(shell, handler);
		});
	}

	[Fact]
	public void RuntimeWindow_ItemsAppendToMapping_CustomizesDeferredCollectionRefresh()
	{
		var originalMapper = ShellHandler.Mapper;
		var mapper = new PropertyMapper<Shell, ShellHandler>(originalMapper);
		var calls = 0;
		mapper.AppendToMapping(nameof(Shell.Items), (handler, _) =>
		{
			calls++;
			foreach (var tab in Tabs(handler).Items.Cast<TabItem>())
				tab.Header = $"Custom {Assert.IsAssignableFrom<ShellSection>(tab.Tag).Title}";
		});
		try
		{
			ShellHandler.Mapper = mapper;
			Run((shell, handler) =>
			{
				var window = new System.Windows.Window
				{
					Content = handler.PlatformView,
					Width = 800,
					Height = 600,
					Left = -20000,
					Top = -20000,
					ShowActivated = false,
					ShowInTaskbar = false,
				};
				try
				{
					window.Show();
					var first = Section("First");
					var second = Section("Second");
					var item = new TabBar { Items = { first, second } };
					shell.Items.Add(item);
					DrainDispatcher();
					var previousCalls = calls;

					var added = Section("Added");
					item.Items.Add(added);
					AssertCustomized("09-mapper-add", first, second, added);

					var replacement = Section("Replacement");
					item.Items[2] = replacement;
					AssertCustomized("10-mapper-replace", first, second, replacement);

					item.Items.Remove(replacement);
					AssertCustomized("11-mapper-remove", first, second);

					item.Items.Clear();
					item.Items.Add(first);
					item.Items.Add(second);
					AssertCustomized("12-mapper-reset-add", first, second);

					void AssertCustomized(string name, params ShellSection[] sections)
					{
						DrainDispatcher();
						Assert.True(calls > previousCalls, "Deferred collection refresh bypassed the Items mapper customization.");
						previousCalls = calls;
						AssertTabs(handler, sections);
						Assert.Equal(sections.Select(section => $"Custom {section.Title}"),
							Tabs(handler).Items.Cast<TabItem>().Select(tab => Assert.IsType<string>(tab.Header)));
						AssertCurrentPage(shell, handler);
						window.UpdateLayout();
						Assert.True(handler.PlatformView.ActualWidth > 0);
						Assert.True(handler.PlatformView.ActualHeight > 0);
						SaveRuntimeEvidence(handler, name);
					}
				}
				finally
				{
					window.Close();
				}
			});
		}
		finally
		{
			ShellHandler.Mapper = originalMapper;
		}
	}

	[Fact]
	public void RootSubscriptions_AttachRebindAndReconnect_RegisterEachCallbackOnce()
	{
		Run((original, handler) =>
		{
			original.Items.Add(Item("Original"));
			DrainDispatcher();
			AssertRootSubscriptions(original, handler, connected: true);
			handler.SetVirtualView(original);
			handler.SetVirtualView(original);
			AssertRootSubscriptions(original, handler, connected: true);

			var replacement = new Shell { Items = { Item("Replacement") } };
			handler.SetVirtualView(replacement);
			DrainDispatcher();
			AssertRootSubscriptions(original, handler, connected: false);
			AssertRootSubscriptions(replacement, handler, connected: true);
			AssertCurrentPage(replacement, handler);

			((IElementHandler)handler).DisconnectHandler();
			AssertRootSubscriptions(original, handler, connected: false);
			AssertRootSubscriptions(replacement, handler, connected: false);

			handler.SetVirtualView(replacement);
			handler.SetVirtualView(replacement);
			DrainDispatcher();
			AssertRootSubscriptions(replacement, handler, connected: true);
			replacement.Items.Add(Item("After reconnect"));
			DrainDispatcher();
			AssertFlyout(handler, replacement.Items.ToArray());
			AssertCurrentPage(replacement, handler);

			((IElementHandler)handler).DisconnectHandler();
			AssertRootSubscriptions(replacement, handler, connected: false);
		});
	}

	[Fact]
	public void RuntimeWindow_DynamicHierarchy_RendersAddRemoveReplaceAndReset()
	{
		Run((shell, handler) =>
		{
			shell.FlyoutBehavior = FlyoutBehavior.Locked;
			var window = new System.Windows.Window
			{
				Title = "Shell dynamic items regression",
				Content = handler.PlatformView,
				Width = 800,
				Height = 600,
				Left = -20000,
				Top = -20000,
				ShowActivated = false,
				ShowInTaskbar = false,
			};
			try
			{
				window.Show();
				var first = Item("First");
				var second = Item("Second");
				shell.Items.Add(first);
				shell.Items.Add(second);
				Render("01-flyout-add", first, second);
				AssertCurrentPage(shell, handler);

				shell.Items.Remove(second);
				Render("02-flyout-remove", first);
				AssertCurrentPage(shell, handler);

				var replacement = Item("Replacement");
				shell.Items[0] = replacement;
				Render("03-flyout-replace", replacement);
				AssertCurrentPage(shell, handler);

				shell.Items.Clear();
				DrainDispatcher();
				Assert.Null(ContentHost(handler).Content);
				Render("04-reset");

				var a = Section("A");
				var b = Section("B");
				var tabs = new TabBar { Items = { a, b } };
				shell.Items.Add(tabs);
				Render("05-tabbar-add");
				AssertTabs(handler, a, b);
				AssertCurrentPage(shell, handler);

				var c = Section("C");
				tabs.Items[1] = c;
				Render("06-tab-replace");
				AssertTabs(handler, a, c);

				tabs.Items.Remove(c);
				Render("07-tab-remove");
				Assert.Equal(System.Windows.Visibility.Collapsed, Tabs(handler).Visibility);

				tabs.Items.Clear();
				Render("08-tabs-reset");
				Assert.Null(ContentHost(handler).Content);
			}
			finally
			{
				window.Close();
			}

			void Render(string name, params ShellItem[] items)
			{
				DrainDispatcher();
				window.UpdateLayout();
				AssertFlyout(handler, items);
				Assert.True(handler.PlatformView.ActualWidth > 0);
				Assert.True(handler.PlatformView.ActualHeight > 0);
				SaveRuntimeEvidence(handler, name);
			}
		});
	}

	[Fact]
	public void DynamicSections_SelectionChanges_KeepTabsAndPageSynchronized()
	{
		Run((shell, handler) =>
		{
			var item = Item("Initial");
			item.Items.Add(Section("Second"));
			shell.Items.Add(item);
			DrainDispatcher();

			var inserted = Section("Inserted");
			item.Items.Add(inserted);
			item.CurrentItem = inserted;
			DrainDispatcher();
			AssertTabs(handler, item.Items.ToArray());
			Assert.Same(inserted, Assert.IsType<TabItem>(Tabs(handler).SelectedItem).Tag);
			AssertCurrentPage(shell, handler);

			var content = Content("Inserted content");
			inserted.Items.Add(content);
			inserted.CurrentItem = content;
			DrainDispatcher();
			Assert.Same(((IShellContentController)content).Page, shell.CurrentPage);
			AssertCurrentPage(shell, handler);

			item.Items.Remove(inserted);
			DrainDispatcher();
			AssertTabs(handler, item.Items.ToArray());
			Assert.Same(item.CurrentItem, Assert.IsType<TabItem>(Tabs(handler).SelectedItem).Tag);
			AssertCurrentPage(shell, handler);

			var tab = Tabs(handler).SelectedItem;
			inserted.CurrentItem = inserted.Items[0];
			inserted.Items.Add(Content("Detached"));
			DrainDispatcher();
			Assert.Same(tab, Tabs(handler).SelectedItem);
		});
	}

	[Fact]
	public void InactiveSections_Changed_KeepActiveTabsSelectionAndPage()
	{
		Run((shell, handler) =>
		{
			var active = Item("Active");
			var selected = Section("Selected");
			active.Items.Add(selected);
			active.CurrentItem = selected;
			var inactive = Item("Inactive");
			inactive.Items.Add(Section("Other"));
			shell.Items.Add(active);
			shell.Items.Add(inactive);
			DrainDispatcher();
			var page = shell.CurrentPage;

			inactive.Items.Add(Section("Added while inactive"));
			DrainDispatcher();

			AssertFlyout(handler, active, inactive);
			AssertTabs(handler, active.Items.ToArray());
			Assert.Same(selected, Assert.IsType<TabItem>(Tabs(handler).SelectedItem).Tag);
			Assert.Same(page, shell.CurrentPage);
			AssertCurrentPage(shell, handler);
			Assert.All(inactive.Items, section =>
				Assert.Null(((IShellContentController)section.Items[0]).Page));

			shell.CurrentItem = inactive;
			DrainDispatcher();
			AssertTabs(handler, inactive.Items.ToArray());
			Assert.Same(inactive.CurrentItem, Assert.IsType<TabItem>(Tabs(handler).SelectedItem).Tag);
			AssertCurrentPage(shell, handler);
		});
	}

	[Fact]
	public void Items_AddRemoveReplaceReorderReset_UpdatesNativeFlyoutAndPage()
	{
		Run((shell, handler) =>
		{
			var first = Item("First");
			shell.Items.Add(first);
			DrainDispatcher();
			AssertFlyout(handler, first);
			AssertCurrentPage(shell, handler);

			var second = Item("Second");
			shell.Items.Add(second);
			DrainDispatcher();
			AssertFlyout(handler, first, second);

			shell.Items.Remove(second);
			shell.Items.Insert(0, second);
			DrainDispatcher();
			AssertFlyout(handler, second, first);

			var replacement = Item("Replacement");
			shell.Items[0] = replacement;
			DrainDispatcher();
			AssertFlyout(handler, replacement, first);

			shell.Items.Remove(first);
			DrainDispatcher();
			AssertFlyout(handler, replacement);
			AssertCurrentPage(shell, handler);

			shell.Items.Clear();
			DrainDispatcher();
			AssertFlyout(handler);
			Assert.Null(ContentHost(handler).Content);
			Assert.Equal(System.Windows.Visibility.Collapsed, Tabs(handler).Visibility);
		});
	}

	[Fact]
	public void Items_ClearThenAddTabBar_ReplacesFlyoutWithTabsWithoutCreatingInactivePages()
	{
		Run((shell, handler) =>
		{
			shell.Items.Add(Item("Old A"));
			shell.Items.Add(Item("Old B"));
			DrainDispatcher();
			var inactiveCreated = 0;
			var first = Section("A");
			var second = new Tab
			{
				Title = "B",
				Items = { new ShellContent { ContentTemplate = new DataTemplate(() =>
				{
					inactiveCreated++;
					return new ContentPage();
				}) } },
			};
			shell.Items.Clear();
			shell.Items.Add(new TabBar { Items = { first, second } });
			DrainDispatcher();

			AssertFlyout(handler);
			AssertTabs(handler, first, second);
			AssertCurrentPage(shell, handler);
			Assert.Equal(0, inactiveCreated);
		});
	}

	[Fact]
	public void Sections_AddRemoveReplaceReorderReset_UpdatesNativeTabs()
	{
		Run((shell, handler) =>
		{
			var item = Item("Item");
			shell.Items.Add(item);
			DrainDispatcher();
			var first = item.Items[0];
			var second = Section("Second");
			item.Items.Add(second);
			DrainDispatcher();
			AssertTabs(handler, first, second);

			item.Items.Remove(second);
			item.Items.Insert(0, second);
			DrainDispatcher();
			AssertTabs(handler, second, first);

			var replacement = Section("Replacement");
			item.Items[0] = replacement;
			DrainDispatcher();
			AssertTabs(handler, replacement, first);

			item.Items.Remove(first);
			DrainDispatcher();
			Assert.Equal(System.Windows.Visibility.Collapsed, Tabs(handler).Visibility);
			AssertCurrentPage(shell, handler);

			item.Items.Clear();
			DrainDispatcher();
			Assert.Empty(Tabs(handler).Items);
			Assert.Null(ContentHost(handler).Content);

			item.Items.Add(Section("After reset"));
			DrainDispatcher();
			AssertCurrentPage(shell, handler);
		});
	}

	[Fact]
	public void Contents_ReplaceRemoveResetAdd_UpdatesVisiblePage()
	{
		Run((shell, handler) =>
		{
			var item = Item("Item");
			shell.Items.Add(item);
			DrainDispatcher();
			var section = item.Items[0];
			var replacement = Content("Replacement");
			section.Items[0] = replacement;
			DrainDispatcher();
			Assert.Same(((IShellContentController)replacement).Page, shell.CurrentPage);
			AssertCurrentPage(shell, handler);

			var next = Content("Next");
			section.Items.Add(next);
			section.Items.Remove(replacement);
			DrainDispatcher();
			Assert.Same(((IShellContentController)next).Page, shell.CurrentPage);
			AssertCurrentPage(shell, handler);

			section.Items.Clear();
			DrainDispatcher();
			Assert.Null(ContentHost(handler).Content);
			section.Items.Add(Content("After reset"));
			DrainDispatcher();
			AssertCurrentPage(shell, handler);
		});
	}

	[Fact]
	public void DetachedCollections_AndDisconnectedHandler_DoNotRebuildNativeItems()
	{
		Run((shell, handler) =>
		{
			var removed = Item("Removed");
			shell.Items.Add(removed);
			shell.Items.Add(Item("Retained"));
			DrainDispatcher();
			var removedSection = removed.Items[0];
			shell.Items.Remove(removed);
			DrainDispatcher();
			var button = Assert.Single(Flyout(handler).Children.Cast<System.Windows.Controls.Button>());
			removed.Items.Add(Section("Detached"));
			removedSection.Items.Add(Content("Detached content"));
			DrainDispatcher();
			Assert.Same(button, Assert.Single(Flyout(handler).Children.Cast<System.Windows.Controls.Button>()));

			var flyout = Flyout(handler);
			shell.Items[0].Items.Add(Section("Pending"));
			((IElementHandler)handler).DisconnectHandler();
			shell.Items.Add(Item("Disconnected"));
			DrainDispatcher();
			Assert.Same(button, Assert.Single(flyout.Children.Cast<System.Windows.Controls.Button>()));
		});
	}

	[Fact]
	public void SetVirtualView_RebindsCollectionsAndCancelsOldPendingRefresh()
	{
		Run((oldShell, handler) =>
		{
			oldShell.Items.Add(Item("Old"));
			var shell = new Shell { Items = { Item("New") } };
			handler.SetVirtualView(shell);
			DrainDispatcher();
			AssertFlyout(handler, shell.Items[0]);
			var button = Assert.Single(Flyout(handler).Children.Cast<System.Windows.Controls.Button>());
			oldShell.Items.Add(Item("Detached"));
			DrainDispatcher();
			Assert.Same(button, Assert.Single(Flyout(handler).Children.Cast<System.Windows.Controls.Button>()));
			shell.Items.Add(Item("Added"));
			DrainDispatcher();
			AssertFlyout(handler, shell.Items.ToArray());
			AssertCurrentPage(shell, handler);
		});
	}

	static ShellItem Item(string title) => new FlyoutItem { Title = title, Items = { Section(title) } };
	static void RunWithFlyoutTemplates(Action<Shell, ShellHandler, List<string>,
		Dictionary<string, List<Microsoft.Maui.Controls.Entry>>> test)
	{
		var originalMapper = ShellHandler.Mapper;
		var mapper = new PropertyMapper<Shell, ShellHandler>(originalMapper);
		var calls = new List<string>();
		foreach (var key in new[] { nameof(Shell.Items), nameof(Shell.BackgroundColor),
			nameof(Shell.FlyoutBackgroundColor), nameof(Shell.FlyoutBackground), nameof(Shell.FlyoutWidth) })
			mapper.AppendToMapping(key, (_, _) => calls.Add(key));
		try
		{
			ShellHandler.Mapper = mapper;
			Run((shell, handler) =>
			{
				var created = new Dictionary<string, List<Microsoft.Maui.Controls.Entry>>();
				shell.FlyoutBehavior = FlyoutBehavior.Locked;
				shell.FlyoutHeader = "Initial Header";
				shell.FlyoutFooter = "Initial Footer";
				shell.FlyoutHeaderTemplate = Template("Header");
				shell.FlyoutFooterTemplate = Template("Footer");
				shell.ItemTemplate = Template("Item");
				var active = Item("Active");
				active.Items.Add(Section("Other tab"));
				shell.Items.Add(active);
				shell.Items.Add(Item("Inactive"));
				shell.CurrentItem = active;
				DrainDispatcher();
				var window = new System.Windows.Window
				{
					Content = handler.PlatformView,
					Width = 800,
					Height = 600,
					Left = -20000,
					Top = -20000,
					ShowActivated = false,
					ShowInTaskbar = false,
				};
				try
				{
					window.Show();
					DrainDispatcher();
					window.UpdateLayout();
					Assert.True(handler.PlatformView.ActualWidth > 0);
					Assert.True(handler.PlatformView.ActualHeight > 0);
					AssertCurrentPage(shell, handler);
					foreach (var surface in created.Keys)
					{
						var native = FlyoutTemplateNative(handler, surface);
						Assert.True(native.ActualWidth > 0);
						Assert.True(native.ActualHeight > 0);
						Assert.Equal(surface == "Item" ? "Initial item" : $"Initial {surface}", native.Text);
						Assert.Contains(created[surface], entry => ReferenceEquals(entry.Handler?.PlatformView, native));
					}
					test(shell, handler, calls, created);
				}
				finally
				{
					window.Close();
				}

				Microsoft.Maui.Controls.DataTemplate Template(string surface)
				{
					var entries = new List<Microsoft.Maui.Controls.Entry>();
					created.Add(surface, entries);
					return new Microsoft.Maui.Controls.DataTemplate(() =>
					{
						var entry = new Microsoft.Maui.Controls.Entry { Text = "Initial item" };
						if (surface != "Item")
							entry.SetBinding(Microsoft.Maui.Controls.Entry.TextProperty,
								new Binding(".", mode: BindingMode.OneWay));
						entries.Add(entry);
						return entry;
					});
				}
			});
		}
		finally
		{
			ShellHandler.Mapper = originalMapper;
		}
	}

	static TextBox FlyoutTemplateNative(ShellHandler handler, string surface)
	{
		if (surface == "Item")
			return Assert.IsType<TextBox>(((System.Windows.Controls.Button)Flyout(handler).Children[0]).Content);
		var panel = (WGrid)handler.PlatformView.Children[0];
		return Assert.IsType<TextBox>(Assert.IsType<ContentControl>(panel.Children[surface == "Header" ? 0 : 2]).Content);
	}

	static ShellSection Section(string title) => new Tab { Title = title, Items = { Content(title) } };
	static ShellContent Content(string title) => new()
	{
		Title = title,
		ContentTemplate = new DataTemplate(() => new ContentPage
		{
			Title = title,
			BackgroundColor = Microsoft.Maui.Graphics.Colors.White,
			Content = new Label { Text = title },
		}),
	};

	static WGrid MainGrid(ShellHandler handler) => (WGrid)handler.PlatformView.Children[1];
	static System.Windows.Media.Brush NativeBackground(ShellHandler handler, string key)
		=> key == nameof(Shell.BackgroundColor)
			? MainGrid(handler).Children.OfType<DockPanel>().Single().Background
			: ((WGrid)handler.PlatformView.Children[0]).Background;
	static TabControl Tabs(ShellHandler handler) => MainGrid(handler).Children.OfType<TabControl>().Single();
	static ContentControl ContentHost(ShellHandler handler) => MainGrid(handler).Children.OfType<ContentControl>().Single();
	static System.Windows.Controls.StackPanel Flyout(ShellHandler handler)
		=> (System.Windows.Controls.StackPanel)((System.Windows.Controls.ScrollViewer)
			((WGrid)handler.PlatformView.Children[0]).Children[1]).Content;

	static void AssertFlyout(ShellHandler handler, params ShellItem[] items)
		=> Assert.Equal(items, Flyout(handler).Children.Cast<System.Windows.Controls.Button>().Select(button => button.Tag));

	static void AssertTabs(ShellHandler handler, params ShellSection[] sections)
	{
		Assert.Equal(sections, Tabs(handler).Items.Cast<TabItem>().Select(tab => tab.Tag));
		Assert.Equal(System.Windows.Visibility.Visible, Tabs(handler).Visibility);
	}

	static void AssertCurrentPage(Shell shell, ShellHandler handler)
	{
		Assert.NotNull(shell.CurrentPage);
		Assert.NotNull(shell.CurrentPage.Handler?.PlatformView);
		Assert.Same(shell.CurrentPage.Handler.PlatformView, ContentHost(handler).Content);
	}

	static void AssertRootSubscriptions(Shell shell, ShellHandler handler, bool connected)
	{
		AssertCallbacks(typeof(Shell), nameof(Shell.Navigated), "OnShellNavigated");
		AssertCallbacks(typeof(Shell), nameof(Shell.Navigating), "OnShellNavigating");
		AssertCallbacks(typeof(BindableObject), nameof(Shell.PropertyChanged),
			"OnSelectionShellPropertyChanged", "OnShellPropertyChanged");

		void AssertCallbacks(Type declaringType, string eventName, params string[] expected)
		{
			// Count registrations, not coalesced renders, so duplicate subscriptions cannot hide.
			var field = declaringType.GetField(eventName,
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			Assert.NotNull(field);
			var callbacks = (field.GetValue(shell) as Delegate)?.GetInvocationList() ?? Array.Empty<Delegate>();
			var owned = callbacks.Where(callback => ReferenceEquals(callback.Target, handler) &&
				callback.Method.DeclaringType == typeof(ShellHandler))
				.Select(callback => callback.Method.Name).OrderBy(name => name).ToArray();
			Assert.Equal(connected ? expected.OrderBy(name => name).ToArray() : Array.Empty<string>(), owned);
		}
	}

	static void SaveRuntimeEvidence(ShellHandler handler, string name)
	{
		var directory = Environment.GetEnvironmentVariable("SHELL_ITEMS_EVIDENCE_DIRECTORY");
		if (string.IsNullOrEmpty(directory))
			return;
		System.IO.Directory.CreateDirectory(directory);
		var image = new System.Windows.Media.Imaging.RenderTargetBitmap(
			(int)handler.PlatformView.ActualWidth, (int)handler.PlatformView.ActualHeight,
			96, 96, System.Windows.Media.PixelFormats.Pbgra32);
		image.Render(handler.PlatformView);
		var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
		encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
		using var file = System.IO.File.Create(System.IO.Path.Combine(directory, name + ".png"));
		encoder.Save(file);
	}

	static void DrainDispatcher()
	{
		var frame = new DispatcherFrame();
		Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
			new Action(() => frame.Continue = false));
		Dispatcher.PushFrame(frame);
	}

	static void Run(Action<Shell, ShellHandler> test)
	{
		Exception? failure = null;
		var thread = new Thread(() =>
		{
			try
			{
				_ = Dispatcher.CurrentDispatcher;
				DispatcherProvider.SetCurrent(new WPFDispatcherProvider());
				using var app = MauiApp.CreateBuilder().UseMauiAppWPF<Application>().Build();
				var handler = new ShellHandler();
				handler.SetMauiContext(new WPFMauiContext(app.Services));
				var shell = new Shell();
				try
				{
					handler.SetVirtualView(shell);
					DrainDispatcher();
					test(shell, handler);
				}
				finally
				{
					((IElementHandler)handler).DisconnectHandler();
				}
			}
			catch (Exception exception)
			{
				failure = exception;
			}
			finally
			{
				Dispatcher.CurrentDispatcher.InvokeShutdown();
			}
		}) { IsBackground = true };
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "The STA test did not finish within the timeout.");
		if (failure != null)
			System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
	}
}
