using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Asks to approve a <c>book_meeting</c> call, showing the meeting it would book.</summary>
public partial class MeetingApprovalView : ScenarioCardView
{
    public MeetingApprovalView() => InitializeComponent();

    protected override void Refresh()
    {
        if (TitleLabel is null || Block is not ToolApprovalBlock approval)
            return;

        var arguments = approval.Arguments;
        var title = ScenarioJson.ReadString(arguments, "title") ?? "Meeting";
        var start = ScenarioJson.ReadString(arguments, "start");
        var minutes = ScenarioJson.ReadInt(arguments, "durationMinutes");
        var attendees = arguments is not null && arguments.TryGetValue("attendees", out var value)
            ? ScenarioJson.AsStrings(value)
            : [];

        TitleLabel.Text = title;
        WhenLabel.Text = string.Join(" \u00B7 ", new[] { start, minutes is { } length ? $"{length} min" : null }
            .Where(static part => !string.IsNullOrWhiteSpace(part)));
        WhoLabel.Text = attendees.Count > 0 ? string.Join(", ", attendees) : "Just you";

        var booking = ScenarioJson.Read<MeetingBooking>(approval.Result?.Result);
        var running = IsRunning(approval);
        ButtonsRow.IsVisible = approval.IsAwaitingInput;
        (CaptionLabel.Text, StatusLabel.Text) = approval.Status switch
        {
            ApprovalStatus.Pending when approval.IsAwaitingInput => ("BOOK THIS MEETING?", "The agent waits for your decision."),
            ApprovalStatus.Pending => ("NOT BOOKED", IsStopped(approval)
                ? "Not decided: the turn was stopped."
                : "Not decided."),
            ApprovalStatus.Approved when booking is not null => ("BOOKED", $"\u2713 Booked \u00B7 confirmation {booking.Confirmation}"),
            ApprovalStatus.Approved when running => ("BOOKING", "Approved. Booking\u2026"),
            ApprovalStatus.Approved => ("APPROVED", "Approved."),
            _ => ("NOT BOOKED", "\u2715 You rejected it, so nothing was booked."),
        };
        StatusLabel.SetDynamicResource(Label.TextColorProperty, approval.Status switch
        {
            ApprovalStatus.Approved when booking is not null => AIChatThemeKeys.SuccessColor,
            _ => AIChatThemeKeys.SecondaryTextColor,
        });
        SemanticProperties.SetDescription(Card,
            $"{CaptionLabel.Text}: {title}, {WhenLabel.Text}, with {WhoLabel.Text}. {StatusLabel.Text}");
    }

    private void ApproveClicked(object? sender, EventArgs e)
    {
        if (Block is ToolApprovalBlock { IsAwaitingInput: true } approval)
            approval.Approve();
    }

    private void RejectClicked(object? sender, EventArgs e)
    {
        if (Block is ToolApprovalBlock { IsAwaitingInput: true } approval)
            approval.Reject("The user chose not to book this meeting.");
    }
}
