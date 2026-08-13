namespace Microsoft.Maui.Chat.Controls;

/// <summary>
/// The single binding source for chat styling: avatar, participant name, timestamp, status, and bubble
/// options shared by every rendered row.
/// </summary>
/// <remarks>
/// <para>
/// Colour properties are <see langword="null"/> by default, which means "use the theme". Set one to
/// override just that colour without losing the rest of the theme, including its light and dark
/// variants. Everything else has a concrete default so templates can bind without null checks.
/// </para>
/// <para>Like every model in this package it is single-thread affine and not thread-safe.</para>
/// </remarks>
public class ChatAppearance : BindableObject
{
    /// <summary>
    /// Gets the shared appearance used when a host does not supply one. Mutating it affects every row
    /// that has no explicit appearance, so prefer assigning your own instance.
    /// </summary>
    public static ChatAppearance Default { get; } = new();

    /// <summary>Backing property for <see cref="ShowAvatars"/>.</summary>
    public static readonly BindableProperty ShowAvatarsProperty =
        BindableProperty.Create(nameof(ShowAvatars), typeof(bool), typeof(ChatAppearance), true);

    /// <summary>Backing property for <see cref="AvatarSize"/>.</summary>
    public static readonly BindableProperty AvatarSizeProperty =
        BindableProperty.Create(nameof(AvatarSize), typeof(double), typeof(ChatAppearance), 32.0);

    /// <summary>Backing property for <see cref="ShowParticipantNames"/>.</summary>
    public static readonly BindableProperty ShowParticipantNamesProperty =
        BindableProperty.Create(nameof(ShowParticipantNames), typeof(bool), typeof(ChatAppearance), true);

    /// <summary>Backing property for <see cref="ShowTimestamps"/>.</summary>
    public static readonly BindableProperty ShowTimestampsProperty =
        BindableProperty.Create(nameof(ShowTimestamps), typeof(bool), typeof(ChatAppearance), true);

    /// <summary>Backing property for <see cref="TimestampFormat"/>.</summary>
    public static readonly BindableProperty TimestampFormatProperty =
        BindableProperty.Create(nameof(TimestampFormat), typeof(string), typeof(ChatAppearance), "t");

    /// <summary>Backing property for <see cref="ShowMessageStatus"/>.</summary>
    public static readonly BindableProperty ShowMessageStatusProperty =
        BindableProperty.Create(nameof(ShowMessageStatus), typeof(bool), typeof(ChatAppearance), true);

    /// <summary>Backing property for <see cref="BubbleCornerRadius"/>.</summary>
    public static readonly BindableProperty BubbleCornerRadiusProperty =
        BindableProperty.Create(nameof(BubbleCornerRadius), typeof(double), typeof(ChatAppearance), 18.0);

    /// <summary>Backing property for <see cref="BubbleStrokeThickness"/>.</summary>
    public static readonly BindableProperty BubbleStrokeThicknessProperty =
        BindableProperty.Create(nameof(BubbleStrokeThickness), typeof(double), typeof(ChatAppearance), 0.0);

    /// <summary>Backing property for <see cref="BubbleStrokeColor"/>.</summary>
    public static readonly BindableProperty BubbleStrokeColorProperty =
        BindableProperty.Create(nameof(BubbleStrokeColor), typeof(Color), typeof(ChatAppearance));

    /// <summary>Backing property for <see cref="MaxBubbleWidth"/>.</summary>
    public static readonly BindableProperty MaxBubbleWidthProperty =
        BindableProperty.Create(nameof(MaxBubbleWidth), typeof(double), typeof(ChatAppearance), 360.0);

    /// <summary>Backing property for <see cref="ContentSpacing"/>.</summary>
    public static readonly BindableProperty ContentSpacingProperty =
        BindableProperty.Create(nameof(ContentSpacing), typeof(double), typeof(ChatAppearance), 2.0);

    /// <summary>Backing property for <see cref="GroupSpacing"/>.</summary>
    public static readonly BindableProperty GroupSpacingProperty =
        BindableProperty.Create(nameof(GroupSpacing), typeof(double), typeof(ChatAppearance), 10.0);

    /// <summary>Backing property for <see cref="IncomingBubbleColor"/>.</summary>
    public static readonly BindableProperty IncomingBubbleColorProperty =
        BindableProperty.Create(nameof(IncomingBubbleColor), typeof(Color), typeof(ChatAppearance));

    /// <summary>Backing property for <see cref="OutgoingBubbleColor"/>.</summary>
    public static readonly BindableProperty OutgoingBubbleColorProperty =
        BindableProperty.Create(nameof(OutgoingBubbleColor), typeof(Color), typeof(ChatAppearance));

    /// <summary>Backing property for <see cref="IncomingTextColor"/>.</summary>
    public static readonly BindableProperty IncomingTextColorProperty =
        BindableProperty.Create(nameof(IncomingTextColor), typeof(Color), typeof(ChatAppearance));

    /// <summary>Backing property for <see cref="OutgoingTextColor"/>.</summary>
    public static readonly BindableProperty OutgoingTextColorProperty =
        BindableProperty.Create(nameof(OutgoingTextColor), typeof(Color), typeof(ChatAppearance));

