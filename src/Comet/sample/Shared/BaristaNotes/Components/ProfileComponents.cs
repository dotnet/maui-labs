#nullable enable
using System;
using System.IO;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using CometSamples.BaristaNotes.Styles;
using CometBaristaNotes.Services;

namespace CometSamples.BaristaNotes.Components;

public sealed class ProfileAvatar : View
{
    readonly string? _path;
    readonly float _size;
    readonly string _automationId;
    readonly bool _highlighted;

    public ProfileAvatar(
        string? path,
        float size,
        string automationId,
        bool highlighted = false)
    {
        _path = path;
        _size = size;
        _automationId = automationId;
        _highlighted = highlighted;
    }

    [Body]
    View body()
    {
        var hasImage = !string.IsNullOrWhiteSpace(_path) && File.Exists(_path);
        View avatar = hasImage
            ? new Image(_path!)
                .Aspect(Aspect.AspectFill)
                .AutomationId($"{_automationId}_image")
            : new Grid
            {
                new Text(CoffeeIcons.Person)
                    .FontFamily(CoffeeIcons.FontFamily)
                    .FontSize(_size * .5)
                    .Color(CoffeeTheme.TextMuted)
                    .Center(),
            }
            .Background(CoffeeTheme.SurfaceVariant)
            .AutomationId($"{_automationId}_placeholder");

        return avatar
            .Frame(width: _size, height: _size)
            .Border(
                _highlighted ? 3 : 1,
                _highlighted
                    ? CoffeeTheme.PrimaryColor
                    : CoffeeTheme.TextMuted.WithAlpha(.4f))
            .ClipShape(new Ellipse())
            .FlexShrink(0)
            .AutomationId(_automationId);
    }
}

public sealed class ProfileImagePicker : View
{
    readonly string? _path;
    readonly float _size;
    readonly Signal<bool> _isLoading;
    readonly Action _changePhoto;
    readonly Action _removePhoto;
    readonly string? _statusMessage;
    readonly bool _isEnabled;

    public ProfileImagePicker(
        string? path,
        float size,
        Signal<bool> isLoading,
        Action changePhoto,
        Action removePhoto,
        string? statusMessage = null,
        bool isEnabled = true)
    {
        _path = path;
        _size = size;
        _isLoading = isLoading;
        _changePhoto = changePhoto;
        _removePhoto = removePhoto;
        _statusMessage = statusMessage;
        _isEnabled = isEnabled;
    }

    [Body]
    View body()
    {
        var actions = new HStack(spacing: 10)
        {
            new Button("Change Photo", _changePhoto)
                .FontFamily("Manrope")
                .FontSize(14)
                .MaxLines(1)
                .LineBreakMode(LineBreakMode.NoWrap)
                .Color(CoffeeTheme.Light.OnPrimary)
                .Background(CoffeeTheme.Light.Primary)
                .CornerRadius(BaristaSourceVisualContract.SourceButtonRadius)
                .Padding(new Thickness(
                    BaristaSourceVisualContract.SourceButtonHorizontalPadding,
                    BaristaSourceVisualContract.SourceButtonVerticalPadding))
                .MinimumWidth(132)
                .MinimumHeight(BaristaSourceVisualContract.SourceButtonMinimumHeight)
                .IsEnabled(_isEnabled)
                .AutomationId("ChangePhotoButton"),
        };
        if (!string.IsNullOrWhiteSpace(_path))
        {
            actions.Add(new Button("Remove", _removePhoto)
                .FontFamily("Manrope")
                .FontSize(14)
                .MaxLines(1)
                .LineBreakMode(LineBreakMode.NoWrap)
                .Color(CoffeeTheme.Light.OnPrimary)
                .Background(CoffeeTheme.Light.Primary)
                .CornerRadius(BaristaSourceVisualContract.SourceButtonRadius)
                .Padding(new Thickness(
                    BaristaSourceVisualContract.SourceButtonHorizontalPadding,
                    BaristaSourceVisualContract.SourceButtonVerticalPadding))
                .MinimumWidth(96)
                .MinimumHeight(BaristaSourceVisualContract.SourceButtonMinimumHeight)
                .IsEnabled(_isEnabled)
                .AutomationId("RemovePhotoButton"));
        }

        var content = new VStack(spacing: 10)
        {
            new ProfileAvatar(_path, _size, "ProfileAvatar"),
            actions.Center(),
        };

        if (_isLoading.Value)
        {
            content.Add(new ActivityIndicator()
                .AutomationId("ImageLoadingIndicator"));
        }
        if (!string.IsNullOrWhiteSpace(_statusMessage))
        {
            content.Add(new Text(_statusMessage!)
                .FontSize(13)
                .SecondaryText()
                .AutomationId("profile_photo_status"));
        }

        return content.Center().AutomationId("ProfileImagePicker");
    }
}

public sealed record ProfileFixedAction(
    string AutomationId,
    string Label,
    Action Invoke,
    bool inverted = false,
    bool danger = false,
    bool enabled = true);

public sealed class ProfileFixedActionRow : View
{
    readonly string _automationId;
    readonly ProfileFixedAction[] _actions;

    public ProfileFixedActionRow(
        string automationId,
        params ProfileFixedAction[] actions)
    {
        _automationId = automationId;
        _actions = actions;
    }

