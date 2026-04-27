using System;
using Microsoft.Maui.Handlers;
using AppKit;
using Foundation;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public class DatePickerHandler : MacOSViewHandler<IDatePicker, NSDatePicker>
{
    public static readonly IPropertyMapper<IDatePicker, DatePickerHandler> Mapper =
        new PropertyMapper<IDatePicker, DatePickerHandler>(ViewMapper)
        {
            [nameof(IDatePicker.Date)] = MapDate,
            [nameof(IDatePicker.MinimumDate)] = MapMinimumDate,
            [nameof(IDatePicker.MaximumDate)] = MapMaximumDate,
            [nameof(IDatePicker.TextColor)] = MapTextColor,
            [nameof(IDatePicker.Format)] = MapFormat,
        };

    bool _updating;

    public DatePickerHandler() : base(Mapper) { }

    protected override NSDatePicker CreatePlatformView()
    {
        var picker = new NSDatePicker
        {
            DatePickerStyle = NSDatePickerStyle.TextFieldAndStepper,
            DatePickerElements = NSDatePickerElementFlags.YearMonthDateDay,
            DatePickerMode = NSDatePickerMode.Single,
            DateValue = (NSDate)DateTime.Now,
            Bezeled = true,
        };
        return picker;
    }

    protected override void ConnectHandler(NSDatePicker platformView)
    {
        base.ConnectHandler(platformView);
        platformView.Activated += OnDateChanged;
    }

    protected override void DisconnectHandler(NSDatePicker platformView)
    {
        platformView.Activated -= OnDateChanged;
        base.DisconnectHandler(platformView);
    }

    void OnDateChanged(object? sender, EventArgs e)
    {
        if (_updating || VirtualView == null)
            return;

        _updating = true;
        try
        {
            VirtualView.Date = (DateTime)PlatformView.DateValue;
        }
        finally
        {
            _updating = false;
        }
    }

    static NSDate ToNSDate(DateTime dt)
    {
        if (dt.Kind == DateTimeKind.Unspecified)
            dt = DateTime.SpecifyKind(dt, DateTimeKind.Local);
        return (NSDate)dt;
    }

    public static void MapDate(DatePickerHandler handler, IDatePicker datePicker)
    {
        if (handler._updating)
            return;

        if (datePicker.Date is DateTime date)
            handler.PlatformView.DateValue = ToNSDate(date);
    }

    public static void MapMinimumDate(DatePickerHandler handler, IDatePicker datePicker)
    {
        if (datePicker.MinimumDate is DateTime min)
            handler.PlatformView.MinDate = ToNSDate(min);
    }

    public static void MapMaximumDate(DatePickerHandler handler, IDatePicker datePicker)
    {
        if (datePicker.MaximumDate is DateTime max)
            handler.PlatformView.MaxDate = ToNSDate(max);
    }

    public static void MapTextColor(DatePickerHandler handler, IDatePicker datePicker)
    {
        if (datePicker.TextColor is not null)
            handler.PlatformView.TextColor = datePicker.TextColor.ToPlatformColor();
    }

    public static void MapFormat(DatePickerHandler handler, IDatePicker datePicker)
    {
        var format = datePicker.Format;
        if (string.IsNullOrEmpty(format))
            return;

        // Map common .NET date format strings to NSDatePicker element flags
        var elements = NSDatePickerElementFlags.YearMonthDateDay;
        if (format.Contains('H') || format.Contains('h') || format.Contains('t'))
            elements |= NSDatePickerElementFlags.HourMinuteSecond;

        handler.PlatformView.DatePickerElements = elements;
    }
}