    /// <summary>Gets or sets whether participant avatars are shown. Defaults to <see langword="true"/>.</summary>
    public bool ShowAvatars
    {
        get => (bool)GetValue(ShowAvatarsProperty);
        set => SetValue(ShowAvatarsProperty, value);
    }

    /// <summary>Gets or sets the avatar width and height in device-independent units. Defaults to <c>32</c>.</summary>
    public double AvatarSize
    {
        get => (double)GetValue(AvatarSizeProperty);
        set => SetValue(AvatarSizeProperty, value);
    }

    /// <summary>Gets or sets whether the participant name is shown above the first bubble of a group.</summary>
    public bool ShowParticipantNames
    {
        get => (bool)GetValue(ShowParticipantNamesProperty);
        set => SetValue(ShowParticipantNamesProperty, value);
    }

    /// <summary>Gets or sets whether the timestamp is shown under the last bubble of a group.</summary>
    public bool ShowTimestamps
    {
        get => (bool)GetValue(ShowTimestampsProperty);
        set => SetValue(ShowTimestampsProperty, value);
    }

    /// <summary>Gets or sets the format string used for timestamps. Defaults to the short time pattern (<c>"t"</c>).</summary>
    public string TimestampFormat
    {
        get => (string)GetValue(TimestampFormatProperty);
        set => SetValue(TimestampFormatProperty, value);
    }

    /// <summary>Gets or sets whether the delivery status is shown for outgoing messages.</summary>
    public bool ShowMessageStatus
    {
        get => (bool)GetValue(ShowMessageStatusProperty);
        set => SetValue(ShowMessageStatusProperty, value);
    }

    /// <summary>Gets or sets the bubble corner radius. Defaults to <c>18</c>.</summary>
    public double BubbleCornerRadius
    {
        get => (double)GetValue(BubbleCornerRadiusProperty);
        set => SetValue(BubbleCornerRadiusProperty, value);
    }

    /// <summary>Gets or sets the bubble border thickness. Defaults to <c>0</c>.</summary>
    public double BubbleStrokeThickness
    {
        get => (double)GetValue(BubbleStrokeThicknessProperty);
        set => SetValue(BubbleStrokeThicknessProperty, value);
    }

    /// <summary>Gets or sets the bubble border colour, or <see langword="null"/> to use the theme.</summary>
    public Color? BubbleStrokeColor
    {
        get => (Color?)GetValue(BubbleStrokeColorProperty);
        set => SetValue(BubbleStrokeColorProperty, value);
    }

    /// <summary>Gets or sets the maximum bubble width. Defaults to <c>360</c>.</summary>
    public double MaxBubbleWidth
    {
        get => (double)GetValue(MaxBubbleWidthProperty);
        set => SetValue(MaxBubbleWidthProperty, value);
    }

    /// <summary>Gets or sets the vertical spacing between rows of the same message. Defaults to <c>2</c>.</summary>
    public double ContentSpacing
    {
        get => (double)GetValue(ContentSpacingProperty);
        set => SetValue(ContentSpacingProperty, value);
    }

    /// <summary>Gets or sets the vertical spacing added after the last row of a participant group. Defaults to <c>10</c>.</summary>
    public double GroupSpacing
    {
        get => (double)GetValue(GroupSpacingProperty);
        set => SetValue(GroupSpacingProperty, value);
    }

    /// <summary>Gets or sets the incoming bubble colour, or <see langword="null"/> to use the theme.</summary>
    public Color? IncomingBubbleColor
    {
        get => (Color?)GetValue(IncomingBubbleColorProperty);
        set => SetValue(IncomingBubbleColorProperty, value);
    }

    /// <summary>Gets or sets the outgoing bubble colour, or <see langword="null"/> to use the theme.</summary>
    public Color? OutgoingBubbleColor
    {
        get => (Color?)GetValue(OutgoingBubbleColorProperty);
        set => SetValue(OutgoingBubbleColorProperty, value);
    }

    /// <summary>Gets or sets the incoming bubble text colour, or <see langword="null"/> to use the theme.</summary>
    public Color? IncomingTextColor
    {
        get => (Color?)GetValue(IncomingTextColorProperty);
        set => SetValue(IncomingTextColorProperty, value);
    }

    /// <summary>Gets or sets the outgoing bubble text colour, or <see langword="null"/> to use the theme.</summary>
    public Color? OutgoingTextColor
    {
        get => (Color?)GetValue(OutgoingTextColorProperty);
        set => SetValue(OutgoingTextColorProperty, value);
    }

    /// <summary>Formats a timestamp with <see cref="TimestampFormat"/> using the current culture.</summary>
    /// <param name="timestamp">The value to format.</param>
    /// <returns>The formatted timestamp, or an empty string when <see cref="ShowTimestamps"/> is <see langword="false"/>.</returns>
    public string FormatTimestamp(DateTimeOffset timestamp)
    {
        if (!ShowTimestamps)
            return string.Empty;

        var format = TimestampFormat;
        return string.IsNullOrEmpty(format)
            ? timestamp.ToString(System.Globalization.CultureInfo.CurrentCulture)
            : timestamp.ToString(format, System.Globalization.CultureInfo.CurrentCulture);
    }
}