    [Body]
    View body()
    {
        var columns = new object[_actions.Length];
        Array.Fill(columns, "*");
        var row = new Grid(
            columns: columns,
            rows: new object[] { "Auto" },
            columnSpacing: CoffeeSpacing.Divider)
            .Background(CoffeeTheme.OutlineColor)
            .AutomationId(_automationId);
        var padding = BaristaSafeAreaLayout.ActionPadding(this);

        for (var index = 0; index < _actions.Length; index++)
        {
            var action = _actions[index];
            var background = action.danger
                ? CoffeeTheme.Error
                : action.inverted
                    ? CoffeeTheme.TextPrimary
                    : CoffeeTheme.SurfaceColor;
            var foreground = action.danger || action.inverted
                ? CoffeeTheme.SurfaceColor
                : CoffeeTheme.TextPrimary;

            row.Add(new Button(action.Label, action.Invoke)
                .FontFamily("ManropeSemibold")
                .FontSize(18)
                .Color(foreground)
                .Padding(padding)
                .MinimumHeight(CoffeeSpacing.ActionRowHeight)
                .Background(background)
                .CornerRadius(0)
                .IsEnabled(action.enabled)
                .InputTransparent(!action.enabled)
                .AutomationId(action.AutomationId)
                .Cell(column: index));
        }

        return row;
    }
}

public sealed class ProfileSuccessToast : View
{
    readonly string _message;

    public ProfileSuccessToast(string message) =>
        _message = string.IsNullOrWhiteSpace(message)
            ? throw new ArgumentException("A visible feedback message is required.", nameof(message))
            : message;

    [Body]
    View body()
    {
        var toast = new Grid(
            columns: new object[] { 32, "*" },
            rows: new object[] { ProfileFeedbackContract.Height })
        {
            new Text(CoffeeIcons.Check)
                .FontFamily(CoffeeIcons.FontFamily)
                .FontSize(20)
                .Color(Color.FromArgb("#7CFC00"))
                .Center()
                .Cell(column: 0),
            new Text(_message)
                .FontFamily("ManropeSemibold")
                .FontSize(14)
                .Color(Colors.White)
                .Center()
                .Cell(column: 1),
        }
        .Padding(new Thickness(CoffeeSpacing.M, 0))
        .Frame(height: ProfileFeedbackContract.Height)
        .Background(Color.FromArgb("#2D5016"))
        .AutomationId("profile_success_feedback");

        return toast;
    }
}

public sealed class ProfilePhotoSourceOverlay : View
{
    readonly Signal<bool> _isOpen;
    readonly Action _camera;
    readonly Action _gallery;
    readonly bool _isEnabled;

    public ProfilePhotoSourceOverlay(
        Signal<bool> isOpen,
        Action camera,
        Action gallery,
        bool isEnabled = true)
    {
        _isOpen = isOpen;
        _camera = camera;
        _gallery = gallery;
        _isEnabled = isEnabled;
    }

    [Body]
    View body()
        => new AlertDialog(
            _isOpen,
            text: new Text("Take a new photo or choose one from your library.")
                .SecondaryText(),
            title: new Text("Profile photo").SubHeadline(),
            confirmButton: new Button("CAMERA", _camera)
                .TextButton()
                .Color(CoffeeTheme.PrimaryColor)
                .CornerRadius(0)
                .IsEnabled(_isEnabled)
                .AutomationId("profile_photo_camera"),
            dismissButton: new Button("GALLERY", _gallery)
                .TextButton()
                .Color(CoffeeTheme.TextPrimary)
                .CornerRadius(0)
                .IsEnabled(_isEnabled)
                .AutomationId("profile_photo_gallery"))
            .AutomationId("profile_photo_source_dialog");
}

public sealed class ProfileConfirmationOverlay : View
{
    readonly Signal<bool> _isOpen;
    readonly string _title;
    readonly string _message;
    readonly string _confirmLabel;
    readonly Action _confirm;
    readonly string _dismissLabel;
    readonly Action _dismiss;
    readonly string _automationId;
    readonly bool _danger;
    readonly bool _isEnabled;

    public ProfileConfirmationOverlay(
        Signal<bool> isOpen,
        string title,
        string message,
        string confirmLabel,
        Action confirm,
        string dismissLabel,
        Action dismiss,
        string automationId,
        bool danger = false,
        bool isEnabled = true)
    {
        _isOpen = isOpen;
        _title = title;
        _message = message;
        _confirmLabel = confirmLabel;
        _confirm = confirm;
        _dismissLabel = dismissLabel;
        _dismiss = dismiss;
        _automationId = automationId;
        _danger = danger;
        _isEnabled = isEnabled;
    }

    [Body]
    View body()
        => new AlertDialog(
            _isOpen,
            text: new Text(_message)
                .SecondaryText()
                .AutomationId($"{_automationId}_message"),
            title: new Text(_title).SubHeadline(),
            confirmButton: new Button(_confirmLabel, _confirm)
                .TextButton()
                .Color(_danger ? CoffeeTheme.Error : CoffeeTheme.PrimaryColor)
                .CornerRadius(0)
                .IsEnabled(_isEnabled)
                .AutomationId($"{_automationId}_confirm"),
            dismissButton: new Button(_dismissLabel, _dismiss)
                .TextButton()
                .Color(CoffeeTheme.TextPrimary)
                .CornerRadius(0)
                .IsEnabled(_isEnabled)
                .AutomationId($"{_automationId}_dismiss"))
            .AutomationId(_automationId);
}
