#nullable enable
using System;
using System.Threading.Tasks;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using CometSamples.BaristaNotes.Styles;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;

namespace CometSamples.BaristaNotes.Components;

/// <summary>
/// Real AI advice panel that calls <see cref="IAIAdviceService.GetAdviceForShotAsync"/>
/// for the current persisted shot. Renders distinct states: loading, unavailable,
/// cancelled, failed, and success (adjustments + reasoning + source).
/// </summary>
public sealed class AIAdvicePanel : View
{
    readonly Action _dismiss;
    readonly AIAdviceRequestSession _request;
    readonly Signal<AIAdviceRequestStatus> _status = new(AIAdviceRequestStatus.Idle);
    readonly Signal<string> _message = new(string.Empty);
    readonly Signal<string> _reasoning = new(string.Empty);
    readonly Signal<string> _source = new(string.Empty);
    readonly Signal<string> _adjustments = new(string.Empty);
    bool _startQueued;

    public AIAdvicePanel(Func<int?> getShotId, Action dismiss)
    {
        _dismiss = dismiss;
        _request = new AIAdviceRequestSession(
            BaristaServiceLocator.AIAdviceService,
            getShotId,
            ApplyState,
            ThreadHelper.RunOnMainThread);
    }

    public void RequestAdvice()
    {
        _startQueued = true;
        _ = _request.StartAsync();
    }

    public void CancelAdvice() => _request.Cancel();

    async Task StartAfterInitialRenderAsync()
    {
        await Task.Yield();
        await _request.StartAsync();
    }

    void ApplyState(AIAdviceRequestState state)
    {
        _status.Value = state.Status;
        _message.Value = state.Message;

        if (state.Status != AIAdviceRequestStatus.Success || state.Advice is null)
        {
            _adjustments.Value = string.Empty;
            _reasoning.Value = string.Empty;
            _source.Value = string.Empty;
            return;
        }

        var adjustmentLines = new System.Text.StringBuilder();
        foreach (var adjustment in state.Advice.Adjustments)
            adjustmentLines.AppendLine($"• {adjustment.Recommendation}");
        _adjustments.Value = adjustmentLines.ToString().TrimEnd();
        _reasoning.Value = state.Advice.Reasoning ?? string.Empty;
        _source.Value = state.Advice.Source ?? string.Empty;
    }

    [Body]
    View body()
    {
        var status = _status.Value;

        if (status == AIAdviceRequestStatus.Idle)
        {
            if (!_startQueued)
            {
                _startQueued = true;
                _ = StartAfterInitialRenderAsync();
            }
            return LoadingState();
        }

        return status switch
        {
            AIAdviceRequestStatus.Loading => LoadingState(),
            AIAdviceRequestStatus.Unavailable => InfoState("!", "Unavailable", _message.Value, "ai_unavailable"),
            AIAdviceRequestStatus.Cancelled => InfoState(CoffeeIcons.Close, "Cancelled", _message.Value, "ai_cancelled",
                retry: () => RequestAdvice()),
            AIAdviceRequestStatus.Failed => InfoState("!", "Error", _message.Value, "ai_failed",
                retry: () => RequestAdvice()),
            AIAdviceRequestStatus.Success => SuccessState(),
            _ => LoadingState(),
        };
    }

    View LoadingState() => new VStack(spacing: CoffeeSpacing.M)
    {
        new Text("…").FontFamily("ManropeSemibold").FontSize(48)
            .Color(CoffeeTheme.TextMuted).Center(),
        new Text("Analyzing your drink…")
            .FontFamily("ManropeSemibold").FontSize(16).Color(CoffeeTheme.TextPrimary).Center(),
        new Button("CANCEL", CancelAdvice)
            .TextButton().Color(CoffeeTheme.TextSecondary).CornerRadius(0)
            .AutomationId("ai_cancel"),
    }.Padding(new Thickness(CoffeeSpacing.L)).AutomationId("ai_loading");

    View InfoState(string icon, string title, string message, string automationId, Action? retry = null)
    {
        var stack = new VStack(spacing: CoffeeSpacing.S)
        {
            new Text(icon)
                .FontFamily(icon.Length == 1 && char.IsAscii(icon[0]) ? "ManropeSemibold" : CoffeeIcons.FontFamily)
                .FontSize(36).Color(CoffeeTheme.TextMuted).Center(),
            new Text(title).FontFamily("ManropeSemibold").FontSize(18).Color(CoffeeTheme.TextPrimary).Center(),
            new Text(message).FontFamily("Manrope").FontSize(14).Color(CoffeeTheme.TextSecondary).Center(),
        };
        if (retry is not null)
            stack.Add(new Button("TRY AGAIN", retry).TextButton().Color(CoffeeTheme.PrimaryColor).CornerRadius(0)
                .AutomationId($"{automationId}_retry"));
        stack.Add(new Button("CLOSE", _dismiss).TextButton().Color(CoffeeTheme.TextSecondary).CornerRadius(0));
        return stack.Padding(new Thickness(CoffeeSpacing.L)).AutomationId(automationId);
    }

    View SuccessState()
    {
        var stack = new VStack(spacing: CoffeeSpacing.M)
        {
            new Text("AI ADVICE").SectionLabel().Color(CoffeeTheme.TextSecondary),
            new Text(() => _message.Value).FontFamily("Manrope").FontSize(12)
                .Color(CoffeeTheme.TextMuted),
        };

        if (!string.IsNullOrWhiteSpace(_adjustments.Value))
        {
            stack.Add(new Text("ADJUSTMENTS").SectionLabel());
            stack.Add(new Text(() => _adjustments.Value)
                .FontFamily("ManropeSemibold").FontSize(16).Color(CoffeeTheme.TextPrimary));
        }

        if (!string.IsNullOrWhiteSpace(_reasoning.Value))
        {
            stack.Add(new Text("REASONING").SectionLabel());
            stack.Add(new Text(() => _reasoning.Value)
                .FontFamily("Manrope").FontSize(14).Color(CoffeeTheme.TextSecondary));
        }

        if (!string.IsNullOrWhiteSpace(_source.Value))
        {
            stack.Add(new Text("SOURCE").SectionLabel());
            stack.Add(new Text(() => _source.Value)
                .FontFamily("Manrope").FontSize(12).Color(CoffeeTheme.TextMuted));
        }

        stack.Add(new Button("CLOSE", _dismiss).TextButton().Color(CoffeeTheme.TextSecondary).CornerRadius(0));

        return new ScrollView
        {
            stack.Padding(new Thickness(CoffeeSpacing.L)),
        }.AutomationId("ai_success");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _request.Dispose();
        base.Dispose(disposing);
    }
}
